using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public static class CompanionLog
{
    private static readonly string LogDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "ClaudeCompanion");
    private static readonly string LogPath = Path.Combine(LogDirectory, "session-log.txt");

    public static string FilePath => LogPath;

    public static void AppendChat(string role, string text)
    {
        AppendLine($"CHAT\t{role}\t{Escape(text)}");
    }

    public static void AppendActivity(string text)
    {
        AppendLine($"LOG\t{Escape(text)}");
    }

    /// <summary>
    /// Archives the current log under a timestamped name so a fresh session starts clean
    /// while past history stays recoverable on disk.
    /// </summary>
    public static void RotateForNewSession()
    {
        try
        {
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 0)
            {
                string archivePath = Path.Combine(LogDirectory, $"session-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.Move(LogPath, archivePath);
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    public static List<ChatMessage> LoadChatHistory()
    {
        List<ChatMessage> result = new List<ChatMessage>();
        foreach (string line in ReadLines())
        {
            string[] parts = line.Split('\t');
            if (parts.Length >= 4 && parts[1] == "CHAT")
            {
                result.Add(new ChatMessage(parts[2], Unescape(parts[3])));
            }
        }
        return result;
    }

    public static List<string> LoadActivityHistory()
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

    private static void AppendLine(string payload)
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{payload}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private static string[] ReadLines()
    {
        try
        {
            return File.Exists(LogPath) ? File.ReadAllLines(LogPath) : Array.Empty<string>();
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
