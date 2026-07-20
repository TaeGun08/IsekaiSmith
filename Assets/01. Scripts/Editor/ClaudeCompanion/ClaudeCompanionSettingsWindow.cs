using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Small standalone settings popup, same "independent utility window" pattern as
// ClaudeCompanionSendDialog - keeps the main controls row from accumulating one toggle button
// per preference as more settings show up (2026-07-16 request: sound needed to be choosable,
// and there should be one place for settings like it going forward, not scattered buttons).
public class ClaudeCompanionSettingsWindow : EditorWindow
{
    private ClaudeCompanionWindow owner;

    public static void Open(ClaudeCompanionWindow owner)
    {
        ClaudeCompanionSettingsWindow window = CreateInstance<ClaudeCompanionSettingsWindow>();
        window.owner = owner;
        window.titleContent = new GUIContent("설정");
        window.minSize = new Vector2(260, 150);
        window.maxSize = new Vector2(260, 150);
        window.ShowUtility();
    }

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.paddingLeft = 10;
        root.style.paddingRight = 10;
        root.style.paddingTop = 10;
        root.style.paddingBottom = 10;

        Label title = new Label("알림음");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 6;
        root.Add(title);

        Toggle soundToggle = new Toggle("턴 완료 시 알림음") { value = owner.SoundEnabled };
        root.Add(soundToggle);

        List<string> variantChoices = new List<string> { "기본음 (1회)", "강조음 (2회)" };
        DropdownField variantDropdown = new DropdownField("소리 종류", variantChoices, owner.SoundVariant);
        variantDropdown.style.marginTop = 4;
        variantDropdown.SetEnabled(owner.SoundEnabled);
        root.Add(variantDropdown);

        soundToggle.RegisterValueChangedCallback(evt =>
        {
            owner.SoundEnabled = evt.newValue;
            variantDropdown.SetEnabled(evt.newValue);
        });
        variantDropdown.RegisterValueChangedCallback(_ =>
        {
            owner.SoundVariant = variantDropdown.index;
        });

        Button testButton = new Button(() => owner.PlayNotificationSound()) { text = "테스트 재생" };
        testButton.style.marginTop = 12;
        root.Add(testButton);
    }
}
