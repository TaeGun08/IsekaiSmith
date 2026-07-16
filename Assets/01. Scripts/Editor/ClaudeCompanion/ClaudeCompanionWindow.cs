using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class ClaudeCompanionWindow : EditorWindow
{
    [MenuItem("Window/Claude Companion")]
    public static void ShowWindow()
    {
        ClaudeCompanionWindow window = GetWindow<ClaudeCompanionWindow>("Claude Companion");
        window.minSize = new Vector2(640, 760);
    }

    private const string StyleSheetPath =
        "Assets/01. Scripts/Editor/ClaudeCompanion/UI/ClaudeCompanionStyles.uss";

    // The only colors still needed in C#: everything else is static USS. These are computed
    // per-frame/per-event (button state, busy dots) so they can't just live in the stylesheet.
    private static readonly Color RunningColor = new Color(0.85f, 0.35f, 0.35f);
    private static readonly Color StoppedColor = new Color(0.35f, 0.75f, 0.45f);
    private static readonly Color BridgeDotOffColor = new Color(0.6f, 0.6f, 0.6f);
    private static readonly Color BusyDotColor = new Color(1f, 0.62f, 0.25f);
    private static readonly Color IdleDotColor = new Color(0.55f, 0.62f, 0.72f);

    private bool bridgeRunning;

    // Small persisted record per session/tab - only the pieces that need to survive a domain
    // reload via Unity's own serializer. Everything else (chat history, running process, log
    // handle) lives in the runtime-only CompanionSession and is rebuilt from disk in OnEnable.
    [Serializable]
    private class SessionRecord
    {
        public string SessionKey;
        public string RestoredSessionId;
        public string DisplayName;
    }

    [SerializeField] private List<SessionRecord> sessionRecords = new List<SessionRecord>();
    [SerializeField] private int activeSessionIndex;

    // sessionRecords above only survives a domain reload - Unity discards an EditorWindow's
    // fields entirely once the window is actually closed (not just reloaded), so reopening it
    // previously always fell through to the "no records yet" seed path below and silently
    // started a brand new session 1, orphaning every other tab's chat history on disk even
    // though the underlying session-log-*.txt files were all still there. Mirroring the record
    // list to this small manifest file lets OnEnable rebuild the real tab list after a full
    // close, the same way CompanionLog already recovers each tab's chat text.
    [Serializable]
    private class SessionManifest
    {
        public List<SessionRecord> Records = new List<SessionRecord>();
        public int ActiveIndex;
    }

    private static string ManifestPath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "ClaudeCompanion", "sessions.json");

    private void SaveManifest()
    {
        try
        {
            string dir = Path.GetDirectoryName(ManifestPath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            SessionManifest manifest = new SessionManifest { Records = sessionRecords, ActiveIndex = activeSessionIndex };
            File.WriteAllText(ManifestPath, JsonUtility.ToJson(manifest));
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private static List<SessionRecord> LoadManifest(out int savedActiveIndex)
    {
        savedActiveIndex = 0;
        try
        {
            if (File.Exists(ManifestPath))
            {
                SessionManifest manifest = JsonUtility.FromJson<SessionManifest>(File.ReadAllText(ManifestPath));
                if (manifest?.Records != null && manifest.Records.Count > 0)
                {
                    savedActiveIndex = manifest.ActiveIndex;
                    return manifest.Records;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        return null;
    }

    // Pre-multi-session fields. Unity's serializer matches by field name, so renaming/removing
    // these would silently orphan whatever session was live when this refactor landed (wrong
    // log file, and the next Send() would start a brand new Claude conversation instead of
    // resuming). Kept only so OnEnable can fold them into sessionRecords once; unused after.
    [SerializeField] private string restoredSessionId;
    [SerializeField] private string companionSessionKey;

    private readonly List<CompanionSession> sessions = new List<CompanionSession>();
    private CompanionSession ActiveSession =>
        (activeSessionIndex >= 0 && activeSessionIndex < sessions.Count) ? sessions[activeSessionIndex] : null;

    // Whichever session's Changed event RefreshChat is currently subscribed to - always kept
    // in sync with ActiveSession by RebuildMainColumn, so switching tabs can't leave a stale
    // subscription refreshing the wrong (now-hidden) session's chat view.
    private CompanionSession boundSession;

    private readonly Dictionary<CompanionSession, VisualElement> sidebarDots = new Dictionary<CompanionSession, VisualElement>();

    private VisualElement sidebarContainer;
    private VisualElement mainColumn;
    private CharacterStageElement characterStage;
    private Button bridgeToggleButton;
    private VisualElement bridgeDot;
    private Label bridgeLabel;
    private ScrollView chatScrollView;
    private Label pendingCountLabel;
    private TextField inputField;
    private Button sendButton;
    private Button cancelButton;

    private void OnEnable()
    {
        if (sessionRecords.Count == 0)
        {
            // A real Close (not a domain reload) destroys this window instance, so the
            // in-memory sessionRecords above is always empty on the way back in - recover
            // the tab list from the on-disk manifest first before assuming this is a
            // genuinely first-ever launch.
            List<SessionRecord> restored = LoadManifest(out int savedActiveIndex);
            if (restored != null)
            {
                sessionRecords.AddRange(restored);
                activeSessionIndex = savedActiveIndex;
            }
            else
            {
                // Fold the pre-multi-session identity in, if any, so the conversation that was
                // live before this refactor keeps resuming instead of silently starting fresh.
                string seedKey = !string.IsNullOrEmpty(companionSessionKey) ? companionSessionKey : Guid.NewGuid().ToString("N");
                sessionRecords.Add(new SessionRecord
                {
                    SessionKey = seedKey,
                    RestoredSessionId = restoredSessionId,
                    DisplayName = "세션 1"
                });
            }
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        sessions.Clear();
        foreach (SessionRecord record in sessionRecords)
        {
            try
            {
                CompanionSession newSession = new CompanionSession(record.SessionKey, record.RestoredSessionId, projectRoot);
                newSession.Changed += () =>
                {
                    record.RestoredSessionId = newSession.RestoredSessionId;
                    SaveManifest();
                };
                sessions.Add(newSession);
            }
            catch (Exception ex)
            {
                // Never let OnEnable throw: right after a domain reload, other packages'
                // static services may not be ready yet, and a throwing OnEnable can cause
                // Unity to drop this window instead of gracefully re-showing it.
                Debug.LogException(ex);
            }
        }
        activeSessionIndex = Mathf.Clamp(activeSessionIndex, 0, Mathf.Max(0, sessions.Count - 1));
        SaveManifest();

        try
        {
            bridgeRunning = MCPServiceLocator.Bridge.IsRunning;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            bridgeRunning = false;
        }

        foreach (CompanionSession s in sessions)
        {
            try
            {
                s.LoadHistoryIfEmpty();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    private void OnDisable()
    {
        // Fires on a real window Close too, not just a domain reload - this is the last
        // chance to persist the tab list (see SessionManifest) before sessionRecords itself
        // is discarded along with this window instance.
        SaveManifest();
        foreach (CompanionSession s in sessions)
        {
            try
            {
                s.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    // UI Toolkit entry point - Unity calls this after OnEnable, once the window's root visual
    // element is actually needed. Building the tree once here (instead of every frame like the
    // old OnGUI) is what removes the whole class of "layout formula desynced from what's
    // actually drawn" bugs this file kept hitting - there's no per-frame height math to get out
    // of sync anymore, Flexbox owns that.
    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.Clear();
        root.AddToClassList("root");

        StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
        if (styleSheet != null)
        {
            root.styleSheets.Add(styleSheet);
        }

        VisualElement mainRow = new VisualElement();
        mainRow.AddToClassList("main-row");
        root.Add(mainRow);

        sidebarContainer = new VisualElement();
        sidebarContainer.AddToClassList("sidebar");
        mainRow.Add(sidebarContainer);

        VisualElement vDivider = new VisualElement();
        vDivider.AddToClassList("vertical-divider");
        mainRow.Add(vDivider);

        mainColumn = new VisualElement();
        mainColumn.AddToClassList("main-column");
        mainRow.Add(mainColumn);

        RebuildSidebar();
        RebuildMainColumn();

        // Drives the character's idle/busy animation and the sidebar busy-dots. Unlike the old
        // EditorApplication.update-based RepaintInterval gate, this doesn't fight a competing
        // self-sustaining redraw loop - UI Toolkit panels only repaint when something dirties
        // them, so this tick is the only thing driving redraws here, at a steady ~60fps.
        root.schedule.Execute(OnAnimationTick).Every(16);

        // The MCP bridge package can silently reconnect on its own after a domain reload
        // (HttpBridgeReloadHandler); poll here so the button/label can't get stuck stale.
        root.schedule.Execute(RefreshBridgeStatus).Every(500);
    }

    private void OnAnimationTick()
    {
        double t = EditorApplication.timeSinceStartup;

        if (characterStage != null)
        {
            characterStage.Tick(ActiveSession != null && ActiveSession.IsBusy, t);
        }

        foreach (KeyValuePair<CompanionSession, VisualElement> kv in sidebarDots)
        {
            kv.Value.style.backgroundColor = kv.Key.IsBusy ? BusyDotColor : IdleDotColor;
        }
    }

    private void RefreshBridgeStatus()
    {
        bool wasRunning = bridgeRunning;
        try
        {
            bridgeRunning = MCPServiceLocator.Bridge.IsRunning;
        }
        catch (Exception)
        {
            // Services may be mid-teardown/setup right around a reload; keep the last known
            // value rather than logging every poll.
        }

        if (bridgeRunning != wasRunning)
        {
            UpdateBridgeControlsVisual();
            UpdateSendControlsEnabled();
        }
    }

    private void RebuildSidebar()
    {
        if (sidebarContainer == null)
        {
            return;
        }

        sidebarContainer.Clear();
        sidebarDots.Clear();

        Label title = new Label("세션");
        title.AddToClassList("sidebar-title");
        sidebarContainer.Add(title);

        for (int i = 0; i < sessions.Count; i++)
        {
            sidebarContainer.Add(BuildSessionRow(i));
        }

        VisualElement spacer = new VisualElement();
        spacer.AddToClassList("spacer");
        sidebarContainer.Add(spacer);

        Button addButton = new Button(AddNewSession) { text = "+ 새 세션" };
        addButton.AddToClassList("new-session-button");
        sidebarContainer.Add(addButton);
    }

    private VisualElement BuildSessionRow(int index)
    {
        CompanionSession s = sessions[index];
        bool isActive = index == activeSessionIndex;

        VisualElement row = new VisualElement();
        row.AddToClassList("session-row");
        if (isActive)
        {
            row.AddToClassList("session-row--active");
        }

        VisualElement dot = new VisualElement();
        dot.AddToClassList("session-dot");
        dot.style.backgroundColor = s.IsBusy ? BusyDotColor : IdleDotColor;
        row.Add(dot);
        sidebarDots[s] = dot;

        string label = index < sessionRecords.Count ? sessionRecords[index].DisplayName : $"세션 {index + 1}";
        Label nameLabel = new Label(label);
        nameLabel.AddToClassList("session-label");
        if (isActive)
        {
            nameLabel.AddToClassList("session-label--active");
        }
        nameLabel.RegisterCallback<ClickEvent>(_ => SwitchToSession(index));
        row.Add(nameLabel);

        // Deleting kills the session's underlying claude process (if any) via
        // CompanionSession.Dispose - besides freeing that process, this is also the only way
        // to release a domain-reload lock a busy session is holding (see
        // ClaudeSessionRunner.LockReload), which otherwise blocks Unity from ever swapping in
        // newly compiled code while that session stays busy.
        Button deleteButton = new Button(() => RequestRemoveSession(index)) { text = "×" };
        deleteButton.AddToClassList("session-delete-button");
        row.Add(deleteButton);

        return row;
    }

    private void SwitchToSession(int index)
    {
        if (index == activeSessionIndex)
        {
            return;
        }
        activeSessionIndex = index;
        SaveManifest();
        RebuildSidebar();
        RebuildMainColumn();
    }

    private void RequestRemoveSession(int index)
    {
        if (index < 0 || index >= sessions.Count)
        {
            return;
        }

        CompanionSession session = sessions[index];
        if (session.IsBusy)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "세션 삭제 확인",
                "이 세션은 아직 작업 중입니다. 지금 삭제하면 진행 중인 응답이 강제로 중단됩니다.\n\n계속하시겠습니까?",
                "삭제",
                "취소");
            if (!proceed)
            {
                return;
            }
        }

        try
        {
            session.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        // The chat/activity log on disk (CompanionLog) is intentionally left alone - this
        // only removes the tab, the same "recoverable, never silently destroyed" philosophy
        // as ResetForNewConversation's archiving instead of deleting.
        sessions.RemoveAt(index);
        if (index < sessionRecords.Count)
        {
            sessionRecords.RemoveAt(index);
        }

        if (activeSessionIndex > index)
        {
            activeSessionIndex--;
        }
        activeSessionIndex = Mathf.Clamp(activeSessionIndex, 0, Mathf.Max(0, sessions.Count - 1));

        SaveManifest();
        RebuildSidebar();
        RebuildMainColumn();
    }

    private void AddNewSession()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        SessionRecord record = new SessionRecord
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            DisplayName = $"세션 {sessionRecords.Count + 1}"
        };
        sessionRecords.Add(record);

        try
        {
            CompanionSession newSession = new CompanionSession(record.SessionKey, null, projectRoot);
            newSession.Changed += () =>
            {
                record.RestoredSessionId = newSession.RestoredSessionId;
                SaveManifest();
            };
            sessions.Add(newSession);
            activeSessionIndex = sessions.Count - 1;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }

        SaveManifest();
        RebuildSidebar();
        RebuildMainColumn();
    }

    private void RebuildMainColumn()
    {
        if (mainColumn == null)
        {
            return;
        }

        if (boundSession != null)
        {
            boundSession.Changed -= RefreshChat;
            boundSession = null;
        }

        mainColumn.Clear();
        characterStage = null;
        bridgeToggleButton = null;
        bridgeDot = null;
        bridgeLabel = null;
        chatScrollView = null;
        pendingCountLabel = null;
        inputField = null;
        sendButton = null;
        cancelButton = null;

        if (ActiveSession == null)
        {
            Label empty = new Label("활성 세션이 없습니다. '+ 새 세션'을 눌러주세요.");
            empty.AddToClassList("empty-state-label");
            mainColumn.Add(empty);
            return;
        }

        characterStage = new CharacterStageElement();
        mainColumn.Add(characterStage);

        mainColumn.Add(BuildControlsRow());

        VisualElement hDivider = new VisualElement();
        hDivider.AddToClassList("horizontal-divider");
        mainColumn.Add(hDivider);

        mainColumn.Add(BuildChatArea());

        boundSession = ActiveSession;
        boundSession.Changed += RefreshChat;

        RefreshChat();
    }

    private VisualElement BuildControlsRow()
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("controls-row");

        bridgeToggleButton = new Button(OnBridgeToggleClicked);
        bridgeToggleButton.AddToClassList("bridge-toggle-button");
        row.Add(bridgeToggleButton);

        bridgeDot = new VisualElement();
        bridgeDot.AddToClassList("bridge-dot");
        row.Add(bridgeDot);

        // The bridge is one shared connection for the whole editor, not per-session - Stop
        // disconnects every session's MCP tool calls, not just the one currently in view.
        // Surfaced via tooltip rather than a badge so it doesn't compete for space.
        bridgeLabel = new Label();
        bridgeLabel.AddToClassList("bridge-label");
        bridgeLabel.tooltip = "모든 세션이 이 브릿지를 공유합니다. Stop을 누르면 다른 세션의 MCP 연결도 함께 끊깁니다.";
        row.Add(bridgeLabel);

        VisualElement spacer = new VisualElement();
        spacer.AddToClassList("spacer");
        row.Add(spacer);

        // Fallback path for the chat input row's recurring visibility bugs under the old IMGUI
        // implementation: an independent popup window that shares none of this window's
        // layout. Kept for now even though UI Toolkit removes the root cause of that bug class
        // - safe to retire once the new input field has proven stable for a while.
        Button fallbackButton = new Button(() => ClaudeCompanionSendDialog.Open(this)) { text = "대체 입력창" };
        fallbackButton.AddToClassList("fallback-input-button");
        row.Add(fallbackButton);

        UpdateBridgeControlsVisual();
        return row;
    }

    private void OnBridgeToggleClicked()
    {
        if (bridgeRunning)
        {
            StopSession();
        }
        else
        {
            StartSession();
        }
    }

    private void UpdateBridgeControlsVisual()
    {
        if (bridgeToggleButton == null)
        {
            return;
        }
        bridgeToggleButton.text = bridgeRunning ? "■ Stop" : "▶ Start";
        bridgeToggleButton.style.backgroundColor = bridgeRunning ? RunningColor : StoppedColor;
        bridgeDot.style.backgroundColor = bridgeRunning ? StoppedColor : BridgeDotOffColor;
        bridgeLabel.text = bridgeRunning ? "브릿지 연결됨" : "브릿지 중지됨";
    }

    private async void StartSession()
    {
        if (!MCPServiceLocator.Server.IsLocalHttpServerRunning())
        {
            MCPServiceLocator.Server.StartLocalHttpServer();
        }

        if (!MCPServiceLocator.Bridge.IsRunning)
        {
            await MCPServiceLocator.Bridge.StartAsync();
        }

        bridgeRunning = MCPServiceLocator.Bridge.IsRunning;

        // Only wipe the conversation for a genuinely fresh start. If a session id is
        // already known (e.g. the user is just reconnecting a bridge that dropped and
        // auto-resumed), clicking Start should not throw away the ongoing chat.
        if (ActiveSession != null && string.IsNullOrEmpty(ActiveSession.RestoredSessionId))
        {
            ActiveSession.ResetForNewConversation();
        }

        UpdateBridgeControlsVisual();
        UpdateSendControlsEnabled();
    }

    private async void StopSession()
    {
        // MCPServiceLocator.Bridge/the local HTTP server are singletons shared by every
        // session in this window, not per-session - stopping them here would silently cut
        // off any other session that's still mid-turn. Warn before doing that instead of
        // doing it quietly.
        int otherBusyCount = 0;
        for (int i = 0; i < sessions.Count; i++)
        {
            if (i != activeSessionIndex && sessions[i].IsBusy)
            {
                otherBusyCount++;
            }
        }

        if (otherBusyCount > 0)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "브릿지 중지 확인",
                $"다른 세션 {otherBusyCount}개가 아직 작업 중입니다. 브릿지는 모든 세션이 공유하므로 지금 중지하면 그 세션들의 MCP 연결도 함께 끊깁니다.\n\n계속하시겠습니까?",
                "중지",
                "취소");
            if (!proceed)
            {
                return;
            }
        }

        ActiveSession?.Runner.Kill();
        await MCPServiceLocator.Bridge.StopAsync();
        MCPServiceLocator.Server.StopLocalHttpServer();
        bridgeRunning = MCPServiceLocator.Bridge.IsRunning;

        UpdateBridgeControlsVisual();
        UpdateSendControlsEnabled();
    }

    private VisualElement BuildChatArea()
    {
        VisualElement container = new VisualElement();
        container.style.flexGrow = 1;

        Label chatTitle = new Label("채팅");
        chatTitle.AddToClassList("sidebar-title");
        container.Add(chatTitle);

        chatScrollView = new ScrollView(ScrollViewMode.Vertical);
        chatScrollView.AddToClassList("chat-scroll");
        container.Add(chatScrollView);

        pendingCountLabel = new Label();
        pendingCountLabel.AddToClassList("pending-count-label");
        pendingCountLabel.style.display = DisplayStyle.None;
        container.Add(pendingCountLabel);

        // Auto-grows with wrapped content up to the "chat-input" USS max-height, same idea as
        // the old IMGUI auto-growing box but handled natively by the multiline TextField
        // instead of a manual CalcHeight/Clamp pass every frame.
        inputField = new TextField { multiline = true };
        inputField.AddToClassList("chat-input");
        VisualElement innerInput = inputField.Q(className: TextField.inputUssClassName);
        innerInput?.AddToClassList("chat-input-inner");
        // Enter sends; Shift+Enter falls through to the field's own default handling so a
        // multi-line message can still be composed. Must run in the trickle-down (capture)
        // phase so it intercepts before the TextField's own bubble-phase handler treats Return
        // as "insert a newline".
        inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        inputField.RegisterValueChangedCallback(_ => UpdateSendControlsEnabled());
        container.Add(inputField);

        VisualElement buttonsRow = new VisualElement();
        buttonsRow.AddToClassList("chat-buttons-row");

        sendButton = new Button(TrySend) { text = "Send" };
        sendButton.AddToClassList("send-button");
        buttonsRow.Add(sendButton);

        // Only meaningful while this specific session is mid-turn - unlike the Stop button
        // above, this cancels just the active session's process, not the shared bridge, so
        // other sessions keep running.
        cancelButton = new Button(() => ActiveSession?.CancelTurn()) { text = "취소" };
        cancelButton.AddToClassList("cancel-button");
        buttonsRow.Add(cancelButton);

        container.Add(buttonsRow);

        return container;
    }

    private void OnInputKeyDown(KeyDownEvent evt)
    {
        if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && !evt.shiftKey)
        {
            evt.StopPropagation();
            evt.PreventDefault();
            TrySend();
        }
    }

    private void TrySend()
    {
        string text = inputField?.value ?? "";
        if (!CanSendMessage(text))
        {
            return;
        }
        SubmitMessage(text);
        inputField.SetValueWithoutNotify("");
        UpdateSendControlsEnabled();
    }

    // Whenever a message is added (sent, queued, or a reply arrives), rebuild the bubble list
    // and jump the scroll view to the bottom - bound to CompanionSession.Changed for whichever
    // session is currently active (see RebuildMainColumn).
    private void RefreshChat()
    {
        if (chatScrollView == null || ActiveSession == null)
        {
            return;
        }

        chatScrollView.Clear();
        foreach (ChatMessage message in ActiveSession.ChatMessages)
        {
            AddChatBubble(message, pending: false);
        }
        foreach (string pendingText in ActiveSession.PendingMessages)
        {
            AddChatBubble(new ChatMessage("You", pendingText), pending: true);
        }

        int pendingCount = ActiveSession.PendingMessages.Count;
        pendingCountLabel.text = pendingCount > 0 ? $"메시지 {pendingCount}개 전송 대기 중" : "";
        pendingCountLabel.style.display = pendingCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;

        UpdateSendControlsEnabled();
        ScrollChatToBottom();
    }

    private void ScrollChatToBottom()
    {
        // Deferred one tick: the newly added bubbles need a layout pass before
        // contentContainer's height reflects them, otherwise this scrolls to the *previous*
        // bottom instead of the new one.
        chatScrollView.schedule.Execute(() =>
        {
            chatScrollView.scrollOffset = new Vector2(0f, chatScrollView.contentContainer.layout.height);
        });
    }

    private void AddChatBubble(ChatMessage message, bool pending)
    {
        bool isUser = message.Role == "You";

        VisualElement row = new VisualElement();
        row.AddToClassList("chat-bubble-row");
        row.AddToClassList(isUser ? "chat-bubble-row--user" : "chat-bubble-row--claude");

        VisualElement bubble = new VisualElement();
        bubble.AddToClassList("chat-bubble");
        bubble.AddToClassList(isUser ? "chat-bubble--user" : "chat-bubble--claude");
        if (pending)
        {
            // Halved alpha is enough to read as "not sent yet" without a separate style pass.
            bubble.AddToClassList("chat-bubble--pending");
        }

        Label roleLabel = new Label(pending ? message.Role + " · 전송 대기" : message.Role);
        roleLabel.AddToClassList("chat-bubble-role");
        bubble.Add(roleLabel);

        BuildBubbleContent(bubble, message.Text);

        row.Add(bubble);
        chatScrollView.Add(row);
    }

    // Renders fenced code blocks in their own plain (non-rich-text) box and everything else as
    // rich text with bold/inline-code/bullets converted - see ChatMarkdown for what's actually
    // supported. Anything Claude sends that isn't one of those just prints as-is.
    private static void BuildBubbleContent(VisualElement bubble, string text)
    {
        foreach (ChatMarkdown.Segment segment in ChatMarkdown.Split(text))
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            if (segment.IsCode)
            {
                VisualElement codeBlock = new VisualElement();
                codeBlock.AddToClassList("chat-code-block");
                Label codeLabel = new Label(segment.Text)
                {
                    // Deliberately not rich-text: this holds fenced code verbatim, and running
                    // rich-text conversion over real code would mangle it (generics, comparison
                    // operators, etc).
                    enableRichText = false
                };
                codeLabel.AddToClassList("chat-code-text");
                codeBlock.Add(codeLabel);
                bubble.Add(codeBlock);
            }
            else
            {
                Label textLabel = new Label(ChatMarkdown.ToRichText(segment.Text)) { enableRichText = true };
                textLabel.AddToClassList("chat-bubble-text");
                bubble.Add(textLabel);
            }
        }
    }

    private void UpdateSendControlsEnabled()
    {
        if (sendButton == null)
        {
            return;
        }
        sendButton.SetEnabled(CanSendMessage(inputField?.value ?? ""));
        cancelButton.SetEnabled(ActiveSession != null && ActiveSession.IsBusy);
    }

    // Shared by both the inline chat row and ClaudeCompanionSendDialog (the fallback input
    // window) so a message can be sent identically regardless of which UI triggered it. Always
    // targets whichever session is active at send time. Deliberately allows submitting while
    // ActiveSession.IsBusy - CompanionSession.Submit queues it instead of sending immediately.
    private bool CanSendMessage(string text)
    {
        return bridgeRunning && ActiveSession != null && !string.IsNullOrWhiteSpace(text);
    }

    public void SubmitMessage(string text)
    {
        if (!CanSendMessage(text))
        {
            return;
        }
        ActiveSession.Submit(text);
    }
}
