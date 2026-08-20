using System;
using System.Diagnostics;

// Shared by every IAiSessionRunner's Kill() - see ClaudeSessionRunner.Kill()'s comment for why a
// plain Process.Kill() isn't enough (claude.cmd/codex.cmd resolve through a cmd.exe shim, so the
// Process this project tracks is that wrapper, not the real CLI running underneath it - killing
// it alone leaves the actual claude/codex/cursor-agent process running in the background).
//
// The normal .NET fix is Process.Kill(bool entireProcessTree) (added in .NET Core 3.0), but that
// overload isn't available here - this Unity Editor's scripting runtime resolves
// System.Diagnostics.Process against an older BCL surface than the .NET version would suggest
// (confirmed via CS1739 at compile time: "The best overload for 'Kill' does not have a parameter
// named 'entireProcessTree'" - 2026-08-20). `taskkill /T /F` is the direct Windows equivalent -
// kills the target PID and its whole descendant tree - and needs no new API surface. Windows-only,
// matching every other environment assumption already baked into these runners (cmd.exe
// indirection, .cmd shims, StandardErrorEncoding mojibake handling) - this toolset targets the
// project's actual development machine, not a cross-platform build.
internal static class ProcessTreeKiller
{
    public static void Kill(int processId)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/T /F /PID {processId}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process killer = Process.Start(startInfo))
            {
                killer?.WaitForExit(5000);
            }
        }
        catch (Exception)
        {
            // Best-effort - if taskkill itself can't be launched, there's no further fallback
            // available; the caller still proceeds to reset its own IsBusy/lock state regardless.
        }
    }
}
