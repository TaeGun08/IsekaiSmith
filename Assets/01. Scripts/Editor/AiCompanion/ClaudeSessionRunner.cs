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
    // Same rationale as CodexSessionRunner's stderrBuffer: a CLI can print benign diagnostics to
    // stderr on a fully successful turn, so a line landing here isn't promoted to a real OnError
    // until the process actually exits non-zero (see HandleLine's "__exited__" case).
    private readonly StringBuilder stderrBuffer = new StringBuilder();
    private Process process;
    private string sessionId;
    private bool reloadLocked;
    private DateTime lastActivityUtc;
    // Set when the "result" line arrives, but OnTurnComplete itself isn't fired until __exited__
    // (see HandleLine) - CompanionSession.AdvanceQueueOrNotify sends the next queued message
    // synchronously off OnTurnComplete, and Runner.Send() silently no-ops while IsBusy is still
    // true. IsBusy only flips to false on __exited__, which (in the same Pump() drain) is
    // typically processed right after "result" - so firing OnTurnComplete that early meant a
    // queued follow-up message got dropped on the floor with no error, ever (2026-08-20 report:
    // "입력을 보냈는데 갑자기 대기중으로 변경").
    private bool turnCompletePending;

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

        // The prompt is sent over stdin (bare "-p", no argument) rather than as a command-line
        // argument - handoff turns pass the entire visible chat transcript
        // (CompanionSession.BuildHandoffContext), which routinely exceeds cmd.exe's
        // ~8191-character command-line limit ("claude.cmd" is a batch shim, so .NET launches it
        // through cmd.exe even with UseShellExecute=false). Going over that limit doesn't throw
        // here - cmd.exe itself rejects the command line and the process exits non-zero with a
        // system-locale error on stderr, which then showed up mojibake'd (see
        // StandardErrorEncoding below) as "연동 실패" on effectively every provider switch once
        // the conversation got long (2026-08-11 report, first diagnosed against CodexSessionRunner).
        //
        // This process runs headless (no interactive stdin otherwise), so there is no way to
        // answer an interactive permission prompt. "acceptEdits" only auto-approves
        // file edits and leaves every other tool call (Bash, UnityMCP tools like
        // manage_ui/manage_gameobject) waiting on a prompt nothing can ever answer,
        // which silently stalls mid-task. bypassPermissions is the only mode that
        // actually works in this architecture.
        StringBuilder args = new StringBuilder();
        args.Append("-p");
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
            // Write the prompt to stdin and close it so claude sees EOF and starts the turn
            // instead of waiting for more input (see the bare "-p" arg above).
            process.StandardInput.Write(message);
            process.StandardInput.Close();
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
                // ResolveClaudeExecutablePath() resolves to claude.cmd on Windows, a batch shim
                // .NET launches through cmd.exe (see the comment on Send()'s command-line-length
                // limit above), so the tracked `process` is that cmd.exe wrapper, not the real
                // claude/node process running underneath it. A plain Kill() only killed the
                // wrapper - the actual CLI kept running in the background, unaware anything had
                // "cancelled" it, and its later __exited__/result output still landed in this same
                // outputQueue and got misread as belonging to whatever turn was running by then
                // (surfacing as the character snapping back to Idle mid-turn - user report,
                // 2026-08-20). ProcessTreeKiller kills the whole tree via taskkill - see its own
                // comment for why Process.Kill(entireProcessTree: true) isn't usable here.
                ProcessTreeKiller.Kill(process.Id);
            }
        }
        catch (Exception)
        {
            // Process may have exited between the check and the kill; safe to ignore.
        }

        IsBusy = false;
        UnlockReload();
        // Cancelling always wins, even if "result" had already arrived a moment before Kill() -
        // otherwise the killed process's later __exited__ would still fire a deferred
        // OnTurnComplete for a turn the caller (CompanionSession.CancelTurn) already treated as
        // cancelled and advanced past.
        turnCompletePending = false;
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
            // Kill() first - it synchronously resets IsBusy/UnlockReload, so by the time this
            // OnError reaches CompanionSession (which may immediately try to send a queued
            // follow-up off the back of it), the runner is actually free to accept it instead of
            // Runner.Send() silently no-opping on a still-true IsBusy.
            Kill();
            OnError?.Invoke($"claude 프로세스가 {IdleTimeout.TotalMinutes}분 동안 응답이 없어 강제 종료합니다.");
        }
    }

    private void HandleLine(string line)
    {
        if (line == "__exited__")
        {
            IsBusy = false;
            UnlockReload();

            if (turnCompletePending)
            {
                // The reply already arrived successfully ("result" was seen) - fire now that
                // IsBusy is actually false, so a queued follow-up message (see turnCompletePending's
                // declaration comment) can really be sent instead of silently dropped. A stray
                // nonzero exit code after a successful reply isn't worth second-guessing here,
                // same spirit as the stderr-buffering below.
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
            // Deferred to __exited__ (see turnCompletePending's declaration comment) instead of
            // firing immediately here - IsBusy is still true at this exact point.
            turnCompletePending = true;
        }
        else if (type == "system")
        {
            OnToolActivity?.Invoke($"system: {json.Value<string>("subtype")}");
        }
    }
}
