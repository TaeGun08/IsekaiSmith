using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;

public class ClaudeSessionRunner
{
    public event Action<string> OnSessionStarted;
    public event Action<string> OnAssistantText;
    public event Action<string> OnToolActivity;
    public event Action OnTurnComplete;
    public event Action<string> OnError;

    public bool IsBusy { get; private set; }

    private readonly string workingDirectory;
    private readonly ConcurrentQueue<string> outputQueue = new ConcurrentQueue<string>();
    private Process process;
    private string sessionId;

    public ClaudeSessionRunner(string workingDirectory)
    {
        this.workingDirectory = workingDirectory;
        EditorApplication.update += Pump;
    }

    public void ResetSession()
    {
        sessionId = null;
    }

    public void Send(string message, bool autoProceed)
    {
        if (IsBusy)
        {
            return;
        }

        string permissionMode = autoProceed ? "bypassPermissions" : "acceptEdits";

        StringBuilder args = new StringBuilder();
        args.Append("-p ").Append(Quote(message));
        args.Append(" --output-format stream-json --verbose");
        if (!string.IsNullOrEmpty(sessionId))
        {
            args.Append(" --resume ").Append(Quote(sessionId));
        }
        args.Append(" --permission-mode ").Append(permissionMode);

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
            IsBusy = false;
        };

        IsBusy = true;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public void Kill()
    {
        if (process != null && !process.HasExited)
        {
            process.Kill();
        }
        IsBusy = false;
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
        while (outputQueue.TryDequeue(out string line))
        {
            HandleLine(line);
        }
    }

    private void HandleLine(string line)
    {
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
            JArray content = json["message"]?["content"] as JArray;
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
