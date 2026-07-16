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
    // per-frame/per-event (busy dots) or are semantic state colors that must win over any USS
    // hover rule, so they can't just live in the stylesheet - see UpdateBridgeControlsVisual
    // for why the Start/Stop button itself uses toggled USS classes instead (inline style beats
    // USS specificity, including :hover, so a button colored via C# can never show a hover tint).
    private static readonly Color StoppedColor = new Color(0.35f, 0.75f, 0.45f);
    private static readonly Color BridgeDotOffColor = new Color(0.6f, 0.6f, 0.6f);
    private static readonly Color StepErrorColor = new Color(0.85f, 0.35f, 0.35f);

    // Friendly Korean labels for the turn-progress stepper's chips - falls back to the raw
    // tool name (or, for mcp__ tools, the part after the last "__") when a tool isn't listed
    // here, so an unrecognized/new tool never renders as a blank chip.
    private static readonly Dictionary<string, string> ToolLabels = new Dictionary<string, string>
    {
        { "Read", "파일 읽기" },
        { "Glob", "파일 검색" },
        { "Grep", "내용 검색" },
        { "WebFetch", "웹 조회" },
        { "WebSearch", "웹 검색" },
        { "NotebookRead", "노트북 읽기" },
        { "Edit", "코드 수정" },
        { "MultiEdit", "코드 수정" },
        { "Write", "파일 작성" },
        { "NotebookEdit", "노트북 수정" },
        { "TodoWrite", "할 일 갱신" },
        { "Bash", "명령 실행" },
        { "BashOutput", "명령 출력 확인" },
        { "KillShell", "프로세스 종료" },
        { "Task", "하위 작업 실행" },
        { "SlashCommand", "명령어 실행" },
    };

    // Per-session identity color (sidebar stripe + a top accent bar on the active session's main
    // column) - purely cosmetic, assigned by tab index so switching/adding tabs stays visually
    // distinguishable. Not tied to busy/idle state, which stays semantic (see CharacterStageElement).
    private static readonly Color[] SessionAccentPalette =
    {
        new Color(0.85f, 0.47f, 0.34f), // coral (brand)
        new Color(0.38f, 0.66f, 0.64f), // teal
        new Color(0.62f, 0.52f, 0.84f), // violet
        new Color(0.84f, 0.71f, 0.35f), // gold
        new Color(0.42f, 0.61f, 0.79f), // sky
        new Color(0.49f, 0.67f, 0.46f), // sage
    };

    private static Color GetSessionAccent(int index)
    {
        return SessionAccentPalette[((index % SessionAccentPalette.Length) + SessionAccentPalette.Length) % SessionAccentPalette.Length];
    }

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
    [SerializeField] private bool turnStepperCollapsed;
    [SerializeField] private bool soundEnabled = true;

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

    // Whichever session's Changed/OnTurnComplete/OnError events this window is currently
    // subscribed to - always kept in sync with ActiveSession by RebuildMainColumn, so switching
    // tabs can't leave a stale subscription refreshing the wrong (now-hidden) session's view.
    private CompanionSession boundSession;

    private readonly Dictionary<CompanionSession, VisualElement> sidebarDots = new Dictionary<CompanionSession, VisualElement>();

    private VisualElement sidebarContainer;
    private VisualElement mainColumn;
    private CharacterStageElement characterStage;
    private Button bridgeToggleButton;
    private VisualElement bridgeDot;
    private Label bridgeLabel;
    private Button soundToggleButton;
    private ScrollView stepperScroll;
    private VisualElement stepperContent;
    private Button stepperToggleButton;
    private ScrollView chatScrollView;
    private Label pendingCountLabel;
    private TextField inputField;
    private Button sendButton;
    private Button cancelButton;

    // Non-null exactly while a "typing..." bubble is showing at the bottom of the chat - see
    // BuildTypingIndicator/RefreshChat. Animated from OnAnimationTick rather than a dedicated
    // scheduled callback so it shares the same 60fps tick as the character stage.
    private VisualElement[] typingDots;

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
            characterStage.Tick(ActiveSession != null ? ActiveSession.CurrentActivity : CharacterActivity.Idle, t);
        }

        foreach (KeyValuePair<CompanionSession, VisualElement> kv in sidebarDots)
        {
            kv.Value.style.backgroundColor = CharacterStageElement.GetIndicatorColor(kv.Key.CurrentActivity);
        }

        if (typingDots != null)
        {
            for (int i = 0; i < typingDots.Length; i++)
            {
                float phase = (float)(t * 4.0) - i * 0.6f;
                typingDots[i].style.opacity = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(phase));
            }
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
        row.style.borderLeftColor = GetSessionAccent(index);

        VisualElement dot = new VisualElement();
        dot.AddToClassList("session-dot");
        dot.style.backgroundColor = CharacterStageElement.GetIndicatorColor(s.CurrentActivity);
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
            boundSession.Changed -= OnSessionChanged;
            boundSession.Runner.OnTurnComplete -= OnActiveTurnComplete;
            boundSession.Runner.OnError -= OnActiveTurnError;
            boundSession = null;
        }

        mainColumn.Clear();
        characterStage = null;
        bridgeToggleButton = null;
        bridgeDot = null;
        bridgeLabel = null;
        soundToggleButton = null;
        stepperScroll = null;
        stepperContent = null;
        stepperToggleButton = null;
        chatScrollView = null;
        pendingCountLabel = null;
        inputField = null;
        sendButton = null;
        cancelButton = null;
        typingDots = null;

        if (ActiveSession == null)
        {
            Label empty = new Label("활성 세션이 없습니다. '+ 새 세션'을 눌러주세요.");
            empty.AddToClassList("empty-state-label");
            mainColumn.Add(empty);
            return;
        }

        VisualElement accentBar = new VisualElement();
        accentBar.AddToClassList("session-accent-bar");
        accentBar.style.backgroundColor = GetSessionAccent(activeSessionIndex);
        mainColumn.Add(accentBar);

        characterStage = new CharacterStageElement();
        mainColumn.Add(characterStage);

        mainColumn.Add(BuildControlsRow());

        mainColumn.Add(BuildStepperSection());

        VisualElement hDivider = new VisualElement();
        hDivider.AddToClassList("horizontal-divider");
        mainColumn.Add(hDivider);

        mainColumn.Add(BuildChatArea());

        boundSession = ActiveSession;
        boundSession.Changed += OnSessionChanged;
        boundSession.Runner.OnTurnComplete += OnActiveTurnComplete;
        boundSession.Runner.OnError += OnActiveTurnError;

        OnSessionChanged();
    }

    private void OnSessionChanged()
    {
        RefreshChat();
        RefreshStepper();
    }

    private VisualElement BuildStepperSection()
    {
        VisualElement section = new VisualElement();
        section.AddToClassList("stepper-section");

        VisualElement header = new VisualElement();
        header.AddToClassList("stepper-header");

        Label title = new Label("진행 상황");
        title.AddToClassList("stepper-title");
        header.Add(title);

        VisualElement spacer = new VisualElement();
        spacer.AddToClassList("spacer");
        header.Add(spacer);

        stepperToggleButton = new Button(ToggleStepperCollapsed)
        {
            text = turnStepperCollapsed ? "펼치기 ▲" : "접기 ▼"
        };
        stepperToggleButton.AddToClassList("stepper-toggle-button");
        header.Add(stepperToggleButton);

        section.Add(header);

        stepperScroll = new ScrollView(ScrollViewMode.Vertical);
        stepperScroll.AddToClassList("stepper-scroll");
        stepperScroll.style.display = turnStepperCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
        section.Add(stepperScroll);

        stepperContent = new VisualElement();
        stepperContent.AddToClassList("stepper-content");
        stepperScroll.Add(stepperContent);

        return section;
    }

    private void ToggleStepperCollapsed()
    {
        turnStepperCollapsed = !turnStepperCollapsed;
        stepperScroll.style.display = turnStepperCollapsed ? DisplayStyle.None : DisplayStyle.Flex;
        stepperToggleButton.text = turnStepperCollapsed ? "펼치기 ▲" : "접기 ▼";
    }

    // Renders the current (or most recently finished) turn's tool calls as a row of small
    // chips - see CompanionSession.CurrentTurnSteps. This replaces the old activity log panel
    // that was stripped out before the UI Toolkit renewal; scoped to just the current turn
    // (rather than the whole session's history) keeps it small enough to never need the manual
    // height/scroll math that kept breaking under IMGUI - the ScrollView here just clips at a
    // fixed max-height (see "stepper-scroll" in the USS) regardless of how many chips there are.
    private void RefreshStepper()
    {
        if (stepperContent == null || ActiveSession == null)
        {
            return;
        }

        stepperContent.Clear();

        if (ActiveSession.CurrentTurnSteps.Count == 0)
        {
            Label placeholder = new Label("최근 턴의 활동이 여기 표시됩니다.");
            placeholder.AddToClassList("stepper-placeholder");
            stepperContent.Add(placeholder);
            return;
        }

        foreach (string entry in ActiveSession.CurrentTurnSteps)
        {
            stepperContent.Add(BuildStepChip(entry));
        }
    }

    private static VisualElement BuildStepChip(string entry)
    {
        DescribeStep(entry, out Color color, out string label);

        VisualElement chip = new VisualElement();
        chip.AddToClassList("step-chip");

        VisualElement dot = new VisualElement();
        dot.AddToClassList("step-chip-dot");
        dot.style.backgroundColor = color;
        chip.Add(dot);

        Label text = new Label(label);
        text.AddToClassList("step-chip-label");
        chip.Add(text);

        return chip;
    }

    // Maps one raw CompanionSession activity-log entry to a chip color + friendly label.
    private static void DescribeStep(string entry, out Color color, out string label)
    {
        const string toolUsePrefix = "tool_use: ";
        const string errorPrefix = "ERROR: ";
        const string systemPrefix = "system: ";

        if (entry.StartsWith(toolUsePrefix))
        {
            string toolName = entry.Substring(toolUsePrefix.Length);
            color = CharacterStageElement.GetIndicatorColor(CompanionSession.ClassifyTool(toolName));
            label = FriendlyToolLabel(toolName);
            return;
        }
        if (entry == "tool_result received")
        {
            color = CharacterStageElement.GetIndicatorColor(CharacterActivity.Thinking);
            label = "결과 확인";
            return;
        }
        if (entry.StartsWith(errorPrefix))
        {
            color = StepErrorColor;
            label = Truncate(entry.Substring(errorPrefix.Length), 40);
            return;
        }
        if (entry.StartsWith(systemPrefix))
        {
            color = CharacterStageElement.GetIndicatorColor(CharacterActivity.Thinking);
            label = "시스템: " + Truncate(entry.Substring(systemPrefix.Length), 30);
            return;
        }

        color = CharacterStageElement.GetIndicatorColor(CharacterActivity.Thinking);
        label = Truncate(entry, 40);
    }

    private static string FriendlyToolLabel(string toolName)
    {
        if (ToolLabels.TryGetValue(toolName, out string label))
        {
            return label;
        }
        if (toolName.StartsWith("mcp__"))
        {
            int lastSeparator = toolName.LastIndexOf("__", StringComparison.Ordinal);
            string tail = lastSeparator >= 0 && lastSeparator + 2 < toolName.Length
                ? toolName.Substring(lastSeparator + 2)
                : toolName;
            return tail.Replace('_', ' ');
        }
        return toolName;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text.Substring(0, maxLength - 1) + "…";
    }

    // One-shot success/error reactions on the character - these aren't part of CompanionSession's
    // persisted CurrentActivity (which is usually already back to Idle/Thinking by the time these
    // fire), just a brief visual overlay triggered directly off the underlying process events.
    private void OnActiveTurnComplete()
    {
        characterStage?.FlashSuccess();
        if (soundEnabled)
        {
            // A plain system beep rather than an imported AudioClip - this is an Editor tool,
            // not the game, so pulling in an audio asset (plus the import/mixer plumbing that
            // implies) for a single notification ding would be a lot of weight for very little.
            EditorApplication.Beep();
        }
    }

    private void OnActiveTurnError(string _)
    {
        characterStage?.FlashError();
    }

    private void ToggleSound()
    {
        soundEnabled = !soundEnabled;
        UpdateSoundToggleVisual();
    }

    private void UpdateSoundToggleVisual()
    {
        if (soundToggleButton == null)
        {
            return;
        }
        soundToggleButton.text = soundEnabled ? "🔔 알림음" : "🔕 알림음";
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

        soundToggleButton = new Button(ToggleSound);
        soundToggleButton.AddToClassList("sound-toggle-button");
        row.Add(soundToggleButton);

        // Fallback path for the chat input row's recurring visibility bugs under the old IMGUI
        // implementation: an independent popup window that shares none of this window's
        // layout. Kept for now even though UI Toolkit removes the root cause of that bug class
        // - safe to retire once the new input field has proven stable for a while.
        Button fallbackButton = new Button(() => ClaudeCompanionSendDialog.Open(this)) { text = "대체 입력창" };
        fallbackButton.AddToClassList("fallback-input-button");
        row.Add(fallbackButton);

        UpdateBridgeControlsVisual();
        UpdateSoundToggleVisual();
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
        bridgeToggleButton.RemoveFromClassList("bridge-toggle-button--running");
        bridgeToggleButton.RemoveFromClassList("bridge-toggle-button--stopped");
        bridgeToggleButton.AddToClassList(bridgeRunning ? "bridge-toggle-button--running" : "bridge-toggle-button--stopped");
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

        if (ActiveSession.IsBusy)
        {
            chatScrollView.Add(BuildTypingIndicator());
        }
        else
        {
            typingDots = null;
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

    // A small "..." bubble shown at the bottom of the chat while ActiveSession.IsBusy - see
    // RefreshChat/OnAnimationTick. Populates the typingDots field the tick loop animates;
    // callers must not reuse the returned element after the next RefreshChat clears the scroll
    // view (typingDots is reset alongside it).
    private VisualElement BuildTypingIndicator()
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("chat-bubble-row");
        row.AddToClassList("chat-bubble-row--claude");

        VisualElement bubble = new VisualElement();
        bubble.AddToClassList("chat-bubble");
        bubble.AddToClassList("chat-bubble--claude");
        bubble.AddToClassList("typing-indicator");

        typingDots = new VisualElement[3];
        for (int i = 0; i < typingDots.Length; i++)
        {
            VisualElement dot = new VisualElement();
            dot.AddToClassList("typing-dot");
            typingDots[i] = dot;
            bubble.Add(dot);
        }

        row.Add(bubble);
        return row;
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
