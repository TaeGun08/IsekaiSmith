using System;
using System.Collections.Generic;
using System.IO;
using MCPForUnity.Editor.Services;
using UnityEditor;
using UnityEngine;

public class ClaudeCompanionWindow : EditorWindow
{
    [MenuItem("Window/Claude Companion")]
    public static void ShowWindow()
    {
        ClaudeCompanionWindow window = GetWindow<ClaudeCompanionWindow>("Claude Companion");
        window.minSize = new Vector2(640, 760);
    }

    private static readonly Color BackgroundColor = new Color(0.13f, 0.13f, 0.15f);
    private static readonly Color StageColor = new Color(0.17f, 0.18f, 0.20f);
    private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color UserBubbleColor = new Color(0.20f, 0.36f, 0.58f);
    private static readonly Color ClaudeBubbleColor = new Color(0.24f, 0.24f, 0.28f);
    private static readonly Color InputFieldColor = new Color(0.24f, 0.24f, 0.28f);
    private static readonly Color RunningColor = new Color(0.85f, 0.35f, 0.35f);
    private static readonly Color StoppedColor = new Color(0.35f, 0.75f, 0.45f);
    private static readonly Color IdleBodyColor = new Color(0.55f, 0.62f, 0.72f);
    private static readonly Color BusyBodyColorA = new Color(1f, 0.62f, 0.25f);
    private static readonly Color BusyBodyColorB = new Color(1f, 0.85f, 0.4f);
    private static readonly Color ActiveSessionRowColor = new Color(1f, 1f, 1f, 0.07f);
    private static readonly Color CodeBlockColor = new Color(0.09f, 0.09f, 0.11f);

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

    private string inputText = "";
    private Vector2 chatScroll;
    private int lastChatMessageCount = -1;

    private const float SidebarWidth = 150f;
    // VerticalDivider: GUILayout.Space(4) + a 1px rect + GUILayout.Space(4).
    private const float SidebarDividerWidth = 9f;
    // Reserves room for the chat scroll view's own vertical scrollbar, which otherwise eats
    // into the same width a same-frame GetControlRect measurement inside that scroll view
    // was reporting as still free - see GetChatPaneWidth.
    private const float ChatScrollbarWidth = 16f;

    // Bounds for the auto-growing chat input box (see DrawChat) - min is one line, max caps
    // growth around ~5-6 lines so a long paste can't swallow the whole window.
    private const float MinInputHeight = 24f;
    private const float MaxInputHeight = 120f;

    private readonly List<CompanionSession> sessions = new List<CompanionSession>();
    private CompanionSession ActiveSession =>
        (activeSessionIndex >= 0 && activeSessionIndex < sessions.Count) ? sessions[activeSessionIndex] : null;

    private Texture2D circleTexture;

    private bool isBlinking;
    private double nextBlinkTime;
    private double blinkEndTime;

    // GUIStyle allocates on every construction, and OnGUI runs many times a second while
    // this window is open (see RepaintInterval below). Building these fresh per call/per
    // label was the single biggest per-frame allocation source, especially the activity
    // log style which used to be re-created for every entry, every frame. Built once,
    // lazily, on first use.
    private GUIStyle sectionHeaderStyle;
    private GUIStyle bubbleRoleStyle;
    private GUIStyle bubbleTextStyle;
    private GUIStyle characterStateLabelStyle;
    private GUIStyle logPathStyle;
    private GUIStyle inputFieldStyle;
    private GUIStyle sessionItemStyle;
    private GUIStyle activeSessionItemStyle;
    private GUIStyle codeBlockStyle;

    // A previous attempt throttled Repaint() by self-gating inside OnGUI (only calling
    // Repaint() again if enough time had passed). That broke the animation because OnGUI
    // only reruns when Repaint() fires - gating from inside that same loop makes the
    // effective frame timing depend on the editor's own unrelated scheduling, producing
    // visible jitter (see git history). Driving the gate from EditorApplication.update
    // instead - a callback that ticks on its own regardless of whether we just repainted -
    // keeps the interval regular while still capping how often the (potentially expensive,
    // unbounded chat history) OnGUI layout actually runs.
    private const double RepaintInterval = 1.0 / 30.0;
    private double lastRepaintTime;

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;

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
                    Repaint();
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

        if (circleTexture == null)
        {
            circleTexture = CreateCircleTexture(64);
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
        EditorApplication.update -= OnEditorUpdate;
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

    private void OnEditorUpdate()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - lastRepaintTime >= RepaintInterval)
        {
            lastRepaintTime = now;
            Repaint();
        }
    }

    private void OnGUI()
    {
        // The MCP bridge package can silently reconnect on its own after a domain reload
        // (HttpBridgeReloadHandler), but bridgeRunning was previously only refreshed in
        // OnEnable/StartSession/StopSession. That left the button stuck on "중지됨" long
        // after the real connection came back, forcing the user to click Start again -
        // which used to wipe the ongoing chat/session for no reason. Polling here keeps
        // the UI truthful without needing a dedicated event hook.
        try
        {
            bridgeRunning = MCPServiceLocator.Bridge.IsRunning;
        }
        catch (Exception)
        {
            // Services may be mid-teardown/setup right around a reload; keep the last
            // known value rather than logging every frame.
        }

        try
        {
            EnsureStylesInitialized();

            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), BackgroundColor);

            // Both groups need an explicit ExpandHeight so DrawChat's own ExpandHeight
            // scroll view actually gets capped to the leftover window space instead of
            // growing with chat content - without it, nothing constrains this column's
            // height and the input row/Send button get pushed further below the visible
            // window as the conversation grows (the sidebar was already fine since its own
            // BeginVertical below already has ExpandHeight).
            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            DrawSessionSidebar();
            VerticalDivider();

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            if (ActiveSession == null)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("활성 세션이 없습니다. '+ 새 세션'을 눌러주세요.", sectionHeaderStyle);
                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.Space(4);
                DrawCharacterStage();
                GUILayout.Space(6);
                DrawControls();
                Divider();
                DrawChat();
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }
        catch (Exception ex)
        {
            // A single bad frame (e.g. a null session right after a failed OnEnable)
            // shouldn't take the whole window down - log it and keep repainting.
            Debug.LogException(ex);
        }
    }

    private void EnsureStylesInitialized()
    {
        if (sectionHeaderStyle != null)
        {
            return;
        }

        sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.9f, 0.9f, 0.95f) },
            margin = new RectOffset(2, 0, 0, 4)
        };
        bubbleRoleStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = new Color(0.8f, 0.85f, 0.95f) },
            padding = new RectOffset(6, 6, 0, 0)
        };
        bubbleTextStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            normal = { textColor = Color.white },
            padding = new RectOffset(6, 6, 0, 0),
            // Lets ChatMarkdown.ToRichText's <b>/<color> tags actually render instead of
            // showing up as literal text.
            richText = true
        };
        codeBlockStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            normal = { textColor = new Color(0.82f, 0.88f, 0.85f) },
            padding = new RectOffset(6, 6, 3, 3),
            fontSize = 11,
            // Deliberately not rich-text: this holds fenced code verbatim, and running
            // markdown/rich-text conversion over real code would mangle it (e.g. generics,
            // comparison operators).
            richText = false
        };
        characterStateLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.85f, 0.85f, 0.9f) }
        };
        logPathStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(0.55f, 0.55f, 0.6f) }
        };
        sessionItemStyle = new GUIStyle(EditorStyles.label)
        {
            padding = new RectOffset(4, 4, 4, 4),
            normal = { textColor = new Color(0.75f, 0.75f, 0.8f) }
        };
        activeSessionItemStyle = new GUIStyle(sessionItemStyle)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        // The default EditorStyles.textArea skin barely contrasts against this window's
        // custom dark BackgroundColor when the field is empty - see DrawChat(). Nulling out
        // normal/focused.background here to force our own EditorGUI.DrawRect underneath was
        // tried first, but it made Unity's internal TextEditor throw a NullReferenceException
        // (in TextEditor.UpdateTextHandle/MoveCursorToPosition) as soon as the field was
        // clicked into - which, caught by OnGUI's outer try/catch, silently killed that
        // frame's rendering and looked exactly like the field "disappearing". Leaving the
        // native background alone and only tinting text color is the safe way to adjust this.
        // Based on textArea (not textField) and word-wrapped so the input can grow to more
        // than one line - see DrawChat's auto-growing input box.
        inputFieldStyle = new GUIStyle(EditorStyles.textArea)
        {
            normal = { textColor = Color.white },
            focused = { textColor = Color.white },
            padding = new RectOffset(6, 6, 4, 4),
            wordWrap = true
        };
    }

    private void DrawSessionSidebar()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));
        EditorGUILayout.LabelField("세션", sectionHeaderStyle);

        for (int i = 0; i < sessions.Count; i++)
        {
            DrawSessionListItem(i);
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("+ 새 세션", GUILayout.Height(22)))
        {
            AddNewSession();
        }
        GUILayout.Space(4);
        EditorGUILayout.EndVertical();
    }

    private void DrawSessionListItem(int index)
    {
        CompanionSession s = sessions[index];
        bool isActive = index == activeSessionIndex;

        Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(24));
        if (isActive)
        {
            EditorGUI.DrawRect(rowRect, ActiveSessionRowColor);
        }

        Rect dotRect = GUILayoutUtility.GetRect(10, 24, GUILayout.Width(10));
        GUI.color = s.IsBusy ? BusyBodyColorA : IdleBodyColor;
        GUI.DrawTexture(new Rect(dotRect.x, dotRect.y + 8, 8, 8), circleTexture);
        GUI.color = Color.white;

        string label = index < sessionRecords.Count ? sessionRecords[index].DisplayName : $"세션 {index + 1}";
        GUIStyle style = isActive ? activeSessionItemStyle : sessionItemStyle;
        if (GUILayout.Button(label, style))
        {
            activeSessionIndex = index;
            SaveManifest();
            GUI.FocusControl(null);
            Repaint();
        }

        EditorGUILayout.EndHorizontal();
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
                Repaint();
            };
            sessions.Add(newSession);
            activeSessionIndex = sessions.Count - 1;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
        SaveManifest();
        Repaint();
    }

    private void DrawCharacterStage()
    {
        Rect stage = GUILayoutUtility.GetRect(position.width, 96, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(stage, StageColor);

        bool busy = ActiveSession != null && ActiveSession.IsBusy;
        float t = (float)EditorApplication.timeSinceStartup;

        Vector2 center = new Vector2(stage.x + stage.width / 2f, stage.y + stage.height / 2f + 4f);

        float bobAmplitude = busy ? 7f : 3f;
        float bobSpeed = busy ? 6f : 2f;
        float bobY = Mathf.Sin(t * bobSpeed) * bobAmplitude;

        if (busy)
        {
            int dotCount = 3;
            float orbitRadius = 42f;
            for (int i = 0; i < dotCount; i++)
            {
                float angle = t * 4f + i * (Mathf.PI * 2f / dotCount);
                Vector2 dotPos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * orbitRadius;
                GUI.color = new Color(1f, 0.85f, 0.4f, 0.85f);
                GUI.DrawTexture(new Rect(dotPos.x - 4f, dotPos.y - 4f, 8f, 8f), circleTexture);
            }
            GUI.color = Color.white;
        }

        const float bodySize = 56f;
        Color bodyColor = busy
            ? Color.Lerp(BusyBodyColorA, BusyBodyColorB, Mathf.PingPong(t * 3f, 1f))
            : IdleBodyColor;

        GUI.color = bodyColor;
        GUI.DrawTexture(new Rect(center.x - bodySize / 2f, center.y - bodySize / 2f + bobY, bodySize, bodySize), circleTexture, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        UpdateBlink(t);
        float eyeOpen = isBlinking ? 0.15f : 1f;
        const float eyeSize = 8f;
        const float eyeSpacing = 10f;
        const float eyeYOffset = -6f;
        float eyeHeight = eyeSize * eyeOpen;
        float eyeY = center.y + bobY + eyeYOffset - eyeHeight / 2f;

        GUI.color = new Color(0.15f, 0.15f, 0.18f);
        GUI.DrawTexture(new Rect(center.x - eyeSpacing - eyeSize / 2f, eyeY, eyeSize, eyeHeight), circleTexture);
        GUI.DrawTexture(new Rect(center.x + eyeSpacing - eyeSize / 2f, eyeY, eyeSize, eyeHeight), circleTexture);
        GUI.color = Color.white;

        GUI.Label(new Rect(stage.x, stage.yMax - 16, stage.width, 16), busy ? "열심히 작업 중..." : "대기 중", characterStateLabelStyle);
    }

    private void UpdateBlink(double t)
    {
        if (nextBlinkTime <= 0)
        {
            nextBlinkTime = t + UnityEngine.Random.Range(2f, 5f);
        }

        if (!isBlinking && t >= nextBlinkTime)
        {
            isBlinking = true;
            blinkEndTime = t + 0.12;
        }
        else if (isBlinking && t >= blinkEndTime)
        {
            isBlinking = false;
            nextBlinkTime = t + UnityEngine.Random.Range(2f, 5f);
        }
    }

    private void DrawControls()
    {
        EditorGUILayout.BeginHorizontal();

        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = bridgeRunning ? RunningColor : StoppedColor;
        if (GUILayout.Button(bridgeRunning ? "■ Stop" : "▶ Start", GUILayout.Width(90), GUILayout.Height(24)))
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
        GUI.backgroundColor = previousBackground;

        GUILayout.Space(6);

        Rect dotRect = GUILayoutUtility.GetRect(14, 24, GUILayout.Width(14));
        GUI.color = bridgeRunning ? StoppedColor : new Color(0.6f, 0.6f, 0.6f);
        GUI.DrawTexture(new Rect(dotRect.x, dotRect.y + 7, 12, 12), circleTexture);
        GUI.color = Color.white;
        // The bridge is one shared connection for the whole editor, not per-session - Stop
        // disconnects every session's MCP tool calls, not just the one currently in view.
        // Surfaced via tooltip rather than a badge so it doesn't compete for space every frame.
        GUIContent bridgeLabel = new GUIContent(
            bridgeRunning ? "브릿지 연결됨" : "브릿지 중지됨",
            "모든 세션이 이 브릿지를 공유합니다. Stop을 누르면 다른 세션의 MCP 연결도 함께 끊깁니다.");
        GUILayout.Label(bridgeLabel, EditorStyles.miniLabel, GUILayout.Width(90));

        GUILayout.FlexibleSpace();

        // Fallback path for the chat input row's recurring visibility bugs: an independent
        // popup window that shares none of this window's layout/height calculations, so it
        // keeps working even if the inline field ever breaks again.
        if (GUILayout.Button("대체 입력창", EditorStyles.miniButton, GUILayout.Width(80)))
        {
            ClaudeCompanionSendDialog.Open(this);
        }

        EditorGUILayout.EndHorizontal();
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
        Repaint();
    }

    private async void StopSession()
    {
        // MCPServiceLocator.Bridge/the local HTTP server are singletons shared by every
        // session in this window, not per-session - stopping them here would silently cut
        // off any other session that's still mid-turn. Warn before doing that instead of
        // doing it quietly (see AddNewSession/DrawSessionSidebar for the multi-session model).
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
        Repaint();
    }

    // A same-frame EditorGUILayout.GetControlRect(GUILayout.Height(0)) call was tried here
    // first (both inside the chat scroll view and for the input row below it) to measure
    // "how wide is this pane right now", but it returned inconsistent values in each of those
    // two spots - bubbles came out far narrower than intended, while the input row came out
    // wider than the pane, spilling out past the sidebar/window edge. Computing the pane width
    // directly from position.width and the sidebar/divider's own fixed sizes removes that
    // per-frame ambiguity - it's the same number every time, in every context.
    private float GetChatPaneWidth()
    {
        return Mathf.Max(200f, position.width - SidebarWidth - SidebarDividerWidth);
    }

    private void DrawChat()
    {
        EditorGUILayout.LabelField("채팅", sectionHeaderStyle);

        // Whenever a message is added (sent or merely queued), jump the scroll view to the
        // bottom so the latest reply/queued bubble is visible without manual scrolling.
        int totalMessageCount = ActiveSession.ChatMessages.Count + ActiveSession.PendingMessages.Count;
        if (totalMessageCount != lastChatMessageCount)
        {
            lastChatMessageCount = totalMessageCount;
            chatScroll.y = float.MaxValue;
        }

        // Fixed-height regions below (input row, divider, activity log) are laid out normally;
        // this scroll view is the only expanding element in the window's vertical layout, so it
        // simply claims whatever space is left - no manual height math or cross-frame caching.
        chatScroll = EditorGUILayout.BeginScrollView(chatScroll, GUILayout.ExpandHeight(true));

        // Reserve room for this scroll view's own vertical scrollbar so bubble text isn't
        // wrapped to a width wider than what's actually left once the scrollbar is drawn.
        float chatAreaWidth = GetChatPaneWidth() - ChatScrollbarWidth;

        foreach (ChatMessage message in ActiveSession.ChatMessages)
        {
            DrawChatBubble(message, pending: false, chatAreaWidth);
        }

        // Messages typed while a turn was already in flight - CompanionSession.Submit queued
        // them instead of sending, and they'll fire automatically once the current turn ends.
        // Rendered dimmed so it's visually clear they haven't actually been sent to Claude yet.
        foreach (string pendingText in ActiveSession.PendingMessages)
        {
            DrawChatBubble(new ChatMessage("You", pendingText), pending: true, chatAreaWidth);
        }

        EditorGUILayout.EndScrollView();

        if (ActiveSession.PendingMessages.Count > 0)
        {
            EditorGUILayout.LabelField($"메시지 {ActiveSession.PendingMessages.Count}개 전송 대기 중", logPathStyle);
        }

        // Same deterministic width as the chat bubbles above (no scrollbar reservation
        // needed here - this row isn't inside a scroll view).
        float inputAreaWidth = GetChatPaneWidth();

        // Auto-growing input box (like Claude's/ChatGPT's chat UI): height tracks the
        // wrapped line count of what's currently typed instead of staying a fixed single
        // line, so a longer message is visible in place rather than requiring the window to
        // be resized to see what was typed.
        float desiredHeight = inputFieldStyle.CalcHeight(new GUIContent(inputText), inputAreaWidth) + 4f;
        float inputHeight = Mathf.Clamp(desiredHeight, MinInputHeight, MaxInputHeight);

        // Enter sends (matching the previous single-line behavior); Shift+Enter falls
        // through to the text area's own default handling so a multi-line message can still
        // be composed before sending. Must be checked and consumed *before* the TextArea
        // below runs - TextArea is a multi-line control that handles Return itself (inserting
        // a newline) as part of its own native event handling, which used the event up before
        // a check placed after the TextArea call ever saw it as still KeyDown. That was why
        // Enter silently stopped sending and only inserted a newline after this field switched
        // from TextField to TextArea.
        Event current = Event.current;
        bool enterPressed = current.type == EventType.KeyDown
            && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            && !current.shift
            && GUI.GetNameOfFocusedControl() == "ChatInput";
        if (enterPressed)
        {
            current.Use();
        }

        Rect inputRect = GUILayoutUtility.GetRect(inputAreaWidth, inputHeight);
        // A bare EditorGUI.TextArea's default skin barely contrasts against this window's
        // custom dark BackgroundColor - when empty (no text/selection highlight to draw the
        // eye), the field reads as "missing" even though it's rendering fine. Painting an
        // explicit background rect first (same approach as the chat bubbles) guarantees it
        // stays visible regardless of content.
        EditorGUI.DrawRect(inputRect, InputFieldColor);
        GUI.SetNextControlName("ChatInput");
        inputText = EditorGUI.TextArea(inputRect, inputText, inputFieldStyle);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        bool canSend = CanSendMessage(inputText);
        EditorGUI.BeginDisabledGroup(!canSend);
        bool sendPressed = GUILayout.Button("Send", GUILayout.Width(60));
        EditorGUI.EndDisabledGroup();

        // Only meaningful while this specific session is mid-turn - unlike the Stop button
        // in DrawControls, this cancels just the active session's process, not the shared
        // bridge, so other sessions keep running.
        EditorGUI.BeginDisabledGroup(ActiveSession == null || !ActiveSession.IsBusy);
        bool cancelPressed = GUILayout.Button("취소", GUILayout.Width(50));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        if ((sendPressed || enterPressed) && canSend)
        {
            SubmitMessage(inputText);
            inputText = "";
            GUI.FocusControl(null);
        }

        if (cancelPressed && ActiveSession != null && ActiveSession.IsBusy)
        {
            ActiveSession.CancelTurn();
        }
    }

    private void DrawChatBubble(ChatMessage message, bool pending, float chatAreaWidth)
    {
        bool isUser = message.Role == "You";

        // Every Begin*/End* pair below is try/finally-guarded so a rendering exception on
        // one message (e.g. from an unusually long/pathological body) can't leave GUILayout's
        // group stack unbalanced - an unmatched Begin desyncs it for the rest of this OnGUI
        // call, which previously showed up as the input row/activity log silently failing to
        // render at all rather than as a visible error.
        GUILayout.BeginHorizontal();
        try
        {
            if (isUser)
            {
                GUILayout.FlexibleSpace();
            }

            // 0.85 (not the ~0.7 typical of chat apps with avatars) because this pane is
            // already narrowed by the session sidebar - capping bubbles further on top of
            // that made ordinary one-sentence messages wrap into a tall, hard-to-read column.
            GUILayout.BeginVertical(GUILayout.MaxWidth(chatAreaWidth * 0.85f));
            try
            {
                Rect bubbleRect = EditorGUILayout.BeginVertical();
                try
                {
                    Color bubbleColor = isUser ? UserBubbleColor : ClaudeBubbleColor;
                    if (pending)
                    {
                        // Halved alpha is enough to read as "not sent yet" without a separate
                        // style/layout pass - same trick as the busy-state dot colors
                        // elsewhere in this file.
                        bubbleColor.a *= 0.5f;
                    }
                    EditorGUI.DrawRect(bubbleRect, bubbleColor);

                    GUILayout.Space(3);
                    EditorGUILayout.LabelField(pending ? message.Role + " · 전송 대기" : message.Role, bubbleRoleStyle);
                    DrawMessageBody(message.Text);
                    GUILayout.Space(3);
                }
                finally
                {
                    EditorGUILayout.EndVertical();
                }
            }
            finally
            {
                GUILayout.EndVertical();
            }

            if (!isUser)
            {
                GUILayout.FlexibleSpace();
            }
        }
        finally
        {
            GUILayout.EndHorizontal();
        }
        GUILayout.Space(4);
    }

    // Renders fenced code blocks in their own plain (non-rich-text) box and everything else
    // as rich text with bold/inline-code/bullets converted - see ChatMarkdown for what's
    // actually supported. Anything Claude sends that isn't one of those just prints as-is.
    private void DrawMessageBody(string text)
    {
        foreach (ChatMarkdown.Segment segment in ChatMarkdown.Split(text))
        {
            if (string.IsNullOrWhiteSpace(segment.Text))
            {
                continue;
            }

            if (segment.IsCode)
            {
                // try/finally: a LabelField on unusually long/pathological text has been
                // observed to throw inside Unity's own layout code (NullReferenceException
                // deep in EditorGUILayout.LabelField). Without the finally, the unmatched
                // BeginVertical desyncs GUILayout's group stack for the rest of this OnGUI
                // call, which is what was silently swallowing the Send button/activity log
                // below - not an actual absence of those controls.
                Rect codeRect = EditorGUILayout.BeginVertical();
                try
                {
                    EditorGUI.DrawRect(codeRect, CodeBlockColor);
                    GUILayout.Space(2);
                    EditorGUILayout.LabelField(segment.Text, codeBlockStyle);
                    GUILayout.Space(2);
                }
                finally
                {
                    EditorGUILayout.EndVertical();
                }
            }
            else
            {
                EditorGUILayout.LabelField(ChatMarkdown.ToRichText(segment.Text), bubbleTextStyle);
            }
        }
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

    private static void Divider()
    {
        GUILayout.Space(6);
        Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, DividerColor);
        GUILayout.Space(6);
    }

    private static void VerticalDivider()
    {
        GUILayout.Space(4);
        Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandHeight(true), GUILayout.Width(1));
        EditorGUI.DrawRect(rect, DividerColor);
        GUILayout.Space(4);
    }

    private static Texture2D CreateCircleTexture(int size)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01(radius - dist);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        return texture;
    }
}
