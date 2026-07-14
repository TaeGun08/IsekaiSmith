using UnityEditor;
using UnityEngine;

// Standalone fallback for sending a chat message when the inline input row in
// ClaudeCompanionWindow is unreadable/broken. Deliberately has its own minimal OnGUI with no
// shared layout math, height caching, or scroll views, so a bug in the main window's chat
// area can't take this down with it.
public class ClaudeCompanionSendDialog : EditorWindow
{
    private ClaudeCompanionWindow owner;
    private string text = "";
    private bool focusPending;

    public static void Open(ClaudeCompanionWindow owner)
    {
        ClaudeCompanionSendDialog window = CreateInstance<ClaudeCompanionSendDialog>();
        window.owner = owner;
        window.titleContent = new GUIContent("대체 입력창");
        window.minSize = new Vector2(360, 130);
        window.maxSize = new Vector2(360, 130);
        window.focusPending = true;
        window.ShowUtility();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("채팅 입력창이 보이지 않을 때 사용하는 대체 입력창입니다.", EditorStyles.wordWrappedLabel);
        GUILayout.Space(6);

        GUI.SetNextControlName("FallbackInput");
        text = EditorGUILayout.TextField(text);

        if (focusPending)
        {
            focusPending = false;
            EditorGUI.FocusTextInControl("FallbackInput");
        }

        GUILayout.FlexibleSpace();

        bool enterPressed = Event.current.type == EventType.KeyDown
            && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
        if (enterPressed)
        {
            Event.current.Use();
        }

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        bool cancelPressed = GUILayout.Button("취소", GUILayout.Width(70));
        bool sendPressed = GUILayout.Button("Send", GUILayout.Width(70));
        EditorGUILayout.EndHorizontal();

        if (cancelPressed)
        {
            Close();
        }
        else if (sendPressed || enterPressed)
        {
            if (owner != null)
            {
                owner.SubmitMessage(text);
            }
            Close();
        }
    }
}
