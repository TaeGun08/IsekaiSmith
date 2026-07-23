using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

// Generic "is this CLI on this machine, and if not, install it" helper - factored out of
// ClaudeSessionRunner so the same PATH-scan/npm-install logic can back other providers' install
// prompts later (2026-07-23 request: "만약 해당 AI설치가 안되어 있다면 해당 AI 설치를 해주거나
// 해줬으면 해") instead of every runner reimplementing it.
public static class CliInstaller
{
    // Scans PATH plus a couple of well-known global-install locations that a freshly-installed
    // CLI often lands in before this Editor process's own PATH env (inherited at Editor launch)
    // has caught up - Unity doesn't re-read PATH until restarted. Returns the full path if
    // found, null otherwise (unlike a plain "assume it's on PATH and let Process.Start fail"
    // approach, this lets callers know definitively before trying to run anything).
    public static string FindExecutable(string commandName)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        string[] extensions = { ".exe", ".cmd", ".bat" };

        List<string> candidateDirs = new List<string>(pathEnv.Split(Path.PathSeparator));

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
                string candidate = Path.Combine(dir.Trim(), commandName + ext);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    // Runs "npm install -g <packageName>" in the background (non-blocking - a real install can
    // take well over a minute) and reports success/failure back on the main thread via
    // EditorApplication.delayCall, since Process.Exited fires on a worker thread and Unity API
    // calls (including the dialog the caller shows next) must happen on the main thread.
    public static void InstallNpmPackageAsync(string packageName, Action<bool> onComplete)
    {
        try
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c npm install -g {packageName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            Process process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.Log("[npm install " + packageName + "] " + e.Data);
                }
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.LogWarning("[npm install " + packageName + "] " + e.Data);
                }
            };
            process.Exited += (sender, e) =>
            {
                bool success = process.ExitCode == 0;
                process.Dispose();
                EditorApplication.delayCall += () => onComplete?.Invoke(success);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            onComplete?.Invoke(false);
        }
    }
}
