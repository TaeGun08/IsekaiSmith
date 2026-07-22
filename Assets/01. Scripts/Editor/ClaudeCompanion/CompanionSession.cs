using System;
using System.Collections.Generic;

// Bundles everything that makes up one independent Companion conversation - the running
// Claude process, its chat/activity history, and its on-disk log - behind a single object.
// ClaudeCompanionWindow currently holds exactly one of these, but keeping the state here
// (instead of as flat fields on the window) is what lets a future multi-session UI hold a
// list of them side by side without re-threading every call site again.
public class CompanionSession
{
    public string SessionKey { get; }
    public string RestoredSessionId { get; private set; }

    public readonly List<ChatMessage> ChatMessages = new List<ChatMessage>();
    public readonly List<string> ActivityLog = new List<string>();
    public readonly ClaudeSessionRunner Runner;
    public readonly CompanionLog Log;

    // Coarse "what is Claude doing right now" signal for the character stage - see
    // CharacterActivity. Derived from tool_use activity, not persisted (a fresh Idle on every
    // reload is fine, this is purely cosmetic).
    public CharacterActivity CurrentActivity { get; private set; } = CharacterActivity.Idle;

    // Just the current (or most recently finished) turn's raw activity entries, for the turn
    // progress stepper - unlike ActivityLog (which keeps growing for the whole conversation and
    // is what's mirrored to disk), this is cleared every time a new turn starts (see SendNow),
    // so the stepper always shows "what just happened" instead of the entire session's history.
    public readonly List<string> CurrentTurnSteps = new List<string>();

    // Running total across this conversation, from each turn's "result" event usage block -
    // not persisted (resets on ResetForNewConversation/a fresh reload), same "cosmetic,
    // fine to lose" treatment as CurrentActivity above.
    public long TotalTokens { get; private set; }

    private static readonly HashSet<string> ReadingTools = new HashSet<string>
        { "Read", "Glob", "Grep", "WebFetch", "WebSearch", "NotebookRead" };
    private static readonly HashSet<string> EditingTools = new HashSet<string>
        { "Edit", "Write", "NotebookEdit", "MultiEdit", "TodoWrite" };
    private static readonly HashSet<string> RunningTools = new HashSet<string>
        { "Bash", "BashOutput", "KillShell", "Task", "SlashCommand" };

    // Shared by CurrentActivity classification (below) and the stepper's chip coloring in
    // ClaudeCompanionWindow, so the "which bucket does this tool fall into" rule only lives
    // in one place.
    public static CharacterActivity ClassifyTool(string toolName)
    {
        if (string.IsNullOrEmpty(toolName))
        {
            return CharacterActivity.Thinking;
        }
        if (ReadingTools.Contains(toolName))
        {
            return CharacterActivity.Reading;
        }
        if (EditingTools.Contains(toolName))
        {
            return CharacterActivity.Editing;
        }
        if (RunningTools.Contains(toolName) || toolName.StartsWith("mcp__"))
        {
            return CharacterActivity.Running;
        }
        return CharacterActivity.Thinking;
    }

    // "tool_use: X" entries carry the real tool name; everything else (tool_result, system,
    // error lines) doesn't represent new work starting, so it leaves CurrentActivity as-is
    // rather than guessing.
    private CharacterActivity ClassifyActivityEntry(string entry)
    {
        const string toolUsePrefix = "tool_use: ";
        if (entry.StartsWith(toolUsePrefix))
        {
            return ClassifyTool(entry.Substring(toolUsePrefix.Length));
        }
        if (entry == "tool_result received")
        {
            // Claude is deciding what to do next, not idle.
            return CharacterActivity.Thinking;
        }
        return CurrentActivity;
    }

    // Messages submitted while a turn was already in flight, waiting to be sent as soon as
    // the current one finishes. Previously Submit() while busy was simply unreachable (the
    // Send button was disabled), forcing the user to babysit the window instead of typing
    // ahead.
    public readonly Queue<string> PendingMessages = new Queue<string>();

    public bool IsBusy => Runner.IsBusy;

    // Fired whenever chat/activity state changes, so a host window can Repaint (and persist
    // RestoredSessionId) without this class needing to know anything about IMGUI.
    public event Action Changed;

    public CompanionSession(string sessionKey, string restoredSessionId, string projectRoot)
    {
        SessionKey = sessionKey;
        RestoredSessionId = restoredSessionId;
        Log = new CompanionLog(sessionKey);
        Runner = new ClaudeSessionRunner(projectRoot);

        if (!string.IsNullOrEmpty(restoredSessionId))
        {
            Runner.RestoreSession(restoredSessionId);
        }

        Runner.OnSessionStarted += id =>
        {
            RestoredSessionId = id;
            Changed?.Invoke();
        };
        Runner.OnAssistantText += text =>
        {
            ChatMessages.Add(new ChatMessage("Claude", text));
            Log.AppendChat("Claude", text);
            Changed?.Invoke();
        };
        Runner.OnToolActivity += entry =>
        {
            ActivityLog.Add(entry);
            CurrentTurnSteps.Add(entry);
            Log.AppendActivity(entry);
            CurrentActivity = ClassifyActivityEntry(entry);
            Changed?.Invoke();
        };
        Runner.OnUsage += usage =>
        {
            TotalTokens += usage.Total;
            Changed?.Invoke();
        };
        Runner.OnTurnComplete += AdvanceQueueOrNotify;
        Runner.OnError += error =>
        {
            ActivityLog.Add("ERROR: " + error);
            Log.AppendActivity("ERROR: " + error);
            Changed?.Invoke();
        };
    }

    // Restores whatever was logged before the window last closed (crash, domain reload, or a
    // plain close) so history isn't silently lost. No-op if either list already has entries
    // (e.g. a second call in the same domain).
    public void LoadHistoryIfEmpty()
    {
        if (ChatMessages.Count != 0 || ActivityLog.Count != 0)
        {
            return;
        }
        ChatMessages.AddRange(Log.LoadChatHistory());
        ActivityLog.AddRange(Log.LoadActivityHistory());
    }

    public void ResetForNewConversation()
    {
        Runner.ResetSession();
        ChatMessages.Clear();
        ActivityLog.Clear();
        PendingMessages.Clear();
        Log.RotateForNewSession();
        RestoredSessionId = null;
        CurrentActivity = CharacterActivity.Idle;
        CurrentTurnSteps.Clear();
        TotalTokens = 0;
        Changed?.Invoke();
    }

    // If the runner is already mid-turn, queue instead of dropping the message on the floor -
    // AdvanceQueueOrNotify sends it automatically once the current turn finishes.
    public void Submit(string text)
    {
        if (Runner.IsBusy)
        {
            PendingMessages.Enqueue(text);
            Changed?.Invoke();
            return;
        }
        SendNow(text);
    }

    private void SendNow(string text)
    {
        ChatMessages.Add(new ChatMessage("You", text));
        Log.AppendChat("You", text);
        CurrentTurnSteps.Clear();
        Runner.Send(BuildOutgoingText(text));
        CurrentActivity = CharacterActivity.Thinking;
        Changed?.Invoke();
    }

    // Prepends a language directive matching the Companion window's language setting, so a
    // resumed turn defaults to that language unless the user's own message explicitly asks for
    // a different one (2026-07-22 request). Only affects what's actually sent to the CLI - the
    // displayed/logged message above stays exactly what the user typed.
    private static string BuildOutgoingText(string text)
    {
        string directive = CompanionPreferences.ResponseLanguage == CompanionPreferences.Language.English
            ? "(Respond in English unless this message explicitly asks for a different language.)"
            : "(별도로 다른 언어를 요청하지 않았다면 한국어로 답변해줘.)";
        return directive + "\n\n" + text;
    }

    // Shared by turn-complete and cancel: if something is queued, start it immediately
    // instead of leaving the user to notice the field is idle again and press Send by hand.
    private void AdvanceQueueOrNotify()
    {
        if (PendingMessages.Count > 0)
        {
            SendNow(PendingMessages.Dequeue());
        }
        else
        {
            CurrentActivity = CharacterActivity.Idle;
            Changed?.Invoke();
        }
    }

    // Kills only this session's claude process, unlike StopSession's bridge-wide stop in
    // ClaudeCompanionWindow. Safe to call from another session's tab without affecting it.
    public void CancelTurn()
    {
        if (!Runner.IsBusy)
        {
            return;
        }
        Runner.Kill();
        ActivityLog.Add("system: 사용자가 턴을 취소했습니다");
        Log.AppendActivity("system: 사용자가 턴을 취소했습니다");
        AdvanceQueueOrNotify();
    }

    public void Dispose()
    {
        Runner.Dispose();
    }
}
