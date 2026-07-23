using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;

// First concrete IAiSessionRunner - talks to the Claude Code CLI specifically (stream-json
// output format, --resume session semantics). Other providers (Codex/Cursor/Antigravity CLIs)
// get their own implementations of the same interface; nothing outside this class knows about
// the Claude-specific process args or JSON shape.
public class ClaudeSessionRunner : IAiSessionRunner
{
    public event Action<string> OnSessionStarted;
    public event Action<string> OnAssistantText;
    public event Action<string> OnToolActivity;
    public event Action OnTurnComplete;
    public event Action<string> OnError;

    // If a turn produces no output at all for this long, the claude process is assumed
    // stuck. Without this, a hung process keeps LockReloadAssemblies held forever, which
    // freezes script compilation for the whole editor, not just this window.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    public bool IsBusy { get; private set; }
    public string SessionId => sessionId;

    private readonly string workingDirectory;
    private readonly ConcurrentQueue<string> outputQueue = new ConcurrentQueue<string>();
    private Process process;
    private string sessionId;
    private bool reloadLocked;
    private DateTime lastActivityUtc;

    public ClaudeSessionRunner(string workingDirectory)
    {
        this.workingDirectory = workingDirectory;
        EditorApplication.update += Pump;
    }

    public void ResetSession()
    {
        sessionId = null;
    }

    /// <summary>
    /// Reattaches a previously known session id (e.g. after a domain reload recreated
    /// this runner) so the next Send() resumes the real Claude conversation instead of
    /// silently starting a new one while the on-screen chat history looks unchanged.
    /// </summary>
    public void RestoreSession(string knownSessionId)
    {
        sessionId = knownSessionId;
    }

    public void Send(string message)
    {
        if (IsBusy)
        {
            return;
        }

        // This process runs headless (-p, no stdin wired up), so there is no way to
        // answer an interactive permission prompt. "acceptEdits" only auto-approves
        // file edits and leaves every other tool call (Bash, UnityMCP tools like
        // manage_ui/manage_gameobject) waiting on a prompt nothing can ever answer,
        // which silently stalls mid-task. bypassPermissions is the only mode that
        // actually works in this architecture.
        StringBuilder args = new StringBuilder();
        args.Append("-p ").Append(Quote(message));
        args.Append(" --output-format stream-json --verbose");
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Append(" --resume ").Append(Quote(sessionId));
        }
        args.Append(" --permission-mode bypassPermissions");

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = ResolveClaudeExecutablePath(),
            Arguments = args.ToString(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputQueue.Enqueue(e.Data);
            }
        };
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                outputQueue.Enqueue("__stderr__:" + e.Data);
            }
        };
        process.Exited += (sender, e) =>
        {
            outputQueue.Enqueue("__exited__");
        };

        // Defer any pending domain reload (script recompile) until this turn finishes,
        // so a compile elsewhere in the project doesn't kill an in-flight response.
        LockReload();
        IsBusy = true;
        lastActivityUtc = DateTime.UtcNow;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            // IsInstalled() is checked before Send() is ever reachable from the UI (see
            // AiCompanionWindow.SubmitMessage/OfferInstallIfNeeded), but if the resolved path
            // still fails to launch (stale cache, permissions, etc.) this must not leave
            // LockReloadAssemblies held forever - that would freeze script compilation for the
            // whole editor, not just this window, with no obvious cause.
            IsBusy = false;
            UnlockReload();
            OnError?.Invoke("claude 실행에 실패했습니다: " + ex.Message);
        }
    }

    public void Kill()
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception)
        {
            // Process may have exited between the check and the kill; safe to ignore.
        }

        IsBusy = false;
        UnlockReload();
    }

    private void LockReload()
    {
        if (!reloadLocked)
        {
            EditorApplication.LockReloadAssemblies();
            reloadLocked = true;
        }
    }

    private void UnlockReload()
    {
        if (reloadLocked)
        {
            EditorApplication.UnlockReloadAssemblies();
            reloadLocked = false;
        }
    }

    public void Dispose()
    {
        EditorApplication.update -= Pump;
        Kill();
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

private static string cachedClaudePath;

    // The npm package this Editor would install to get the "claude" command - see
    // AiProviderRegistry (Claude's IsInstalled/InstallPackage) and CliInstaller.
    public const string NpmPackage = "@anthropic-ai/claude-code";

    // Definitive yes/no (unlike ResolveClaudeExecutablePath's fallback-to-bare-"claude"), so a
    // caller can decide whether to offer an install prompt before ever trying to run anything.
    public static bool IsInstalled()
    {
        return CliInstaller.FindExecutable("claude") != null;
    }

    // Clears the cached path so the next Send() re-resolves - call after a successful install,
    // otherwise this process would keep using the pre-install "not found" result all session.
    public static void ClearResolvedPathCache()
    {
        cachedClaudePath = null;
    }

    private static string ResolveClaudeExecutablePath()
    {
        if (cachedClaudePath != null)
        {
            return cachedClaudePath;
        }

        cachedClaudePath = CliInstaller.FindExecutable("claude") ?? "claude";
        return cachedClaudePath;
    }


    private void Pump()
    {
        bool sawOutput = false;
        while (outputQueue.TryDequeue(out string line))
        {
            sawOutput = true;
            HandleLine(line);
        }

        if (sawOutput)
        {
            lastActivityUtc = DateTime.UtcNow;
        }
        else if (IsBusy && DateTime.UtcNow - lastActivityUtc > IdleTimeout)
        {
            OnError?.Invoke($"claude 프로세스가 {IdleTimeout.TotalMinutes}분 동안 응답이 없어 강제 종료합니다.");
            Kill();
        }
    }

    private void HandleLine(string line)
    {
        if (line == "__exited__")
        {
            IsBusy = false;
            UnlockReload();
            return;
        }

        if (line.StartsWith("__stderr__:"))
        {
            OnError?.Invoke(line.Substring("__stderr__:".Length));
            return;
        }

        JObject json;
        try
        {
            json = JObject.Parse(line);
        }
        catch (Exception)
        {
            return;
        }

        string sid = json.Value<string>("session_id");
        if (!string.IsNullOrEmpty(sid) && sid != sessionId)
        {
            sessionId = sid;
            OnSessionStarted?.Invoke(sessionId);
        }

        string type = json.Value<string>("type");

        if (type == "assistant" || type == "user")
        {
            JObject message = json["message"] as JObject;

            JArray content = message?["content"] as JArray;
            if (content != null)
            {
                foreach (JToken block in content)
                {
                    string blockType = block.Value<string>("type");
                    if (blockType == "text")
                    {
                        string text = block.Value<string>("text");
                        if (!string.IsNullOrEmpty(text))
                        {
                            OnAssistantText?.Invoke(text);
                        }
                    }
                    else if (blockType == "tool_use")
                    {
                        OnToolActivity?.Invoke($"tool_use: {block.Value<string>("name")}");
                    }
                    else if (blockType == "tool_result")
                    {
                        OnToolActivity?.Invoke("tool_result received");
                    }
                }
            }
        }
        else if (type == "result")
        {
            OnTurnComplete?.Invoke();
        }
        else if (type == "system")
        {
            OnToolActivity?.Invoke($"system: {json.Value<string>("subtype")}");
        }
    }
}
