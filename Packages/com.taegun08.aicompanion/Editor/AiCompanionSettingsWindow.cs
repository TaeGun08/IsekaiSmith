using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Small standalone settings popup, same "independent utility window" pattern as
// AiCompanionSendDialog - keeps the main controls row from accumulating one toggle button
// per preference as more settings show up (2026-07-16 request: sound needed to be choosable,
// and there should be one place for settings like it going forward, not scattered buttons).
public class AiCompanionSettingsWindow : EditorWindow
{
    private AiCompanionWindow owner;

    public static void Open(AiCompanionWindow owner)
    {
        AiCompanionSettingsWindow window = CreateInstance<AiCompanionSettingsWindow>();
        window.owner = owner;
        window.titleContent = new GUIContent("설정");
        window.minSize = new Vector2(260, 260);
        window.maxSize = new Vector2(260, 260);
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

        Label themeTitle = new Label("테마");
        themeTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        themeTitle.style.marginTop = 16;
        themeTitle.style.marginBottom = 6;
        root.Add(themeTitle);

        List<string> themeChoices = new List<string> { "다크", "라이트" };
        DropdownField themeDropdown = new DropdownField("색상", themeChoices, owner.Theme);
        root.Add(themeDropdown);
        themeDropdown.RegisterValueChangedCallback(_ => owner.Theme = themeDropdown.index);

        Label languageTitle = new Label("언어");
        languageTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        languageTitle.style.marginTop = 16;
        languageTitle.style.marginBottom = 6;
        root.Add(languageTitle);

        List<string> languageChoices = new List<string> { "한국어", "English" };
        DropdownField languageDropdown = new DropdownField("응답 언어", languageChoices, owner.Language);
        languageDropdown.tooltip = "메시지에서 특정 언어를 요청하지 않으면 이 언어로 응답합니다.";
        root.Add(languageDropdown);
        languageDropdown.RegisterValueChangedCallback(_ => owner.Language = languageDropdown.index);
    }
}
