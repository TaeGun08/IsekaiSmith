using System;
using System.Collections.Generic;

// Bundles everything that makes up one independent Companion conversation - the running
// AI process, its chat/activity history, and its on-disk log - behind a single object.
// AiCompanionWindow currently holds exactly one of these, but keeping the state here
// (instead of as flat fields on the window) is what lets a future multi-session UI hold a
// list of them side by side without re-threading every call site again.
public class CompanionSession
{
    public string SessionKey { get; }
    public string RestoredSessionId { get; private set; }

    public readonly List<ChatMessage> ChatMessages = new List<ChatMessage>();
    public readonly List<string> ActivityLog = new List<string>();
    // Provider-agnostic - see IAiSessionRunner. Which concrete backend this actually is comes
    // from AiProviderRegistry.Get(ProviderId) - swappable at runtime, see SwitchProvider, so
    // this can no longer be a ctor-only readonly field.
    public IAiSessionRunner Runner { get; private set; }
    public readonly CompanionLog Log;
    private readonly string projectRoot;

    public AiProviderId ProviderId { get; private set; }

    // Which AI's visual identity the character stage draws for this session - see
    // AiCharacterConcept. Set from the chosen provider's definition; changes on SwitchProvider.
    public AiCharacterConcept Concept { get; private set; }

    // Session-level mirrors of the current Runner's OnTurnComplete/OnError, with a stable
    // identity that survives SwitchProvider swapping the underlying Runner object out from
    // under any external subscribers (AiCompanionWindow used to subscribe to Runner.* directly,
    // which would have gone stale - subscribed to a dead runner - the moment the provider
    // changed). External code should subscribe to these, not Runner's own events directly.
    public event Action OnTurnComplete;
    public event Action<string> OnError;

    // Coarse "what is the AI doing right now" signal for the character stage - see
    // CharacterActivity. Derived from tool_use activity, not persisted (a fresh Idle on every
    // reload is fine, this is purely cosmetic).
    public CharacterActivity CurrentActivity { get; private set; } = CharacterActivity.Idle;

    // Just the current (or most recently finished) turn's raw activity entries, for the turn
    // progress stepper - unlike ActivityLog (which keeps growing for the whole conversation and
    // is what's mirrored to disk), this is cleared every time a new turn starts (see SendNow),
    // so the stepper always shows "what just happened" instead of the entire session's history.
    public readonly List<string> CurrentTurnSteps = new List<string>();

    private static readonly HashSet<string> ReadingTools = new HashSet<string>
        { "Read", "Glob", "Grep", "WebFetch", "WebSearch", "NotebookRead" };
    private static readonly HashSet<string> EditingTools = new HashSet<string>
        { "Edit", "Write", "NotebookEdit", "MultiEdit", "TodoWrite" };
    private static readonly HashSet<string> RunningTools = new HashSet<string>
        { "Bash", "BashOutput", "KillShell", "Task", "SlashCommand" };

    // Shared by CurrentActivity classification (below) and the stepper's chip coloring in
    // AiCompanionWindow, so the "which bucket does this tool fall into" rule only lives
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
            // The AI is deciding what to do next, not idle.
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

    public CompanionSession(string sessionKey, string restoredSessionId, string projectRoot, AiProviderId providerId = AiProviderId.Claude)
    {
        SessionKey = sessionKey;
        RestoredSessionId = restoredSessionId;
        Log = new CompanionLog(sessionKey);
        this.projectRoot = projectRoot;

        AiProviderDefinition provider = AiProviderRegistry.Get(providerId);
        ProviderId = provider.Id;
        Concept = provider.Concept;
        Runner = provider.CreateRunner(projectRoot);
        AttachRunner(Runner);

        if (!string.IsNullOrEmpty(restoredSessionId))
        {
            Runner.RestoreSession(restoredSessionId);
        }
    }

    // Wires one runner's events to this session's own bookkeeping - shared by the ctor and
    // SwitchProvider so both go through the exact same subscription set (nothing to
    // accidentally miss re-wiring when a provider changes).
    private void AttachRunner(IAiSessionRunner runner)
    {
        runner.OnSessionStarted += HandleSessionStarted;
        runner.OnAssistantText += HandleAssistantText;
        runner.OnToolActivity += HandleToolActivity;
        runner.OnTurnComplete += HandleTurnComplete;
        runner.OnError += HandleRunnerError;
    }

    private void DetachRunner(IAiSessionRunner runner)
    {
        runner.OnSessionStarted -= HandleSessionStarted;
        runner.OnAssistantText -= HandleAssistantText;
        runner.OnToolActivity -= HandleToolActivity;
        runner.OnTurnComplete -= HandleTurnComplete;
        runner.OnError -= HandleRunnerError;
    }

    private void HandleSessionStarted(string id)
    {
        RestoredSessionId = id;
        Changed?.Invoke();
    }

    private void HandleAssistantText(string text)
    {
        // Role label follows whichever AI is actually behind this session (see Concept)
        // instead of a hardcoded "Claude" (2026-07-23 rebrand) - Claude by default today,
        // but the chat log/UI should say GPT/Codex/etc. once other providers exist.
        ChatMessages.Add(new ChatMessage(Concept.DisplayName, text));
        Log.AppendChat(Concept.DisplayName, text);
        Changed?.Invoke();
    }

    private void HandleToolActivity(string entry)
    {
        ActivityLog.Add(entry);
        CurrentTurnSteps.Add(entry);
        Log.AppendActivity(entry);
        CurrentActivity = ClassifyActivityEntry(entry);
        Changed?.Invoke();
    }

    private void HandleTurnComplete()
    {
        AdvanceQueueOrNotify();
        OnTurnComplete?.Invoke();
    }

    private void HandleRunnerError(string error)
    {
        ActivityLog.Add("ERROR: " + error);
        Log.AppendActivity("ERROR: " + error);
        Changed?.Invoke();
        OnError?.Invoke(error);
    }

    // Switches this session to a different AI backend, live - step 3.5 of the multi-provider
    // plan (2026-07-23 request: "지금 세션에서도 변경이 가능하도록"). Kills whatever the old
    // runner was mid-doing (a different provider's process can't finish "this" turn) and drops
    // RestoredSessionId - a resume id is only meaningful to the CLI session that minted it, so
    // reusing it against a different provider would either be ignored or (worse) misinterpreted.
    // Chat/activity history is deliberately kept, not cleared, so switching reads as "the same
    // conversation, different AI now answering" rather than losing everything.
    //
    // 2026-07-23 follow-up request: "다른 AI로 넘어가더라도 현재 세션의 내용을 이어서 작업할 수
    // 있도록" + "연동이 됐다면 연동됐다는 신호 또는 채팅이 있었으면". Cross-CLI --resume isn't
    // possible (each CLI's session/thread id only means something to that CLI), so instead the
    // full visible chat transcript is sent as the new runner's first turn (BuildHandoffContext) -
    // the new AI reads it and picks up where the old one left off, instead of starting blind.
    // A system-notice chat bubble marks the switch, and a second one only appears once the new
    // runner actually confirms it's alive (OnSessionStarted) or fails (OnError) - not before -
    // so "연동됨" always reflects a real, observed connection rather than "we tried."
    public void SwitchProvider(AiProviderId newProviderId)
    {
        if (newProviderId == ProviderId)
        {
            return;
        }

        string previousDisplayName = Concept.DisplayName;
        string handoffContext = BuildHandoffContext(previousDisplayName);

        IAiSessionRunner oldRunner = Runner;
        oldRunner.Kill();
        DetachRunner(oldRunner);
        oldRunner.Dispose();

        AiProviderDefinition provider = AiProviderRegistry.Get(newProviderId);
        ProviderId = provider.Id;
        Concept = provider.Concept;
        RestoredSessionId = null;
        Runner = provider.CreateRunner(projectRoot);
        AttachRunner(Runner);
        // The new CLI process has no memory of the language directive the old one was sent -
        // make sure it gets one too (see BuildOutgoingText).
        lastSentLanguage = null;

        string note = $"system: AI 전환 → {provider.DisplayName}";
        ActivityLog.Add(note);
        Log.AppendActivity(note);

        IAiSessionRunner newRunner = Runner;
        SubscribeOneShotConnectionSignal(newRunner, provider.DisplayName);

        if (handoffContext != null)
        {
            AddSystemNotice($"🔄 {previousDisplayName} → {provider.DisplayName}로 전환 — 이전 대화 내용을 전달합니다.");
            CurrentTurnSteps.Clear();
            newRunner.Send(BuildOutgoingText(handoffContext));
            CurrentActivity = CharacterActivity.Thinking;
        }
        else
        {
            AddSystemNotice($"🔄 {previousDisplayName} → {provider.DisplayName}로 전환했습니다.");
            CurrentActivity = CharacterActivity.Idle;
        }

        Changed?.Invoke();
    }

    // One-time confirmation that the freshly-switched-to runner actually connected (fired the
    // real CLI's session id back) or failed to - unsubscribes itself either way so it never
    // fires again for later, unrelated turns on this same runner.
    private void SubscribeOneShotConnectionSignal(IAiSessionRunner runner, string providerDisplayName)
    {
        Action<string> onConnected = null;
        Action<string> onFailed = null;
        onConnected = _ =>
        {
            runner.OnSessionStarted -= onConnected;
            runner.OnError -= onFailed;
            AddSystemNotice($"✅ {providerDisplayName}와 연동되었습니다.");
        };
        onFailed = error =>
        {
            runner.OnSessionStarted -= onConnected;
            runner.OnError -= onFailed;
            AddSystemNotice($"⚠️ {providerDisplayName} 연동 실패: {error}");
        };
        runner.OnSessionStarted += onConnected;
        runner.OnError += onFailed;
    }

    // Null when there's nothing worth handing off yet (a brand new, empty session) - switching
    // providers on an empty session shouldn't manufacture a prompt out of nothing.
    private string BuildHandoffContext(string previousProviderName)
    {
        if (ChatMessages.Count == 0)
        {
            return null;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("(다른 AI(").Append(previousProviderName).Append(")와 나누던 대화를 이어받았습니다. ")
          .Append("아래는 지금까지의 대화 기록입니다. 내용을 파악한 뒤, 마지막 내용 이후부터 자연스럽게 이어서 작업해주세요.)\n\n");
        foreach (ChatMessage message in ChatMessages)
        {
            if (message.IsSystemNotice)
            {
                continue;
            }
            sb.Append('[').Append(message.Role).Append("]\n").Append(message.Text).Append("\n\n");
        }
        return sb.ToString();
    }

    private void AddSystemNotice(string text)
    {
        ChatMessages.Add(ChatMessage.SystemNotice(text));
        Log.AppendChat("System", text);
        Changed?.Invoke();
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
        lastSentLanguage = null;
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

    // Sentinel (not a real Language value) so the very first message of a conversation always
    // sends the directive once, same as any later actual language change.
    private CompanionPreferences.Language? lastSentLanguage;

    // Prepends a language directive matching the Companion window's language setting, so a
    // resumed turn defaults to that language unless the user's own message explicitly asks for
    // a different one (2026-07-22 request). Only sent when it's new information - the first
    // message of a conversation, or right after the user changes the language setting - not on
    // every single message: a --resume'd conversation already remembers the instruction, so
    // repeating it every turn was pure token waste (2026-07-23 request to cut unnecessary
    // token spend). Only affects what's actually sent to the CLI - the displayed/logged message
    // above stays exactly what the user typed.
    private string BuildOutgoingText(string text)
    {
        CompanionPreferences.Language current = CompanionPreferences.ResponseLanguage;
        if (lastSentLanguage == current)
        {
            return text;
        }
        lastSentLanguage = current;
        string directive = current == CompanionPreferences.Language.English
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

    // Kills only this session's AI process, unlike StopSession's bridge-wide stop in
    // AiCompanionWindow. Safe to call from another session's tab without affecting it.
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
