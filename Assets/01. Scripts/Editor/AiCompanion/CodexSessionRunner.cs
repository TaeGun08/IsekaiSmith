using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;

// Second concrete IAiSessionRunner - talks to the OpenAI Codex CLI (`codex exec --json` JSONL
// event stream, `codex exec resume <thread_id>` session semantics). Mirrors ClaudeSessionRunner's
// process-pump architecture; only the CLI invocation shape and JSON event names differ, so read
// that file first if this one is confusing.
public class CodexSessionRunner : IAiSessionRunner
{
    public event Action<string> OnSessionStarted;
    public event Action<string> OnAssistantText;
    public event Action<string> OnToolActivity;
    public event Action OnTurnComplete;
    public event Action<string> OnError;

    // Same rationale as ClaudeSessionRunner: a stuck headless process would hold
    // LockReloadAssemblies forever and freeze compilation for the whole editor.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    public bool IsBusy { get; private set; }
    public string SessionId => sessionId;

    private readonly string workingDirectory;
    private readonly ConcurrentQueue<string> outputQueue = new ConcurrentQueue<string>();
    private Process process;
    private string sessionId;
    private bool reloadLocked;
    private DateTime lastActivityUtc;

    public CodexSessionRunner(string workingDirectory)
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

        // Headless (no stdin wired up), so nothing can ever answer an interactive approval
        // prompt - --dangerously-bypass-approvals-and-sandbox is Codex's equivalent of
        // Claude's --permission-mode bypassPermissions, and the only mode that doesn't stall.
        StringBuilder args = new StringBuilder();
        args.Append("exec ");
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Append("resume ").Append(Quote(sessionId)).Append(' ');
        }
        args.Append(Quote(message));
        args.Append(" --json --dangerously-bypass-approvals-and-sandbox");

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = ResolveCodexExecutablePath(),
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
            OnError?.Invoke("codex 실행에 실패했습니다: " + ex.Message);
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

    private static string cachedCodexPath;

    // The npm package this Editor would install to get the "codex" command - see
    // AiProviderRegistry (Codex's IsInstalled/InstallPackage) and CliInstaller.
    public const string NpmPackage = "@openai/codex";

    public static bool IsInstalled()
    {
        return CliInstaller.FindExecutable("codex") != null;
    }

    // Clears the cached path so the next Send() re-resolves - call after a successful install,
    // otherwise this process would keep using the pre-install "not found" result all session.
    public static void ClearResolvedPathCache()
    {
        cachedCodexPath = null;
    }

    private static string ResolveCodexExecutablePath()
    {
        if (cachedCodexPath != null)
        {
            return cachedCodexPath;
        }

        cachedCodexPath = CliInstaller.FindExecutable("codex") ?? "codex";
        return cachedCodexPath;
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
            OnError?.Invoke($"codex 프로세스가 {IdleTimeout.TotalMinutes}분 동안 응답이 없어 강제 종료합니다.");
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

        string type = json.Value<string>("type");

        if (type == "thread.started")
        {
            string tid = json.Value<string>("thread_id");
            if (!string.IsNullOrEmpty(tid) && tid != sessionId)
            {
                sessionId = tid;
                OnSessionStarted?.Invoke(sessionId);
            }
        }
        else if (type == "item.completed")
        {
            HandleCompletedItem(json["item"] as JObject);
        }
        else if (type == "turn.completed")
        {
            OnTurnComplete?.Invoke();
        }
        else if (type == "turn.failed")
        {
            JObject error = json["error"] as JObject;
            OnError?.Invoke(error?.Value<string>("message") ?? "알 수 없는 오류가 발생했습니다.");
        }
        else if (type == "error")
        {
            OnError?.Invoke(json.Value<string>("message") ?? "알 수 없는 오류가 발생했습니다.");
        }
    }

    private void HandleCompletedItem(JObject item)
    {
        if (item == null)
        {
            return;
        }

        string itemType = item.Value<string>("type");
        switch (itemType)
        {
            case "agent_message":
                string text = item.Value<string>("text");
                if (!string.IsNullOrEmpty(text))
                {
                    OnAssistantText?.Invoke(text);
                }
                break;
            case "command_execution":
                OnToolActivity?.Invoke($"tool_use: {item.Value<string>("command") ?? "command"}");
                break;
            case "mcp_tool_call":
                OnToolActivity?.Invoke($"tool_use: {item.Value<string>("tool") ?? "mcp"}");
                break;
            case "web_search":
                OnToolActivity?.Invoke("tool_use: web_search");
                break;
            case "file_change":
            case "file_changes":
                OnToolActivity?.Invoke("tool_result received");
                break;
            case "error":
                OnError?.Invoke(item.Value<string>("message") ?? "알 수 없는 오류가 발생했습니다.");
                break;
        }
    }
}
