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
        GetWindow<ClaudeCompanionWindow>("Claude Companion");
    }

    private bool bridgeRunning;
    private bool autoProceed;
    private string inputText = "";
    private Vector2 chatScroll;
    private Vector2 logScroll;

    private readonly List<ChatMessage> chatMessages = new List<ChatMessage>();
    private readonly List<string> activityLog = new List<string>();

    private ClaudeSessionRunner runner;
    private Texture2D avatarTexture;

    private void OnEnable()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        runner = new ClaudeSessionRunner(projectRoot);
        runner.OnSessionStarted += _ => Repaint();
        runner.OnAssistantText += text =>
        {
            chatMessages.Add(new ChatMessage("Claude", text));
            Repaint();
        };
        runner.OnToolActivity += entry =>
        {
            activityLog.Add(entry);
            Repaint();
        };
        runner.OnTurnComplete += Repaint;
        runner.OnError += error =>
        {
            activityLog.Add("ERROR: " + error);
            Repaint();
        };

        bridgeRunning = MCPServiceLocator.Bridge.IsRunning;
        avatarTexture = CreateCircleTexture(64);
    }

    private void OnDisable()
    {
        runner?.Dispose();
    }

    private void OnGUI()
    {
        DrawAvatar();
        EditorGUILayout.Space();
        DrawControls();
        EditorGUILayout.Space();
        DrawChat();
        EditorGUILayout.Space();
        DrawActivityLog();

        if (runner != null && runner.IsBusy)
        {
            Repaint();
        }
    }

    private void DrawAvatar()
    {
        Rect rect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64));
        bool busy = runner != null && runner.IsBusy;

        GUI.color = busy
            ? Color.Lerp(new Color(0.3f, 0.7f, 1f), new Color(0.7f, 0.95f, 1f), Mathf.PingPong((float)EditorApplication.timeSinceStartup * 2f, 1f))
            : new Color(0.6f, 0.6f, 0.6f);
        GUI.DrawTexture(rect, avatarTexture, ScaleMode.ScaleToFit);
        GUI.color = Color.white;

        EditorGUILayout.LabelField(busy ? "working..." : "idle", EditorStyles.miniLabel);
    }

    private void DrawControls()
    {
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button(bridgeRunning ? "Stop" : "Start", GUILayout.Width(80)))
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

        autoProceed = EditorGUILayout.ToggleLeft(
            new GUIContent(
                "자동진행 (허락 없이 진행)",
                "켜면 --permission-mode bypassPermissions로 실행됩니다: 모든 도구 호출(Bash 포함)이 확인 없이 즉시 실행됩니다. 이 로컬 프로젝트 안에서만 사용하세요."),
            autoProceed, GUILayout.Width(220));

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField(bridgeRunning ? "MCP Bridge: Running" : "MCP Bridge: Stopped", EditorStyles.miniLabel);
    }

    private async void StartSession()
    {
        MCPServiceLocator.Server.StartLocalHttpServer();
        await MCPServiceLocator.Bridge.StartAsync();
        bridgeRunning = MCPServiceLocator.Bridge.IsRunning;

        runner.ResetSession();
        chatMessages.Clear();
        activityLog.Clear();
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
        EditorGUILayout.LabelField("Chat", EditorStyles.boldLabel);
        chatScroll = EditorGUILayout.BeginScrollView(chatScroll, GUILayout.Height(200));
        foreach (ChatMessage message in chatMessages)
        {
            EditorGUILayout.LabelField($"{message.Role}: {message.Text}", EditorStyles.wordWrappedLabel);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        inputText = EditorGUILayout.TextField(inputText);

        bool canSend = bridgeRunning && runner != null && !runner.IsBusy && !string.IsNullOrWhiteSpace(inputText);
        EditorGUI.BeginDisabledGroup(!canSend);
        bool sendPressed = GUILayout.Button("Send", GUILayout.Width(60));
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();

        if (sendPressed)
        {
            SendCurrentMessage();
        }
    }

    private void SendCurrentMessage()
    {
        chatMessages.Add(new ChatMessage("You", inputText));
        runner.Send(inputText, autoProceed);
        inputText = "";
        GUI.FocusControl(null);
    }

    private void DrawActivityLog()
    {
        EditorGUILayout.LabelField("Tool Activity Log", EditorStyles.boldLabel);
        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(150));
        foreach (string entry in activityLog)
        {
            EditorGUILayout.LabelField(entry, EditorStyles.wordWrappedMiniLabel);
        }
        EditorGUILayout.EndScrollView();
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
