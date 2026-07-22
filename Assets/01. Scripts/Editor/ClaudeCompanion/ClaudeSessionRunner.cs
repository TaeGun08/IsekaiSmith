using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;

// Token counts from one turn's "result" event (stream-json). Cache tokens are counted
// separately from the CLI's own cost accounting, but for a simple "how much did this use"
// display, all four buckets are real tokens spent - see TokenUsage.Total.
public readonly struct TokenUsage
{
    public readonly long InputTokens;
    public readonly long OutputTokens;
    public readonly long CacheCreationTokens;
    public readonly long CacheReadTokens;

    public TokenUsage(long inputTokens, long outputTokens, long cacheCreationTokens, long cacheReadTokens)
    {
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CacheCreationTokens = cacheCreationTokens;
        CacheReadTokens = cacheReadTokens;
    }

    public long Total => InputTokens + OutputTokens + CacheCreationTokens + CacheReadTokens;
}

public class ClaudeSessionRunner
{
    public event Action<string> OnSessionStarted;
    public event Action<string> OnAssistantText;
    public event Action<string> OnToolActivity;
    public event Action OnTurnComplete;
    public event Action<string> OnError;
    public event Action<TokenUsage> OnUsage;

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
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
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

    private static string ResolveClaudeExecutablePath()
    {
        if (cachedClaudePath != null)
        {
            return cachedClaudePath;
        }

        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] extensions = { ".exe", ".cmd", ".bat" };

        List<string> candidateDirs = new List<string>(pathEnv.Split(Path.PathSeparator));

        // Fall back to well-known install locations in case this Editor process's PATH
        // predates a claude install (Unity doesn't pick up PATH changes until restarted).
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        candidateDirs.Add(Path.Combine(userProfile, ".local", "bin"));
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        candidateDirs.Add(Path.Combine(appData, "npm"));

        foreach (string dir in candidateDirs)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                continue;
            }

            foreach (string ext in extensions)
            {
                string candidate = Path.Combine(dir.Trim(), "claude" + ext);
                if (File.Exists(candidate))
                {
                    cachedClaudePath = candidate;
                    return cachedClaudePath;
                }
            }
        }

        cachedClaudePath = "claude";
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

            // Each "assistant" line is one distinct model call in the agentic loop (initial
            // reply, then one more per tool_use/tool_result round-trip), and the Anthropic
            // message object it wraps carries that call's own "usage" - summing these as they
            // arrive gives a live-updating total through the whole turn (tool calls included)
            // instead of one jump at the very end. "result"'s own usage is the turn's aggregate
            // total, so it's intentionally NOT also added here - that would double-count.
            if (type == "assistant")
            {
                JToken usage = message?["usage"];
                if (usage != null)
                {
                    OnUsage?.Invoke(new TokenUsage(
                        usage.Value<long?>("input_tokens") ?? 0,
                        usage.Value<long?>("output_tokens") ?? 0,
                        usage.Value<long?>("cache_creation_input_tokens") ?? 0,
                        usage.Value<long?>("cache_read_input_tokens") ?? 0));
                }
            }

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
