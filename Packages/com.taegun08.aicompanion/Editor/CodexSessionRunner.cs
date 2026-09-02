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
    // Codex prints benign startup/progress diagnostics to stderr even on a fully successful
    // turn (its banner, "Reading additional input from stdin...", etc.) - buffered here instead
    // of being treated as a fatal error the moment a line arrives; only promoted to OnError if
    // the process actually exits non-zero (see HandleLine's "__exited__" case). Previously any
    // stderr line at all fired OnError immediately, so switching to Codex reported "연동 실패"
    // on effectively every successful turn (2026-08-11 report - the reply still arrived right
    // after the false failure notice).
    private readonly StringBuilder stderrBuffer = new StringBuilder();
    private Process process;
    private string sessionId;
    private bool reloadLocked;
    private DateTime lastActivityUtc;
    // Both deferred until __exited__ instead of firing the instant the JSON line arrives - see
    // ClaudeSessionRunner's turnCompletePending comment for the full rationale
    // (CompanionSession.AdvanceQueueOrNotify sends a queued follow-up synchronously off
    // OnTurnComplete/OnError, and Runner.Send() silently no-ops while IsBusy is still true, which
    // only flips false on __exited__). pendingErrorMessage doubles as "is an error pending" via
    // null-check, so only one of the two should ever end up set for a given turn.
    private bool turnCompletePending;
    private string pendingErrorMessage;

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

        // The prompt is sent over stdin ("-") rather than as a command-line argument -
        // handoff turns pass the entire visible chat transcript (BuildHandoffContext), which
        // routinely exceeds cmd.exe's ~8191-character command-line limit ("codex.cmd" is a
        // batch shim, so .NET launches it through cmd.exe even with UseShellExecute=false).
        // Going over that limit doesn't throw here - cmd.exe itself rejects the command line
        // and codex exits non-zero with "명령줄이 너무 깁니다." on stderr, which then showed up
        // mojibake'd (see StandardErrorEncoding below) as "연동 실패" on effectively every
        // provider switch once the conversation got long (2026-08-11 report).
        //
        // Headless (no interactive stdin otherwise), so nothing can ever answer an interactive
        // approval prompt - --dangerously-bypass-approvals-and-sandbox is Codex's equivalent of
        // Claude's --permission-mode bypassPermissions, and the only mode that doesn't stall.
        StringBuilder args = new StringBuilder();
        args.Append("exec ");
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Append("resume ").Append(Quote(sessionId)).Append(' ');
        }
        args.Append("- --json --dangerously-bypass-approvals-and-sandbox");

        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = ResolveCodexExecutablePath(),
            Arguments = args.ToString(),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Encoding.UTF8,
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

        stderrBuffer.Clear();
        turnCompletePending = false;
        pendingErrorMessage = null;
        LockReload();
        IsBusy = true;
        lastActivityUtc = DateTime.UtcNow;

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Write the prompt to stdin and close it so codex sees EOF and starts the turn
            // instead of waiting for more input (see the "- --json" arg above).
            process.StandardInput.Write(message);
            process.StandardInput.Close();
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
                // See ClaudeSessionRunner.Kill()'s comment - codex.cmd is the same kind of
                // cmd.exe-wrapping batch shim, so a plain Kill() would only kill the wrapper and
                // leave the real codex process running (and still able to feed stale events into
                // outputQueue after this "cancel" was supposed to end the turn). ProcessTreeKiller
                // kills the whole tree via taskkill, since Process.Kill(entireProcessTree: true)
                // isn't available in this Editor's scripting runtime.
                ProcessTreeKiller.Kill(process.Id);
            }
        }
        catch (Exception)
        {
            // Process may have exited between the check and the kill; safe to ignore.
        }

        IsBusy = false;
        UnlockReload();
        // Cancelling always wins over a deferred outcome that arrived a moment before Kill() -
        // see ClaudeSessionRunner.Kill()'s matching comment.
        turnCompletePending = false;
        pendingErrorMessage = null;
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
            // Kill() first - it synchronously resets IsBusy/UnlockReload, so by the time this
            // OnError reaches CompanionSession (which may immediately try to send a queued
            // follow-up off the back of it), the runner is actually free to accept it instead of
            // Runner.Send() silently no-opping on a still-true IsBusy.
            Kill();
            OnError?.Invoke($"codex 프로세스가 {IdleTimeout.TotalMinutes}분 동안 응답이 없어 강제 종료합니다.");
        }
    }

    private void HandleLine(string line)
    {
        if (line == "__exited__")
        {
            IsBusy = false;
            UnlockReload();

            if (pendingErrorMessage != null)
            {
                string message = pendingErrorMessage;
                pendingErrorMessage = null;
                turnCompletePending = false;
                OnError?.Invoke(message);
            }
            else if (turnCompletePending)
            {
                turnCompletePending = false;
                OnTurnComplete?.Invoke();
            }
            // Only stderr from a process that actually failed is a real error - a clean exit
            // (code 0) means whatever it printed to stderr along the way was just diagnostic
            // noise (see stderrBuffer's declaration comment).
            else if (process != null && process.ExitCode != 0 && stderrBuffer.Length > 0)
            {
                OnError?.Invoke(stderrBuffer.ToString().Trim());
            }

            stderrBuffer.Clear();
            return;
        }

        if (line.StartsWith("__stderr__:"))
        {
            stderrBuffer.AppendLine(line.Substring("__stderr__:".Length));
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
            // Deferred to __exited__ - see turnCompletePending's declaration comment.
            turnCompletePending = true;
        }
        else if (type == "turn.failed")
        {
            JObject error = json["error"] as JObject;
            pendingErrorMessage = error?.Value<string>("message") ?? "알 수 없는 오류가 발생했습니다.";
        }
        else if (type == "error")
        {
            pendingErrorMessage = json.Value<string>("message") ?? "알 수 없는 오류가 발생했습니다.";
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
                // Deferred to __exited__ too - see turnCompletePending's declaration comment.
                pendingErrorMessage = item.Value<string>("message") ?? "알 수 없는 오류가 발생했습니다.";
                break;
        }
    }
}
