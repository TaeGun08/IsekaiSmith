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
            Log.AppendActivity(entry);
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
        Runner.Send(text);
        Changed?.Invoke();
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
