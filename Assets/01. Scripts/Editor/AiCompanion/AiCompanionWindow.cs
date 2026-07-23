using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class AiCompanionWindow : EditorWindow
{
    [MenuItem("Window/AI Companion")]
    public static void ShowWindow()
    {
        AiCompanionWindow window = GetWindow<AiCompanionWindow>("AI Companion");
        // Was 760 tall pre-M4/M7; the turn-progress stepper and chat search/export header
        // added roughly 115px of fixed-height content since, so the old minimum left the chat
        // scroll area barely any room (see the min-height:0 fix on "chat-scroll"/"chat-area").
        window.minSize = new Vector2(640, 860);
    }

    private const string StyleSheetPath =
        "Assets/01. Scripts/Editor/AiCompanion/UI/AiCompanionStyles.uss";
    private const string LightStyleSheetPath =
        "Assets/01. Scripts/Editor/AiCompanion/UI/AiCompanionStyles.Light.uss";

    // The only colors still needed in C#: everything else is static USS. These are computed
    // per-frame/per-event (busy dots) or are semantic state colors that must win over any USS
    // hover rule, so they can't just live in the stylesheet - see UpdateBridgeControlsVisual
    // for why the Start/Stop button itself uses toggled USS classes instead (inline style beats
    // USS specificity, including :hover, so a button colored via C# can never show a hover tint).
    private static readonly Color StoppedColor = new Color(0.35f, 0.75f, 0.45f);
    private static readonly Color BridgeDotOffColor = new Color(0.6f, 0.6f, 0.6f);
    private static readonly Color StepErrorColor = new Color(0.92f, 0.26f, 0.26f);

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

    // Icon glyphs for the stepper chips (design direction "C: rich detail" - icons instead of
    // plain color dots). Falls back to a generic wrench/plug for anything unlisted.
    private static readonly Dictionary<string, string> ToolIcons = new Dictionary<string, string>
    {
        { "Read", "📖" }, { "Glob", "🔍" }, { "Grep", "🔎" }, { "WebFetch", "🌐" }, { "WebSearch", "🌐" },
        { "NotebookRead", "📓" }, { "Edit", "✏️" }, { "MultiEdit", "✏️" }, { "Write", "📝" },
        { "NotebookEdit", "📓" }, { "TodoWrite", "☑️" }, { "Bash", "⚡" }, { "BashOutput", "⚡" },
        { "KillShell", "⛔" }, { "Task", "🧩" }, { "SlashCommand", "⚙️" },
    };
    private const string DefaultToolIcon = "🔧";
    private const string McpToolIcon = "🔌";
    private const string ThinkingIcon = "💭";
    private const string SystemIcon = "⚙️";
    private const string ErrorIcon = "⚠️";

    // Per-session identity color (sidebar stripe + a top accent bar on the active session's main
    // column) - purely cosmetic, assigned by tab index so switching/adding tabs stays visually
    // distinguishable. Not tied to busy/idle state, which stays semantic (see CharacterStageElement).
    // 2026-07-16: bumped saturation to match CharacterStageElement's richer activity colors -
    // "dark theme is fine, but wants actual color presence" feedback.
    private static readonly Color[] SessionAccentPalette =
    {
        new Color(0.95f, 0.36f, 0.20f), // coral (brand)
        new Color(0.22f, 0.68f, 0.64f), // teal
        new Color(0.58f, 0.44f, 0.88f), // violet
        new Color(0.92f, 0.72f, 0.18f), // gold
        new Color(0.26f, 0.56f, 0.86f), // sky
        new Color(0.32f, 0.70f, 0.34f), // sage
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
        // Defaults to Claude (enum value 0) for manifests written before provider selection
        // existed - JsonUtility leaves a field missing from the JSON at its C# default.
        public AiProviderId ProviderId;
    }

    [SerializeField] private List<SessionRecord> sessionRecords = new List<SessionRecord>();
    [SerializeField] private int activeSessionIndex;
    [SerializeField] private bool turnStepperCollapsed;
    [SerializeField] private bool soundEnabled = true;
    [SerializeField] private int soundVariant;
    [SerializeField] private bool characterRoomExpanded;
    // 0 = Dark (default), 1 = Light. See ApplyTheme/AiCompanionStyles.Light.uss.
    [SerializeField] private int theme;
    // 0 = Korean (default), 1 = English. Mirrored into CompanionPreferences.ResponseLanguage
    // (see its comment) so CompanionSession can read it without a window reference.
    [SerializeField] private int language;

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

    // Folder name deliberately left as "ClaudeCompanion" (not renamed alongside the class/
    // window in the 2026-07-23 rebrand) - this is where the on-disk session manifest already
    // lives for anyone using this tool today; changing it would silently orphan their existing
    // sessions.json on next load instead of just being a cosmetic rename.
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
    // log file, and the next Send() would start a brand new AI conversation instead of
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

    // Sessions whose turn finished (or errored) while they weren't the visible tab - drives a
    // small badge in the sidebar (see BuildSessionRow) so the user notices without switching
    // over speculatively. Cleared on SwitchToSession. Not persisted - a fresh reload starting
    // with no badges is fine, this is just an attention cue.
    private readonly HashSet<CompanionSession> unseenCompletions = new HashSet<CompanionSession>();

    // Index of the session currently being renamed inline in the sidebar, or -1. See
    // BeginRenameSession/CommitRename.
    private int renamingSessionIndex = -1;

    // Live filter over the active session's chat history - see RefreshChat. Not persisted,
    // resets on session switch/reload same as scroll position would.
    private string chatSearchQuery = "";

    private VisualElement sidebarContainer;
    private VisualElement mainColumn;
    private CharacterStageElement characterStage;
    private Button bridgeToggleButton;
    private VisualElement bridgeDot;
    private Label bridgeLabel;
    private Button settingsButton;
    private ScrollView stepperScroll;
    private VisualElement stepperContent;
    private Button stepperToggleButton;
    private ScrollView chatScrollView;
    private VisualElement chatHistoryContainer;
    private VisualElement chatTrailingContainer;
    private int renderedHistoryCount;
    private Label pendingCountLabel;
    private TextField inputField;
    private Button sendButton;
    private Button cancelButton;

    // Manual undo stack for the input field - UI Toolkit's TextField doesn't reliably support
    // native Ctrl+Z in a multiline field in practice (2026-07-16 request), so previous values
    // are captured on every change and restored on Ctrl+Z instead.
    private readonly List<string> inputUndoStack = new List<string>();
    private bool isApplyingInputUndo;

    // Non-null exactly while a "typing..." bubble is showing at the bottom of the chat - see
    // BuildTypingIndicator/RefreshChat. Animated from OnAnimationTick rather than a dedicated
    // scheduled callback so it shares the same 60fps tick as the character stage.
    private VisualElement[] typingDots;

    private void OnEnable()
    {
        // CompanionPreferences is a plain static field, so it doesn't survive a domain reload
        // even though the [SerializeField] language backing it does - resync on every OnEnable.
        ApplyLanguagePreference();

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
                CompanionSession newSession = new CompanionSession(
                    record.SessionKey, record.RestoredSessionId, projectRoot, record.ProviderId);
                newSession.Changed += () =>
                {
                    record.RestoredSessionId = newSession.RestoredSessionId;
                    SaveManifest();
                };
                newSession.OnTurnComplete += () => OnAnySessionTurnComplete(newSession);
                newSession.OnError += _ => OnAnySessionTurnComplete(newSession);
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

        bridgeRunning = UnityMcpBridgeAccessor.IsBridgeRunning;

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
        // Always loaded alongside the base sheet - gated purely by the "theme-light" class
        // toggled in ApplyTheme, not by whether the sheet itself is present, so switching themes
        // at runtime doesn't need to add/remove stylesheets.
        StyleSheet lightStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(LightStyleSheetPath);
        if (lightStyleSheet != null)
        {
            root.styleSheets.Add(lightStyleSheet);
        }
        ApplyTheme();

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
        // them, so this tick is the only thing driving redraws here. ~30fps (was 16ms/60fps) -
        // this is gentle easing/bob motion, not action animation, so the halved tick rate isn't
        // visible but does halve this window's idle CPU cost (2026-07-16 optimization request).
        root.schedule.Execute(OnAnimationTick).Every(33);

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
            kv.Value.style.backgroundColor = CharacterStageElement.GetIndicatorColor(kv.Key.CurrentActivity, kv.Key.Concept);
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
        bridgeRunning = UnityMcpBridgeAccessor.IsBridgeRunning;

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

        Button addButton = new Button(ShowAddSessionMenu) { text = "+ 새 세션" };
        addButton.AddToClassList("new-session-button");
        sidebarContainer.Add(addButton);
    }

    // Step 3 of the multi-provider plan (2026-07-23): which AI a new session talks to is picked
    // here, at creation time (also changeable afterward from the controls row - see
    // CompanionSession.SwitchProvider). Not-yet-implemented providers stay selectable rather
    // than disabled - NotImplementedSessionRunner gives a friendly in-chat error on Send, same
    // as switching to one mid-session, so there's no dead end to avoid here either.
    private void ShowAddSessionMenu()
    {
        GenericMenu menu = new GenericMenu();
        foreach (AiProviderDefinition provider in AiProviderRegistry.All)
        {
            string label = provider.IsImplemented ? provider.DisplayName : provider.DisplayName + " (준비 중)";
            menu.AddItem(new GUIContent(label), false, () => AddNewSession(provider.Id));
        }
        menu.ShowAsContext();
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
        dot.style.backgroundColor = CharacterStageElement.GetIndicatorColor(s.CurrentActivity, s.Concept);
        row.Add(dot);
        sidebarDots[s] = dot;

        string label = index < sessionRecords.Count ? sessionRecords[index].DisplayName : $"세션 {index + 1}";

        if (index == renamingSessionIndex)
        {
            TextField renameField = new TextField { value = label };
            renameField.AddToClassList("session-rename-field");
            row.Add(renameField);
            renameField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    evt.StopPropagation();
                    CommitRename(index, renameField.value);
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    evt.StopPropagation();
                    renamingSessionIndex = -1;
                    RebuildSidebar();
                }
            }, TrickleDown.TrickleDown);
            renameField.RegisterCallback<FocusOutEvent>(_ => CommitRename(index, renameField.value));
            renameField.schedule.Execute(() =>
            {
                renameField.Focus();
                renameField.SelectAll();
            });
        }
        else
        {
            Label nameLabel = new Label(label);
            nameLabel.AddToClassList("session-label");
            if (isActive)
            {
                nameLabel.AddToClassList("session-label--active");
            }
            // Double-click (or more) renames instead of just switching - the single-click
            // switch on the first click of a double-click is harmless since a rename target is
            // almost always already the active tab.
            nameLabel.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.clickCount >= 2)
                {
                    BeginRenameSession(index);
                }
                else
                {
                    SwitchToSession(index);
                }
            });
            row.Add(nameLabel);

            if (unseenCompletions.Contains(s))
            {
                VisualElement badge = new VisualElement();
                badge.AddToClassList("session-unseen-badge");
                badge.tooltip = "이 세션의 턴이 끝났습니다";
                row.Add(badge);
            }
        }

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
        unseenCompletions.Remove(sessions[index]);
        SaveManifest();
        RebuildSidebar();
        RebuildMainColumn();
    }

    private void BeginRenameSession(int index)
    {
        renamingSessionIndex = index;
        RebuildSidebar();
    }

    // Shared by both the rename field's Enter key and its FocusOut - whichever fires first
    // commits and flips renamingSessionIndex back to -1, so the other one (which follows
    // immediately once RebuildSidebar tears the field down) is a no-op instead of a double
    // commit/rebuild.
    private void CommitRename(int index, string newName)
    {
        if (renamingSessionIndex != index)
        {
            return;
        }
        renamingSessionIndex = -1;
        string trimmed = (newName ?? "").Trim();
        if (!string.IsNullOrEmpty(trimmed) && index < sessionRecords.Count)
        {
            sessionRecords[index].DisplayName = trimmed;
            SaveManifest();
        }
        RebuildSidebar();
    }

    private static void CopyToClipboard(string text)
    {
        EditorGUIUtility.systemCopyBuffer = text;
    }

    // Always-on (not tied to which tab is visible) - see the OnTurnComplete/OnError hookup in
    // OnEnable/AddNewSession. Only actually flags a badge when the finishing session isn't the
    // one on screen; the active session's own completion is already obvious from the chat/character.
    private void OnAnySessionTurnComplete(CompanionSession session)
    {
        if (session == ActiveSession)
        {
            return;
        }
        unseenCompletions.Add(session);
        RebuildSidebar();
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
        unseenCompletions.Remove(session);
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

    private void AddNewSession(AiProviderId providerId)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        SessionRecord record = new SessionRecord
        {
            SessionKey = Guid.NewGuid().ToString("N"),
            DisplayName = $"세션 {sessionRecords.Count + 1}",
            ProviderId = providerId
        };
        sessionRecords.Add(record);

        try
        {
            CompanionSession newSession = new CompanionSession(record.SessionKey, null, projectRoot, providerId);
            newSession.Changed += () =>
            {
                record.RestoredSessionId = newSession.RestoredSessionId;
                SaveManifest();
            };
            newSession.OnTurnComplete += () => OnAnySessionTurnComplete(newSession);
            newSession.OnError += _ => OnAnySessionTurnComplete(newSession);
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

        AiProviderDefinition provider = AiProviderRegistry.Get(providerId);
        if (provider.IsInstalled != null && !provider.IsInstalled())
        {
            OfferInstall(provider);
        }
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
            boundSession.OnTurnComplete -= OnActiveTurnComplete;
            boundSession.OnError -= OnActiveTurnError;
            boundSession = null;
        }

        mainColumn.Clear();
        characterStage = null;
        bridgeToggleButton = null;
        bridgeDot = null;
        bridgeLabel = null;
        settingsButton = null;
        stepperScroll = null;
        stepperContent = null;
        stepperToggleButton = null;
        chatScrollView = null;
        chatHistoryContainer = null;
        chatTrailingContainer = null;
        renderedHistoryCount = 0;
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
        characterStage.SetConcept(ActiveSession.Concept);
        characterStage.Expanded = characterRoomExpanded;
        characterStage.ExpandedChanged += expanded => characterRoomExpanded = expanded;
        mainColumn.Add(characterStage);

        mainColumn.Add(BuildControlsRow());

        mainColumn.Add(BuildStepperSection());

        VisualElement hDivider = new VisualElement();
        hDivider.AddToClassList("horizontal-divider");
        mainColumn.Add(hDivider);

        mainColumn.Add(BuildChatArea());

        boundSession = ActiveSession;
        boundSession.Changed += OnSessionChanged;
        boundSession.OnTurnComplete += OnActiveTurnComplete;
        boundSession.OnError += OnActiveTurnError;

        OnSessionChanged();
    }

    private void OnSessionChanged()
    {
        // Keeps the character's palette in sync with ActiveSession.Concept after SwitchProvider
        // - SetConcept only ever ran once at tab-build time before, so a provider switch would
        // otherwise leave the character showing the old AI's colors.
        characterStage?.SetConcept(ActiveSession?.Concept);
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
            stepperContent.Add(BuildStepChip(entry, ActiveSession.Concept));
        }
    }

    private static VisualElement BuildStepChip(string entry, AiCharacterConcept concept)
    {
        DescribeStep(entry, concept, out Color color, out string icon, out string label);

        VisualElement chip = new VisualElement();
        chip.AddToClassList("step-chip");
        chip.style.borderTopColor = color;
        chip.style.borderRightColor = color;
        chip.style.borderBottomColor = color;
        chip.style.borderLeftColor = color;

        Label iconLabel = new Label(icon);
        iconLabel.AddToClassList("step-chip-icon");
        chip.Add(iconLabel);

        Label text = new Label(label);
        text.AddToClassList("step-chip-label");
        chip.Add(text);

        return chip;
    }

    // Maps one raw CompanionSession activity-log entry to a chip color + icon + friendly label.
    private static void DescribeStep(string entry, AiCharacterConcept concept, out Color color, out string icon, out string label)
    {
        const string toolUsePrefix = "tool_use: ";
        const string errorPrefix = "ERROR: ";
        const string systemPrefix = "system: ";

        if (entry.StartsWith(toolUsePrefix))
        {
            string toolName = entry.Substring(toolUsePrefix.Length);
            color = CharacterStageElement.GetIndicatorColor(CompanionSession.ClassifyTool(toolName), concept);
            icon = FriendlyToolIcon(toolName);
            label = FriendlyToolLabel(toolName);
            return;
        }
        if (entry == "tool_result received")
        {
            color = CharacterStageElement.GetIndicatorColor(CharacterActivity.Thinking, concept);
            icon = ThinkingIcon;
            label = "결과 확인";
            return;
        }
        if (entry.StartsWith(errorPrefix))
        {
            color = StepErrorColor;
            icon = ErrorIcon;
            label = Truncate(entry.Substring(errorPrefix.Length), 40);
            return;
        }
        if (entry.StartsWith(systemPrefix))
        {
            color = CharacterStageElement.GetIndicatorColor(CharacterActivity.Thinking, concept);
            icon = SystemIcon;
            label = "시스템: " + Truncate(entry.Substring(systemPrefix.Length), 30);
            return;
        }

        color = CharacterStageElement.GetIndicatorColor(CharacterActivity.Thinking, concept);
        icon = ThinkingIcon;
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

    private static string FriendlyToolIcon(string toolName)
    {
        if (ToolIcons.TryGetValue(toolName, out string icon))
        {
            return icon;
        }
        return toolName.StartsWith("mcp__") ? McpToolIcon : DefaultToolIcon;
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
        PlayNotificationSound();
    }

    private void OnActiveTurnError(string _)
    {
        characterStage?.FlashError();
    }

    // Exposed for AiCompanionSettingsWindow (a separate small popup, same pattern as
    // AiCompanionSendDialog) to read/write and for its "테스트 재생" button.
    public bool SoundEnabled
    {
        get => soundEnabled;
        set => soundEnabled = value;
    }

    // 0 = 기본음 (single beep), 1 = 강조음 (double beep). A plain system beep rather than an
    // imported AudioClip - this is an Editor tool, not the game, so pulling in an audio asset
    // (plus the import/mixer plumbing that implies) for a notification ding would be a lot of
    // weight for very little; a couple of Beep() timing patterns is enough variety.
    public int SoundVariant
    {
        get => soundVariant;
        set => soundVariant = value;
    }

    // Exposed for AiCompanionSettingsWindow, same pattern as SoundEnabled/SoundVariant.
    // 0 = Dark, 1 = Light.
    public int Theme
    {
        get => theme;
        set
        {
            theme = value;
            ApplyTheme();
        }
    }

    private void ApplyTheme()
    {
        if (rootVisualElement == null)
        {
            return;
        }
        if (theme == 1)
        {
            rootVisualElement.AddToClassList("theme-light");
        }
        else
        {
            rootVisualElement.RemoveFromClassList("theme-light");
        }
    }

    // 0 = Korean, 1 = English - see CompanionPreferences for how CompanionSession picks this up.
    public int Language
    {
        get => language;
        set
        {
            language = value;
            ApplyLanguagePreference();
        }
    }

    private void ApplyLanguagePreference()
    {
        CompanionPreferences.ResponseLanguage =
            language == 1 ? CompanionPreferences.Language.English : CompanionPreferences.Language.Korean;
    }

    public void PlayNotificationSound()
    {
        if (!soundEnabled)
        {
            return;
        }
        EditorApplication.Beep();
        if (soundVariant == 1)
        {
            rootVisualElement.schedule.Execute(() => EditorApplication.Beep()).ExecuteLater(150);
        }
    }

    private VisualElement BuildControlsRow()
    {
        VisualElement row = new VisualElement();
        row.AddToClassList("controls-row");

        // MCPForUnity is an optional companion package (Unity-side tool calls only) - when it
        // isn't installed there's nothing to start/stop, so show a plain note instead of a
        // dead toggle (2026-07-23: this tool must still fully work, chat included, without it).
        if (UnityMcpBridgeAccessor.IsAvailable)
        {
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
        }
        else
        {
            Label noBridgeLabel = new Label("MCP 연결 없음 (채팅은 그대로 사용 가능)");
            noBridgeLabel.AddToClassList("bridge-label");
            noBridgeLabel.tooltip = "MCPForUnity 패키지가 설치되어 있지 않습니다. Unity 도구 호출 없이 채팅만 사용할 수 있습니다.";
            row.Add(noBridgeLabel);
        }

        // Live provider switch for the active session (2026-07-23 request: "지금 세션에서도
        // 변경이 가능하도록") - not-yet-implemented providers stay selectable (not disabled)
        // since NotImplementedSessionRunner already gives a friendly in-chat error on Send,
        // same as picking one at session creation.
        List<string> providerChoices = new List<string>();
        int currentProviderIndex = 0;
        for (int i = 0; i < AiProviderRegistry.All.Count; i++)
        {
            AiProviderDefinition p = AiProviderRegistry.All[i];
            providerChoices.Add(p.IsImplemented ? p.DisplayName : p.DisplayName + " (준비 중)");
            if (ActiveSession != null && p.Id == ActiveSession.ProviderId)
            {
                currentProviderIndex = i;
            }
        }
        DropdownField providerDropdown = new DropdownField();
        providerDropdown.choices = providerChoices;
        providerDropdown.index = currentProviderIndex;
        providerDropdown.AddToClassList("provider-dropdown");
        providerDropdown.tooltip = "이 세션이 사용할 AI를 선택합니다.";
        providerDropdown.SetEnabled(ActiveSession != null);
        providerDropdown.RegisterValueChangedCallback(evt =>
        {
            int index = providerChoices.IndexOf(evt.newValue);
            if (index < 0 || ActiveSession == null)
            {
                return;
            }
            AiProviderDefinition selected = AiProviderRegistry.All[index];
            if (selected.Id == ActiveSession.ProviderId)
            {
                return;
            }
            ActiveSession.SwitchProvider(selected.Id);
            if (selected.IsInstalled != null && !selected.IsInstalled())
            {
                OfferInstall(selected);
            }
        });
        row.Add(providerDropdown);

        VisualElement spacer = new VisualElement();
        spacer.AddToClassList("spacer");
        row.Add(spacer);

        settingsButton = new Button(() => AiCompanionSettingsWindow.Open(this)) { text = "⚙ 설정" };
        settingsButton.AddToClassList("settings-button");
        row.Add(settingsButton);

        // Fallback path for the chat input row's recurring visibility bugs under the old IMGUI
        // implementation: an independent popup window that shares none of this window's
        // layout. Kept for now even though UI Toolkit removes the root cause of that bug class
        // - safe to retire once the new input field has proven stable for a while.
        Button fallbackButton = new Button(() => AiCompanionSendDialog.Open(this)) { text = "대체 입력창" };
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
        bridgeToggleButton.RemoveFromClassList("bridge-toggle-button--running");
        bridgeToggleButton.RemoveFromClassList("bridge-toggle-button--stopped");
        bridgeToggleButton.AddToClassList(bridgeRunning ? "bridge-toggle-button--running" : "bridge-toggle-button--stopped");
        bridgeDot.style.backgroundColor = bridgeRunning ? StoppedColor : BridgeDotOffColor;
        bridgeLabel.text = bridgeRunning ? "브릿지 연결됨" : "브릿지 중지됨";
    }

    private async void StartSession()
    {
        if (!UnityMcpBridgeAccessor.IsLocalHttpServerRunning())
        {
            UnityMcpBridgeAccessor.StartLocalHttpServer();
        }

        if (!UnityMcpBridgeAccessor.IsBridgeRunning)
        {
            await UnityMcpBridgeAccessor.StartBridgeAsync();
        }

        bridgeRunning = UnityMcpBridgeAccessor.IsBridgeRunning;

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
        // The MCP bridge/local HTTP server are singletons shared by every session in this
        // window, not per-session - stopping them here would silently cut
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
        await UnityMcpBridgeAccessor.StopBridgeAsync();
        UnityMcpBridgeAccessor.StopLocalHttpServer();
        bridgeRunning = UnityMcpBridgeAccessor.IsBridgeRunning;

        UpdateBridgeControlsVisual();
        UpdateSendControlsEnabled();
    }

    private VisualElement BuildChatArea()
    {
        VisualElement container = new VisualElement();
        container.AddToClassList("chat-area");

        VisualElement headerRow = new VisualElement();
        headerRow.AddToClassList("chat-header-row");

        Label chatTitle = new Label("채팅");
        chatTitle.AddToClassList("sidebar-title");
        headerRow.Add(chatTitle);

        VisualElement headerSpacer = new VisualElement();
        headerSpacer.AddToClassList("spacer");
        headerRow.Add(headerSpacer);

        TextField searchField = new TextField { value = chatSearchQuery };
        searchField.AddToClassList("chat-search-field");
        searchField.tooltip = "대화 검색";
        VisualElement searchInner = searchField.Q(className: TextField.inputUssClassName);
        searchInner?.AddToClassList("chat-search-field-inner");
        searchField.RegisterValueChangedCallback(evt =>
        {
            chatSearchQuery = evt.newValue;
            RefreshChat();
        });
        headerRow.Add(searchField);

        Button exportButton = new Button(ExportActiveSessionChat) { text = "내보내기" };
        exportButton.AddToClassList("chat-export-button");
        headerRow.Add(exportButton);

        container.Add(headerRow);

        chatScrollView = new ScrollView(ScrollViewMode.Vertical);
        chatScrollView.AddToClassList("chat-scroll");
        container.Add(chatScrollView);

        // Split so RefreshChat can append-only to history instead of rebuilding it every time -
        // see RefreshChat's perf note. Trailing (pending/typing) is small and always redrawn.
        chatHistoryContainer = new VisualElement();
        chatScrollView.Add(chatHistoryContainer);
        chatTrailingContainer = new VisualElement();
        chatScrollView.Add(chatTrailingContainer);
        renderedHistoryCount = 0;

        pendingCountLabel = new Label();
        pendingCountLabel.AddToClassList("pending-count-label");
        pendingCountLabel.style.display = DisplayStyle.None;
        container.Add(pendingCountLabel);

        // Wrapped in a ScrollView (instead of the TextField sitting directly in the layout)
        // purely so a message longer than the max height gets mouse-wheel scrolling and a
        // scrollbar instead of just clipping with no way to see the rest (2026-07-16 request) -
        // the TextField itself grows to its full natural content height inside, ScrollView owns
        // the min/max-height clamping (see "chat-input-scroll" in the USS).
        VisualElement inputScroll = new ScrollView(ScrollViewMode.Vertical);
        inputScroll.AddToClassList("chat-input-scroll");
        container.Add(inputScroll);

        inputField = new TextField { multiline = true };
        inputField.AddToClassList("chat-input");
        VisualElement innerInput = inputField.Q(className: TextField.inputUssClassName);
        innerInput?.AddToClassList("chat-input-inner");
        // Enter sends; Shift+Enter falls through to the field's own default handling so a
        // multi-line message can still be composed. Registered on the *inner* text-input
        // element (not the outer TextField wrapper) in the trickle-down (capture) phase, and
        // using StopImmediatePropagation - the outer wrapper is just a composite control, the
        // actual native "insert a newline on Return" handling lives on this inner element, and
        // registering any further away/weaker than this let a stray newline through after Send
        // in practice (report: message sent correctly, but a blank line was left behind/typed
        // next). TrySend also defensively re-clears next frame as a second safety net in case
        // the native handling still slips something in. Same capture-phase registration also
        // owns Ctrl+Z - see OnInputKeyDown/inputUndoStack for why a manual undo stack was
        // needed instead of relying on the native field's own undo (2026-07-16 request: it
        // wasn't undoing in this multiline field in practice).
        (innerInput ?? inputField).RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
        inputField.RegisterValueChangedCallback(evt =>
        {
            if (!isApplyingInputUndo)
            {
                inputUndoStack.Add(evt.previousValue);
                if (inputUndoStack.Count > 200)
                {
                    inputUndoStack.RemoveAt(0);
                }
            }
            UpdateSendControlsEnabled();
        });
        inputScroll.Add(inputField);

        VisualElement buttonsRow = new VisualElement();
        buttonsRow.AddToClassList("chat-buttons-row");

        sendButton = new Button(TrySend) { text = "보내기" };
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
            evt.StopImmediatePropagation();
            evt.PreventDefault();
            TrySend();
            return;
        }

        // Shift+Enter inserts a newline instead of sending. Handled explicitly (splicing the
        // string at the cursor) rather than just letting the event fall through to the native
        // TextField - the native multiline handling didn't reliably insert a line break for
        // Shift+Return in practice (2026-07-23 request).
        if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && evt.shiftKey)
        {
            evt.StopImmediatePropagation();
            evt.PreventDefault();
            string current = inputField.value ?? "";
            int cursor = Mathf.Clamp(inputField.cursorIndex, 0, current.Length);
            inputField.value = current.Insert(cursor, "\n");
            inputField.cursorIndex = cursor + 1;
            inputField.selectIndex = cursor + 1;
            return;
        }

        if (evt.keyCode == KeyCode.Z && (evt.ctrlKey || evt.commandKey))
        {
            evt.StopImmediatePropagation();
            evt.PreventDefault();
            if (inputUndoStack.Count > 0)
            {
                string previous = inputUndoStack[inputUndoStack.Count - 1];
                inputUndoStack.RemoveAt(inputUndoStack.Count - 1);
                isApplyingInputUndo = true;
                inputField.value = previous;
                isApplyingInputUndo = false;
            }
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

        // Safety net: if the Enter keystroke that triggered this still manages to insert a
        // newline into the field afterward (native text-input handling racing this callback),
        // clean it up next frame rather than leaving a stray blank line sitting in an otherwise
        // "just sent" input box.
        TextField capturedField = inputField;
        capturedField.schedule.Execute(() =>
        {
            if (capturedField.value != "")
            {
                capturedField.SetValueWithoutNotify("");
            }
        });
    }

    // Whenever a message is added (sent, queued, or a reply arrives), sync the bubble list and
    // jump the scroll view to the bottom - bound to CompanionSession.Changed for whichever
    // session is currently active (see RebuildMainColumn).
    //
    // Performance note (2026-07-16): this used to Clear()+rebuild every bubble in the whole
    // history on every single call - Changed fires on every tool_use/assistant-text/turn-complete
    // event, so a long conversation meant re-creating hundreds of VisualElements (each now with
    // its own copy button too) many times a minute. chatHistoryContainer is append-only for the
    // common case now; only a search query (or the history actually shrinking, e.g. a
    // conversation reset) pays for a full rebuild.
    private void RefreshChat()
    {
        if (chatScrollView == null || ActiveSession == null)
        {
            return;
        }

        bool hasQuery = !string.IsNullOrWhiteSpace(chatSearchQuery);
        string query = chatSearchQuery?.Trim() ?? "";

        if (hasQuery || renderedHistoryCount > ActiveSession.ChatMessages.Count)
        {
            chatHistoryContainer.Clear();
            renderedHistoryCount = 0;
            int matchCount = 0;
            foreach (ChatMessage message in ActiveSession.ChatMessages)
            {
                if (hasQuery && message.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                matchCount++;
                AddChatBubble(chatHistoryContainer, message, pending: false);
            }
            renderedHistoryCount = ActiveSession.ChatMessages.Count;
            if (hasQuery && matchCount == 0)
            {
                Label noResults = new Label("일치하는 메시지가 없습니다.");
                noResults.AddToClassList("chat-no-results-label");
                chatHistoryContainer.Add(noResults);
            }
        }
        else
        {
            for (int i = renderedHistoryCount; i < ActiveSession.ChatMessages.Count; i++)
            {
                AddChatBubble(chatHistoryContainer, ActiveSession.ChatMessages[i], pending: false);
            }
            renderedHistoryCount = ActiveSession.ChatMessages.Count;
        }

        // Pending/typing are always small (0-3 items) and inherently "redraw every time" state,
        // so clearing just this trailing container each call is cheap regardless of history size.
        chatTrailingContainer.Clear();
        foreach (string pendingText in ActiveSession.PendingMessages)
        {
            AddChatBubble(chatTrailingContainer, new ChatMessage("You", pendingText), pending: true);
        }

        // Thinking (not just "busy") on purpose: while a tool is actually running
        // (Reading/Editing/Running), the character stage + stepper already say so in detail -
        // showing this "..." bubble for that whole span too read as "a reply is coming any
        // second" when a long tool call was actually still in progress, which was confusing
        // (user report, 2026-07-16). Thinking is specifically the "about to answer" state.
        if (ActiveSession.CurrentActivity == CharacterActivity.Thinking)
        {
            chatTrailingContainer.Add(BuildTypingIndicator());
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
        // float.MaxValue instead of reading contentContainer.layout.height: ScrollView clamps
        // scrollOffset to its real max internally, and reading the height directly was racing
        // layout on a freshly-opened window with a lot of restored history - one deferred frame
        // wasn't always enough for every bubble's layout pass to finish, so the read height was
        // sometimes still short and landed the scroll partway up instead of at the true bottom
        // (user report, 2026-07-16: reopening after an Editor restart showed the chat scrolled
        // to the top). Scheduling a couple of retries over the next few frames covers the same
        // "layout still settling" window without needing to measure it ourselves.
        chatScrollView.schedule.Execute(() => chatScrollView.scrollOffset = new Vector2(0f, float.MaxValue));
        chatScrollView.schedule.Execute(() => chatScrollView.scrollOffset = new Vector2(0f, float.MaxValue)).ExecuteLater(50);
        chatScrollView.schedule.Execute(() => chatScrollView.scrollOffset = new Vector2(0f, float.MaxValue)).ExecuteLater(200);
    }

    private void ExportActiveSessionChat()
    {
        if (ActiveSession == null || ActiveSession.ChatMessages.Count == 0)
        {
            return;
        }

        string sessionName = activeSessionIndex < sessionRecords.Count
            ? sessionRecords[activeSessionIndex].DisplayName
            : $"세션 {activeSessionIndex + 1}";
        string path = EditorUtility.SaveFilePanel("대화 내보내기", "", $"{sessionName}-대화", "md");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        foreach (ChatMessage message in ActiveSession.ChatMessages)
        {
            sb.Append("**").Append(message.Role).Append("**\n\n");
            sb.Append(message.Text).Append("\n\n");
        }

        try
        {
            File.WriteAllText(path, sb.ToString());
            EditorUtility.RevealInFinder(path);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void AddChatBubble(VisualElement container, ChatMessage message, bool pending)
    {
        if (message.IsSystemNotice)
        {
            VisualElement noticeRow = new VisualElement();
            noticeRow.AddToClassList("chat-system-notice-row");
            Label noticeLabel = new Label(message.Text);
            noticeLabel.AddToClassList("chat-system-notice");
            noticeRow.Add(noticeLabel);
            container.Add(noticeRow);
            return;
        }

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

        // Only shown on hover (see "bubble-copy-button" in USS) - copies the whole message,
        // as opposed to each code block's own copy button below which copies just that snippet.
        string fullText = message.Text;
        Button copyButton = new Button(() => CopyToClipboard(fullText)) { text = "⧉" };
        copyButton.AddToClassList("bubble-copy-button");
        copyButton.tooltip = "메시지 복사";
        bubble.Add(copyButton);

        row.Add(bubble);
        container.Add(row);
    }

    // A small "..." bubble shown at the bottom of the chat while CurrentActivity is Thinking
    // (not just IsBusy - see RefreshChat) - see RefreshChat/OnAnimationTick. Populates the
    // typingDots field the tick loop animates;
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
    // supported. Anything the AI sends that isn't one of those just prints as-is.
    private static void BuildBubbleContent(VisualElement bubble, string text)
    {
        foreach (ChatMarkdown.Segment segment in ChatMarkdown.Split(text))
        {
            if (segment.Kind == ChatMarkdown.SegmentKind.Image)
            {
                bubble.Add(BuildImageSegment(segment.Text));
                continue;
            }

            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            if (segment.Kind == ChatMarkdown.SegmentKind.Code)
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

                // Copies just this code segment, not the whole message - requested explicitly
                // (2026-07-16) so a code block can be grabbed without the surrounding prose.
                string codeText = segment.Text;
                Button codeCopyButton = new Button(() => CopyToClipboard(codeText)) { text = "⧉" };
                codeCopyButton.AddToClassList("code-copy-button");
                codeCopyButton.tooltip = "코드 복사";
                codeBlock.Add(codeCopyButton);

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

    // [[image: path]] (see ChatMarkdown) - path may be Assets-relative or an absolute path
    // (screenshots/generated files often land outside Assets). Renders the image inline with a
    // hover "저장" button so the user doesn't have to go find the file on disk (2026-07-16 request).
    private static VisualElement BuildImageSegment(string path)
    {
        VisualElement container = new VisualElement();
        container.AddToClassList("chat-image-container");

        string fullPath = ResolveImagePath(path);
        Texture2D texture = LoadImageFromDisk(fullPath);
        if (texture == null)
        {
            Label missing = new Label("(이미지를 표시할 수 없습니다: " + path + ")");
            missing.AddToClassList("chat-image-missing");
            container.Add(missing);
            return container;
        }

        Image image = new Image { image = texture, scaleMode = ScaleMode.ScaleToFit };
        image.AddToClassList("chat-image");
        container.Add(image);

        Button saveButton = new Button(() => SaveImageAs(fullPath)) { text = "💾 저장" };
        saveButton.AddToClassList("image-save-button");
        container.Add(saveButton);

        return container;
    }

    private static string ResolveImagePath(string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }
        return Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
    }

    private static Texture2D LoadImageFromDisk(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath))
            {
                return null;
            }
            byte[] bytes = File.ReadAllBytes(fullPath);
            Texture2D texture = new Texture2D(2, 2);
            texture.LoadImage(bytes);
            return texture;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            return null;
        }
    }

    private static void SaveImageAs(string fullSourcePath)
    {
        if (!File.Exists(fullSourcePath))
        {
            return;
        }
        string ext = Path.GetExtension(fullSourcePath).TrimStart('.');
        string defaultName = Path.GetFileNameWithoutExtension(fullSourcePath);
        string dest = EditorUtility.SaveFilePanel("이미지 저장", "", defaultName, ext);
        if (string.IsNullOrEmpty(dest))
        {
            return;
        }
        try
        {
            File.Copy(fullSourcePath, dest, true);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
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

    // Shared by both the inline chat row and AiCompanionSendDialog (the fallback input
    // window) so a message can be sent identically regardless of which UI triggered it. Always
    // targets whichever session is active at send time. Deliberately allows submitting while
    // ActiveSession.IsBusy - CompanionSession.Submit queues it instead of sending immediately.
    private bool CanSendMessage(string text)
    {
        // Deliberately not gated on bridgeRunning - the MCP bridge only affects whether the AI
        // can call Unity-side tools (manage_gameobject etc.), not whether it can chat at all.
        // Previously any message was blocked until the bridge was started, even a plain
        // question with no tool calls needed (2026-07-23 request: usable with MCP disconnected).
        return ActiveSession != null && !string.IsNullOrWhiteSpace(text);
    }

    public void SubmitMessage(string text)
    {
        if (!CanSendMessage(text))
        {
            return;
        }

        // Safety net for a session restored from an old manifest, or one whose provider's CLI
        // got uninstalled since it was picked - the same install offer the provider dropdown
        // shows proactively, so this can't be reached without ever being offered a fix.
        AiProviderDefinition provider = AiProviderRegistry.Get(ActiveSession.ProviderId);
        if (provider.IsInstalled != null && !provider.IsInstalled())
        {
            OfferInstall(provider);
            return;
        }

        ActiveSession.Submit(text);
    }

    // Offers to npm-install a provider's missing CLI - step 3.5 of the multi-provider plan
    // (2026-07-23 request: "만약 해당 AI설치가 안되어 있다면 해당 AI 설치를 해주거나 해줬으면
    // 해"). Only meaningful for providers with a real IsInstalled/InstallPackage wired up
    // (Claude, Codex today); NotImplementedSessionRunner-backed providers don't launch anything a PATH
    // check could verify, so they're not offered an install here at all.
    private void OfferInstall(AiProviderDefinition provider)
    {
        // Providers without an npm package (e.g. Cursor, whose official install is a
        // curl|bash script) can't be auto-installed - just let the user know it's missing
        // instead of offering a button that would run a broken "npm install -g " command.
        if (string.IsNullOrEmpty(provider.InstallPackage))
        {
            EditorUtility.DisplayDialog(
                $"{provider.DisplayName} CLI 없음",
                $"{provider.DisplayName} CLI가 이 컴퓨터에 설치되어 있지 않은 것 같습니다.\n\n이 프로바이더는 자동 설치를 지원하지 않아서, 공식 설치 방법으로 직접 설치한 뒤 다시 시도해주세요.",
                "확인");
            return;
        }

        bool install = EditorUtility.DisplayDialog(
            $"{provider.DisplayName} CLI 없음",
            $"{provider.DisplayName} CLI가 이 컴퓨터에 설치되어 있지 않은 것 같습니다.\n\n지금 설치할까요? (npm install -g {provider.InstallPackage})",
            "설치",
            "나중에");
        if (!install)
        {
            return;
        }

        Debug.Log($"[{provider.DisplayName}] 설치를 시작합니다: npm install -g {provider.InstallPackage}");
        CliInstaller.InstallNpmPackageAsync(provider.InstallPackage, success =>
        {
            if (success && provider.Id == AiProviderId.Claude)
            {
                ClaudeSessionRunner.ClearResolvedPathCache();
            }
            else if (success && provider.Id == AiProviderId.Codex)
            {
                CodexSessionRunner.ClearResolvedPathCache();
            }
            EditorUtility.DisplayDialog(
                success ? $"{provider.DisplayName} 설치 완료" : $"{provider.DisplayName} 설치 실패",
                success
                    ? "설치가 끝났습니다. 이제 메시지를 보내보세요."
                    : "설치 중 문제가 발생했습니다. Unity 콘솔 로그를 확인해주세요.",
                "확인");
        });
    }
}
