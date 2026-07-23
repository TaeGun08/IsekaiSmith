using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;

// Third concrete IAiSessionRunner - talks to the Cursor CLI (`cursor-agent -p --output-format
// stream-json`). Cursor's stream-json event shape ("type":"assistant"/"user" with a nested
// message.content[] array, plus a top-level "session_id" and a terminal "type":"result") is
// effectively the same convention Claude Code's CLI uses, so the JSON parsing below is almost
// identical to ClaudeSessionRunner's - only the process invocation differs.
public class CursorSessionRunner : IAiSessionRunner
{
    public event Action<string> OnSessionStarted;
    public event Action<string> OnAssistantText;
    public event Action<string> OnToolActivity;
    public event Action OnTurnComplete;
    public event Action<string> OnError;

    // Same rationale as ClaudeSessionRunner: a stuck headless process would hold
    // LockReloadAssemblies forever and freeze compilation for the whole editor. Cursor's -p
    // mode has known reports of hanging with zero output on some versions, so this matters here.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    public bool IsBusy { get; private set; }
    public string SessionId => sessionId;

    private readonly string workingDirectory;
    private readonly ConcurrentQueue<string> outputQueue = new ConcurrentQueue<string>();
    private Process process;
    private string sessionId;
    private bool reloadLocked;
    private DateTime lastActivityUtc;

    public CursorSessionRunner(string workingDirectory)
    {
        this.workingDirectory = workingDirectory;
        EditorApplication.update += Pump;
    }

    public void ResetSession()
    {
        sessionId = null;
    }

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

        // Headless (no stdin wired up) - "--force" is Cursor's equivalent of Claude's
        // --permission-mode bypassPermissions / Codex's --dangerously-bypass-approvals-and-
        // sandbox: without it, file-modification confirmation prompts would stall forever
        // since nothing can answer them.
        StringBuilder args = new StringBuilder();
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Append("--resume ").Append(Quote(sessionId)).Append(' ');
        }
        args.Append("-p --force --output-format stream-json ").Append(Quote(message));

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = ResolveCursorExecutablePath(),
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
            IsBusy = false;
            UnlockReload();
            OnError?.Invoke("cursor-agent 실행에 실패했습니다: " + ex.Message);
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

    private static string cachedCursorPath;

    // Unlike Claude/Codex, Cursor's official install is a curl|bash script, not an npm package
    // (see AiProviderRegistry - Cursor's InstallPackage is deliberately left null so the "no
    // installer wired up" dialog path is used instead of attempting a broken npm install).
    public static bool IsInstalled()
    {
        return CliInstaller.FindExecutable("cursor-agent") != null;
    }

    public static void ClearResolvedPathCache()
    {
        cachedCursorPath = null;
    }

    private static string ResolveCursorExecutablePath()
    {
        if (cachedCursorPath != null)
        {
            return cachedCursorPath;
        }

        cachedCursorPath = CliInstaller.FindExecutable("cursor-agent") ?? "cursor-agent";
        return cachedCursorPath;
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
            OnError?.Invoke($"cursor-agent 프로세스가 {IdleTimeout.TotalMinutes}분 동안 응답이 없어 강제 종료합니다.");
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
        else if (type == "tool_call")
        {
            OnToolActivity?.Invoke($"tool_use: {json.Value<string>("subtype") ?? "call"}");
        }
        else if (type == "result")
        {
            OnTurnComplete?.Invoke();
        }
    }
}
