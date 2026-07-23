using System;

// Common contract every AI backend (Claude Code CLI today; GPT/Codex/Cursor/Gemini CLIs later)
// must satisfy so CompanionSession and the window never need to know which one is actually
// running. ClaudeSessionRunner is the first implementation - extracted as a pure refactor
// (no behavior change) as step 1 of the multi-provider plan (2026-07-23).
public interface IAiSessionRunner : IDisposable
{
    event Action<string> OnSessionStarted;
    event Action<string> OnAssistantText;
    event Action<string> OnToolActivity;
    event Action OnTurnComplete;
    event Action<string> OnError;

    bool IsBusy { get; }
    string SessionId { get; }

    void ResetSession();
    void RestoreSession(string knownSessionId);
    void Send(string message);
    void Kill();
}
