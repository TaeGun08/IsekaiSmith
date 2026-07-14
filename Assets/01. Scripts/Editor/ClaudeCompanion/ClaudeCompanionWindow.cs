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
        window.minSize = new Vector2(420, 680);
    }

    private static readonly Color BackgroundColor = new Color(0.13f, 0.13f, 0.15f);
    private static readonly Color StageColor = new Color(0.17f, 0.18f, 0.20f);
    private static readonly Color DividerColor = new Color(1f, 1f, 1f, 0.08f);
    private static readonly Color UserBubbleColor = new Color(0.20f, 0.36f, 0.58f);
    private static readonly Color ClaudeBubbleColor = new Color(0.24f, 0.24f, 0.28f);
    private static readonly Color RunningColor = new Color(0.85f, 0.35f, 0.35f);
    private static readonly Color StoppedColor = new Color(0.35f, 0.75f, 0.45f);
    private static readonly Color IdleBodyColor = new Color(0.55f, 0.62f, 0.72f);
    private static readonly Color BusyBodyColorA = new Color(1f, 0.62f, 0.25f);
    private static readonly Color BusyBodyColorB = new Color(1f, 0.85f, 0.4f);

    private const string TokenBudgetPrefKey = "ClaudeCompanion.TokenBudget";

    private bool bridgeRunning;
    private bool autoProceed;
    private string inputText = "";
    private Vector2 chatScroll;
    private Vector2 logScroll;
    private TokenUsage tokenUsage;
    private int tokenBudget = 100000;

    private readonly List<ChatMessage> chatMessages = new List<ChatMessage>();
    private readonly List<string> activityLog = new List<string>();

    private ClaudeSessionRunner runner;
    private Texture2D circleTexture;

    private bool isBlinking;
    private double nextBlinkTime;
    private double blinkEndTime;

    private void OnEnable()
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            runner = new ClaudeSessionRunner(projectRoot);
            runner.OnSessionStarted += _ => Repaint();
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
            runner.OnUsageUpdated += usage =>
            {
                tokenUsage = usage;
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

        tokenBudget = EditorPrefs.GetInt(TokenBudgetPrefKey, 100000);

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
        try
        {
            runner?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    private void OnGUI()
    {
        EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), BackgroundColor);

        GUILayout.Space(4);
        DrawCharacterStage();
        GUILayout.Space(6);
        DrawControls();
        Divider();
        DrawChat();
        Divider();
        DrawActivityLog();

        DrawTokenUsageBadge();

        // Keep the character breathing/blinking even when idle.
        Repaint();
    }

    private void DrawTokenUsageBadge()
    {
        long consumed = tokenUsage.TotalTokens;
        long remaining = Math.Max(0L, (long)tokenBudget - consumed);
        float ratio = tokenBudget > 0 ? Mathf.Clamp01((float)consumed / tokenBudget) : 0f;

        const float width = 160f;
        Rect area = new Rect(position.width - width - 8, 6, width, 40);

        GUIStyle usageStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.82f, 0.86f, 0.94f) }
        };
        GUI.Label(new Rect(area.x, area.y, area.width, 14), $"토큰 {FormatTokenCount(consumed)} / {FormatTokenCount(tokenBudget)}", usageStyle);

        Rect barBackground = new Rect(area.x, area.y + 16, area.width, 6);
        EditorGUI.DrawRect(barBackground, new Color(1f, 1f, 1f, 0.12f));

        Rect barFill = new Rect(area.x, area.y + 16, area.width * ratio, 6);
        Color barColor = ratio < 0.7f
            ? new Color(0.4f, 0.85f, 0.5f)
            : ratio < 0.95f
                ? new Color(0.95f, 0.8f, 0.3f)
                : new Color(0.9f, 0.35f, 0.35f);
        EditorGUI.DrawRect(barFill, barColor);

        GUIStyle remainingStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.65f, 0.68f, 0.76f) }
        };
        GUI.Label(new Rect(area.x, area.y + 24, area.width, 14), $"남은 토큰(목표 대비) {FormatTokenCount(remaining)}", remainingStyle);
    }

    private static string FormatTokenCount(long count)
    {
        if (count >= 1_000_000)
        {
            return (count / 1_000_000f).ToString("0.#") + "M";
        }
        if (count >= 1_000)
        {
            return (count / 1_000f).ToString("0.#") + "K";
        }
        return count.ToString();
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

        GUIStyle stateLabel = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.85f, 0.85f, 0.9f) }
        };
        GUI.Label(new Rect(stage.x, stage.yMax - 16, stage.width, 16), busy ? "열심히 작업 중..." : "대기 중", stateLabel);
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

        GUILayout.Label(
            new GUIContent("목표", "채팅창 상단의 '남은 토큰' 표시 기준이 되는 목표 토큰 수입니다. 실제로 요청을 제한하지는 않고 표시용입니다."),
            EditorStyles.miniLabel, GUILayout.Width(26));
        int newBudget = EditorGUILayout.IntField(tokenBudget, GUILayout.Width(64));
        if (newBudget != tokenBudget && newBudget > 0)
        {
            tokenBudget = newBudget;
            EditorPrefs.SetInt(TokenBudgetPrefKey, tokenBudget);
        }

        GUILayout.Space(8);

        autoProceed = EditorGUILayout.ToggleLeft(
            new GUIContent(
                "자동진행",
                "켜면 --permission-mode bypassPermissions로 실행됩니다: 모든 도구 호출(Bash 포함)이 확인 없이 즉시 실행됩니다. 이 로컬 프로젝트 안에서만 사용하세요."),
            autoProceed, GUILayout.Width(70));

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

        runner.ResetSession();
        chatMessages.Clear();
        activityLog.Clear();
        tokenUsage = default;
        CompanionLog.RotateForNewSession();
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
        EditorGUILayout.LabelField("채팅", SectionHeaderStyle());
        chatScroll = EditorGUILayout.BeginScrollView(chatScroll, GUILayout.Height(220));

        foreach (ChatMessage message in chatMessages)
        {
            DrawChatBubble(message);
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        GUI.SetNextControlName("ChatInput");
        inputText = EditorGUILayout.TextField(inputText);

        bool enterPressed = false;
        Event current = Event.current;
        if (current.type == EventType.KeyDown
            && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter)
            && GUI.GetNameOfFocusedControl() == "ChatInput")
        {
            enterPressed = true;
            current.Use();
        }

        bool canSend = bridgeRunning && runner != null && !runner.IsBusy && !string.IsNullOrWhiteSpace(inputText);
        EditorGUI.BeginDisabledGroup(!canSend);
        bool sendPressed = GUILayout.Button("Send", GUILayout.Width(60));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        if ((sendPressed || enterPressed) && canSend)
        {
            SendCurrentMessage();
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
        EditorGUILayout.LabelField(message.Role, BubbleRoleStyle());
        EditorGUILayout.LabelField(message.Text, BubbleTextStyle());
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

    private void SendCurrentMessage()
    {
        chatMessages.Add(new ChatMessage("You", inputText));
        CompanionLog.AppendChat("You", inputText);
        runner.Send(inputText, autoProceed);
        inputText = "";
        GUI.FocusControl(null);
    }

    private void DrawActivityLog()
    {
        EditorGUILayout.LabelField("도구 활동 로그", SectionHeaderStyle());
        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(140));
        foreach (string entry in activityLog)
        {
            GUIStyle style = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = LogColor(entry) } };
            EditorGUILayout.LabelField(entry, style);
        }
        EditorGUILayout.EndScrollView();

        GUIStyle pathStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.55f, 0.55f, 0.6f) } };
        EditorGUILayout.LabelField($"로그 파일: {CompanionLog.FilePath}", pathStyle);
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

    private static GUIStyle SectionHeaderStyle()
    {
        return new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.9f, 0.9f, 0.95f) },
            margin = new RectOffset(2, 0, 0, 4)
        };
    }

    private static GUIStyle BubbleRoleStyle()
    {
        return new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = new Color(0.8f, 0.85f, 0.95f) },
            padding = new RectOffset(6, 6, 0, 0)
        };
    }

    private static GUIStyle BubbleTextStyle()
    {
        return new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            normal = { textColor = Color.white },
            padding = new RectOffset(6, 6, 0, 0)
        };
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
