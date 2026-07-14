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
        window.minSize = new Vector2(480, 760);
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

    private bool bridgeRunning;

    // Serialized so a domain reload (e.g. triggered by recompiling this very script)
    // doesn't silently reset it - without this, the on-screen chat looked continuous
    // (thanks to CompanionLog) while the actual Claude session quietly restarted underneath it.
    [SerializeField] private string restoredSessionId;

    private string inputText = "";
    private Vector2 chatScroll;
    private Vector2 logScroll;
    private int lastChatMessageCount = -1;

    // Hidden by default - the activity log went through several rounds of resize/measurement
    // logic that kept fighting with the chat area's layout (see git history). Instead of
    // continuing to patch that coupling, the log now only keeps a minimal collapsed trace by
    // default; expanding it uses a fixed height so it can never feed back into chat sizing.
    [SerializeField] private bool activityLogCollapsed = true;
    private const float ActivityLogHeight = 120f;

    private readonly List<ChatMessage> chatMessages = new List<ChatMessage>();
    private readonly List<string> activityLog = new List<string>();

    private ClaudeSessionRunner runner;
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
    private readonly Dictionary<Color, GUIStyle> logEntryStyleCache = new Dictionary<Color, GUIStyle>();

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
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            runner = new ClaudeSessionRunner(projectRoot);
            if (!string.IsNullOrEmpty(restoredSessionId))
            {
                runner.RestoreSession(restoredSessionId);
            }
            runner.OnSessionStarted += id =>
            {
                restoredSessionId = id;
                Repaint();
            };
            runner.OnAssistantText += text =>
            {
                chatMessages.Add(new ChatMessage("Claude", text));
                CompanionLog.AppendChat("Claude", text);
                Repaint();
            };
            runner.OnToolActivity += entry =>
            {
                activityLog.Add(entry);
                CompanionLog.AppendActivity(entry);
                Repaint();
            };
            runner.OnTurnComplete += Repaint;
            runner.OnError += error =>
            {
                string entry = "ERROR: " + error;
                activityLog.Add(entry);
                CompanionLog.AppendActivity(entry);
                Repaint();
            };
        }
        catch (Exception ex)
        {
            // Never let OnEnable throw: right after a domain reload, other packages'
            // static services may not be ready yet, and a throwing OnEnable can cause
            // Unity to drop this window instead of gracefully re-showing it.
            Debug.LogException(ex);
        }

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

        // Restore whatever was logged before the window last closed (crash, domain
        // reload, or a plain close) so history isn't silently lost.
        if (chatMessages.Count == 0 && activityLog.Count == 0)
        {
            try
            {
                chatMessages.AddRange(CompanionLog.LoadChatHistory());
                activityLog.AddRange(CompanionLog.LoadActivityHistory());
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
        try
        {
            runner?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
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

            GUILayout.Space(4);
            DrawCharacterStage();
            GUILayout.Space(6);
            DrawControls();
            Divider();
            DrawChat();
            Divider();
            DrawActivityLog();
        }
        catch (Exception ex)
        {
            // A single bad frame (e.g. a null runner right after a failed OnEnable)
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
            padding = new RectOffset(6, 6, 0, 0)
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

        // The default EditorStyles.textField skin barely contrasts against this window's
        // custom dark BackgroundColor when the field is empty - see DrawChat(). Nulling out
        // normal/focused.background here to force our own EditorGUI.DrawRect underneath was
        // tried first, but it made Unity's internal TextEditor throw a NullReferenceException
        // (in TextEditor.UpdateTextHandle/MoveCursorToPosition) as soon as the field was
        // clicked into - which, caught by OnGUI's outer try/catch, silently killed that
        // frame's rendering and looked exactly like the field "disappearing". Leaving the
        // native background alone and only tinting text color is the safe way to adjust this.
        inputFieldStyle = new GUIStyle(EditorStyles.textField)
        {
            normal = { textColor = Color.white },
            focused = { textColor = Color.white },
            padding = new RectOffset(6, 6, 2, 2)
        };
    }

    private GUIStyle GetLogEntryStyle(Color color)
    {
        if (!logEntryStyleCache.TryGetValue(color, out GUIStyle style))
        {
            style = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = color } };
            logEntryStyleCache[color] = style;
        }
        return style;
    }

    private void DrawCharacterStage()
    {
        Rect stage = GUILayoutUtility.GetRect(position.width, 96, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(stage, StageColor);

        bool busy = runner != null && runner.IsBusy;
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
        GUILayout.Label(bridgeRunning ? "브릿지 연결됨" : "브릿지 중지됨", EditorStyles.miniLabel, GUILayout.Width(90));

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
        if (string.IsNullOrEmpty(restoredSessionId))
        {
            runner.ResetSession();
            chatMessages.Clear();
            activityLog.Clear();
            CompanionLog.RotateForNewSession();
        }
        Repaint();
    }

    private async void StopSession()
    {
        runner.Kill();
        await MCPServiceLocator.Bridge.StopAsync();
        MCPServiceLocator.Server.StopLocalHttpServer();
        bridgeRunning = MCPServiceLocator.Bridge.IsRunning;
        Repaint();
    }

    private void DrawChat()
    {
        EditorGUILayout.LabelField("채팅", sectionHeaderStyle);

        // Whenever a message is added, jump the scroll view to the bottom so the latest
        // reply is visible without the user having to drag the scrollbar down every turn.
        if (chatMessages.Count != lastChatMessageCount)
        {
            lastChatMessageCount = chatMessages.Count;
            chatScroll.y = float.MaxValue;
        }

        // Fixed-height regions below (input row, divider, activity log) are laid out normally;
        // this scroll view is the only expanding element in the window's vertical layout, so it
        // simply claims whatever space is left - no manual height math or cross-frame caching.
        chatScroll = EditorGUILayout.BeginScrollView(chatScroll, GUILayout.ExpandHeight(true));

        foreach (ChatMessage message in chatMessages)
        {
            DrawChatBubble(message);
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();

        // A bare EditorGUILayout.TextField's default skin barely contrasts against this
        // window's custom dark BackgroundColor - when empty (no text/selection highlight
        // to draw the eye), the field reads as "missing" even though it's rendering fine.
        // Painting an explicit background rect first (same approach as the chat bubbles)
        // guarantees it stays visible regardless of content.
        Rect inputRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(inputRect, InputFieldColor);
        GUI.SetNextControlName("ChatInput");
        inputText = EditorGUI.TextField(inputRect, inputText, inputFieldStyle);

        bool enterPressed = false;
        Event current = Event.current;
        if (current.type == EventType.KeyDown
            && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            && GUI.GetNameOfFocusedControl() == "ChatInput")
        {
            enterPressed = true;
            current.Use();
        }

        bool canSend = CanSendMessage(inputText);
        EditorGUI.BeginDisabledGroup(!canSend);
        bool sendPressed = GUILayout.Button("Send", GUILayout.Width(60));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        if ((sendPressed || enterPressed) && canSend)
        {
            SubmitMessage(inputText);
            inputText = "";
            GUI.FocusControl(null);
        }
    }

    private void DrawChatBubble(ChatMessage message)
    {
        bool isUser = message.Role == "You";

        GUILayout.BeginHorizontal();
        if (isUser)
        {
            GUILayout.FlexibleSpace();
        }

        GUILayout.BeginVertical(GUILayout.MaxWidth(position.width * 0.72f));
        Rect bubbleRect = EditorGUILayout.BeginVertical();
        EditorGUI.DrawRect(bubbleRect, isUser ? UserBubbleColor : ClaudeBubbleColor);

        GUILayout.Space(3);
        EditorGUILayout.LabelField(message.Role, bubbleRoleStyle);
        EditorGUILayout.LabelField(message.Text, bubbleTextStyle);
        GUILayout.Space(3);

        EditorGUILayout.EndVertical();
        GUILayout.EndVertical();

        if (!isUser)
        {
            GUILayout.FlexibleSpace();
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4);
    }

    // Shared by both the inline chat row and ClaudeCompanionSendDialog (the fallback input
    // window) so a message can be sent identically regardless of which UI triggered it.
    private bool CanSendMessage(string text)
    {
        return bridgeRunning && runner != null && !runner.IsBusy && !string.IsNullOrWhiteSpace(text);
    }

    public void SubmitMessage(string text)
    {
        if (!CanSendMessage(text))
        {
            return;
        }
        chatMessages.Add(new ChatMessage("You", text));
        CompanionLog.AppendChat("You", text);
        runner.Send(text);
        Repaint();
    }

    private void DrawActivityLog()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("도구 활동 로그", sectionHeaderStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(activityLogCollapsed ? "펼치기 ▲" : "접기 ▼", EditorStyles.miniButton, GUILayout.Width(70)))
        {
            activityLogCollapsed = !activityLogCollapsed;
        }
        EditorGUILayout.EndHorizontal();

        if (activityLogCollapsed)
        {
            return;
        }

        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(ActivityLogHeight));
        foreach (string entry in activityLog)
        {
            EditorGUILayout.LabelField(entry, GetLogEntryStyle(LogColor(entry)));
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.LabelField($"로그 파일: {CompanionLog.FilePath}", logPathStyle);
    }

    private static Color LogColor(string entry)
    {
        if (entry.StartsWith("ERROR"))
        {
            return new Color(1f, 0.45f, 0.45f);
        }
        if (entry.StartsWith("tool_use"))
        {
            return new Color(0.55f, 0.8f, 1f);
        }
        if (entry.StartsWith("tool_result"))
        {
            return new Color(0.6f, 0.9f, 0.6f);
        }
        if (entry.StartsWith("system"))
        {
            return new Color(0.75f, 0.75f, 0.82f);
        }
        return new Color(0.7f, 0.7f, 0.7f);
    }

    private static void Divider()
    {
        GUILayout.Space(6);
        Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, DividerColor);
        GUILayout.Space(6);
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
