using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// One log file per Companion session/tab, keyed by a locally generated id that stays stable
// across domain reloads (unlike the underlying AI CLI's own session id, which changes on
// reset) - this is what lets multiple concurrent sessions keep separate histories instead of
// interleaving into one file.
public class CompanionLog
{
    private readonly string sessionKey;
    private readonly string logDirectory;
    private readonly string logPath;

    public string FilePath => logPath;

    public CompanionLog(string sessionKey)
    {
        this.sessionKey = sessionKey;
        // Folder name kept as "ClaudeCompanion" (see the matching note on AiCompanionWindow.
        // ManifestPath) so this still finds everyone's existing on-disk chat logs after the
        // 2026-07-23 rebrand instead of orphaning them under a path nothing looks at anymore.
        logDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "ClaudeCompanion");
        logPath = Path.Combine(logDirectory, $"session-log-{sessionKey}.txt");
        MigrateLegacyLogIfNeeded();
    }

    // Before multi-session support, every window shared one fixed "session-log.txt". Adopt
    // it into this (the first) session's keyed file so existing chat history isn't silently
    // orphaned by the rename.
    private void MigrateLegacyLogIfNeeded()
    {
        try
        {
            string legacyPath = Path.Combine(logDirectory, "session-log.txt");
            if (!File.Exists(logPath) && File.Exists(legacyPath))
            {
                File.Move(legacyPath, logPath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    public void AppendChat(string role, string text)
    {
        AppendLine($"CHAT\t{role}\t{Escape(text)}");
    }

    public void AppendActivity(string text)
    {
        AppendLine($"LOG\t{Escape(text)}");
    }

    /// <summary>
    /// Archives the current log under a timestamped name so a fresh session starts clean
    /// while past history stays recoverable on disk.
    /// </summary>
    public void RotateForNewSession()
    {
        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 0)
            {
                string archivePath = Path.Combine(logDirectory, $"session-log-{sessionKey}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.Move(logPath, archivePath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    public List<ChatMessage> LoadChatHistory()
    {
        List<ChatMessage> result = new List<ChatMessage>();
        foreach (string line in ReadLines())
        {
            string[] parts = line.Split('\t');
            if (parts.Length >= 4 && parts[1] == "CHAT")
            {
                // "System" is only ever written by CompanionSession.AddSystemNotice - restoring
                // the flag here keeps a reloaded notice looking like a notice, not a bubble.
                result.Add(new ChatMessage(parts[2], Unescape(parts[3]), parts[2] == "System"));
            }
        }
        return result;
    }

    public List<string> LoadActivityHistory()
    {
        List<string> result = new List<string>();
        foreach (string line in ReadLines())
        {
            string[] parts = line.Split('\t');
            if (parts.Length >= 3 && parts[1] == "LOG")
            {
                result.Add(Unescape(parts[2]));
            }
        }
        return result;
    }

    private void AppendLine(string payload)
    {
        try
        {
            if (!Directory.Exists(logDirectory))
            {
                Directory.CreateDirectory(logDirectory);
            }

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{payload}{Environment.NewLine}";
            File.AppendAllText(logPath, line);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private string[] ReadLines()
    {
        try
        {
            return File.Exists(logPath) ? File.ReadAllLines(logPath) : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return Array.Empty<string>();
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    private static string Unescape(string value)
    {
        return value.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\\", "\\");
    }
}
