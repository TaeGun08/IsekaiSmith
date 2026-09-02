using System;
using System.Collections.Generic;
// touch
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;

// Guided first-run checklist for what AI Companion needs on a machine it's new to: the optional
// Unity MCP package (for editor tool-calls) and each AI provider's CLI (for chat itself). Exists
// because this ships as a drop-in local UPM package now (2026-09-03 asset-packaging pass) - a
// project that just received the package has none of the "already set this up once" context
// IsekaiSmith's own dev machine has, so what's missing needs to surface as a guided, one-click
// fix up front instead of only reactively (see AiCompanionWindow.OfferInstall, which still
// exists as the safety net for later - a session picking a provider whose CLI got uninstalled
// since setup ran).
public class AiCompanionSetupWindow : EditorWindow
{
    private const string McpPackageUrl = "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main";
    private const string NodeJsGuideUrl = "https://nodejs.org/";
    private const string CursorGuideUrl = "https://cursor.com";
    private const string MarkerFileName = "setup-wizard-shown.marker";

    // Same "Library/ClaudeCompanion" folder CompanionLog already writes chat logs to (kept under
    // that legacy name for the same reason CompanionLog does - not worth relocating on-disk data
    // just for a rebrand) - one shared folder instead of a second one only this file uses.
    private static string MarkerFilePath =>
        Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Library", "ClaudeCompanion", MarkerFileName);

    private readonly HashSet<string> installingPackages = new HashSet<string>();
    private VisualElement checklistRoot;
    private AddRequest mcpAddRequest;

    [MenuItem("Window/AI Companion Setup Wizard")]
    public static void Open()
    {
        AiCompanionSetupWindow window = GetWindow<AiCompanionSetupWindow>();
        window.titleContent = new GUIContent("AI Companion 셋업");
        window.minSize = new Vector2(420, 320);
        window.Show();
    }

    // Runs once per Editor process (not per window open) - pops the wizard up unprompted the
    // very first time this project ever loads the package, then never again, via a marker file
    // on disk (EditorPrefs would be wrong here - it's keyed per-machine, not per-project, so it
    // would wrongly suppress the prompt in every other project on the same computer).
    [InitializeOnLoadMethod]
    private static void AutoPromptOnFirstLoad()
    {
        EditorApplication.delayCall += () =>
        {
            try
            {
                if (File.Exists(MarkerFilePath))
                {
                    return;
                }
                string dir = Path.GetDirectoryName(MarkerFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(MarkerFilePath, DateTime.Now.ToString("O"));
                Open();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        };
    }

    private void CreateGUI()
    {
        VisualElement root = rootVisualElement;
        root.style.paddingLeft = 12;
        root.style.paddingRight = 12;
        root.style.paddingTop = 12;
        root.style.paddingBottom = 12;

        Label title = new Label("AI Companion 셋업");
        title.style.fontSize = 16;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.marginBottom = 4;
        root.Add(title);

        Label intro = new Label(
            "채팅에 필요한 항목을 점검합니다. Unity MCP는 선택 사항입니다 - 없어도 채팅은 정상 동작하고, " +
            "설치하면 AI가 에디터를 직접 조작하는 툴콜을 쓸 수 있습니다.");
        intro.style.whiteSpace = WhiteSpace.Normal;
        intro.style.marginBottom = 10;
        intro.style.color = new Color(0.6f, 0.6f, 0.6f);
        root.Add(intro);

        checklistRoot = new VisualElement();
        root.Add(checklistRoot);

        Button refreshButton = new Button(RefreshChecklist) { text = "새로고침" };
        refreshButton.style.marginTop = 10;
        refreshButton.style.alignSelf = Align.FlexStart;
        root.Add(refreshButton);

        RefreshChecklist();
    }

    private void RefreshChecklist()
    {
        if (checklistRoot == null)
        {
            return;
        }
        checklistRoot.Clear();
        checklistRoot.Add(BuildMcpRow());
        checklistRoot.Add(BuildNpmRow());
        foreach (AiProviderDefinition provider in AiProviderRegistry.All)
        {
            // NotImplementedSessionRunner-backed providers (no IsInstalled wired up) have
            // nothing an install button could verify - nothing to check here either.
            if (provider.IsInstalled != null)
            {
                checklistRoot.Add(BuildProviderRow(provider));
            }
        }
    }

    private VisualElement BuildMcpRow()
    {
        bool ready = UnityMcpBridgeAccessor.IsAvailable;
        bool installing = mcpAddRequest != null && !mcpAddRequest.IsCompleted;
        if (ready)
        {
            return MakeRow("Unity MCP 패키지 (선택)", true, null, null);
        }
        return MakeRow("Unity MCP 패키지 (선택)", false, installing ? "설치 중..." : "패키지 추가", installing ? null : (Action)StartMcpInstall);
    }

    private void StartMcpInstall()
    {
        if (mcpAddRequest != null && !mcpAddRequest.IsCompleted)
        {
            return;
        }
        Debug.Log("[AiCompanion] Unity MCP 패키지 설치를 시작합니다: " + McpPackageUrl);
        mcpAddRequest = Client.Add(McpPackageUrl);
        EditorApplication.update += PollMcpInstall;
        RefreshChecklist();
    }

    private void PollMcpInstall()
    {
        if (mcpAddRequest == null || !mcpAddRequest.IsCompleted)
        {
            return;
        }
        EditorApplication.update -= PollMcpInstall;
        Debug.Log(mcpAddRequest.Status == StatusCode.Failure
            ? "[AiCompanion] Unity MCP 패키지 설치 실패: " + mcpAddRequest.Error.message
            : "[AiCompanion] Unity MCP 패키지 설치 완료. 컴파일/도메인 리로드 후 반영됩니다.");
        if (this != null)
        {
            RefreshChecklist();
        }
    }

    private static VisualElement BuildNpmRow()
    {
        bool ready = CliInstaller.FindExecutable("npm") != null;
        return MakeRow("Node.js / npm", ready, ready ? null : "설치 안내 열기",
            ready ? null : (Action)(() => Application.OpenURL(NodeJsGuideUrl)));
    }

    private VisualElement BuildProviderRow(AiProviderDefinition provider)
    {
        string label = provider.DisplayName + " CLI";
        bool ready = provider.IsInstalled();
        if (ready)
        {
            return MakeRow(label, true, null, null);
        }
        if (installingPackages.Contains(provider.DisplayName))
        {
            return MakeRow(label, false, "설치 중...", null);
        }
        // Providers without an npm package (e.g. Cursor, whose official install is a curl|bash
        // script) can't be auto-installed - point at the guide instead of a button that would
        // run a broken "npm install -g " command (same reasoning as AiCompanionWindow.OfferInstall).
        if (string.IsNullOrEmpty(provider.InstallPackage))
        {
            return MakeRow(label, false, "설치 안내 열기", () => Application.OpenURL(CursorGuideUrl));
        }
        return MakeRow(label, false, "설치", () => InstallProviderCli(provider));
    }

    private void InstallProviderCli(AiProviderDefinition provider)
    {
        installingPackages.Add(provider.DisplayName);
        RefreshChecklist();

        Debug.Log($"[AiCompanion] {provider.DisplayName} CLI 설치를 시작합니다: npm install -g {provider.InstallPackage}");
        CliInstaller.InstallNpmPackageAsync(provider.InstallPackage, success =>
        {
            installingPackages.Remove(provider.DisplayName);
            if (success)
            {
                provider.ClearResolvedPathCache?.Invoke();
            }
            Debug.Log(success
                ? $"[AiCompanion] {provider.DisplayName} CLI 설치 완료."
                : $"[AiCompanion] {provider.DisplayName} CLI 설치 실패. Unity 콘솔 로그를 확인하세요.");
            if (this != null)
            {
                RefreshChecklist();
            }
        });
    }

    private static VisualElement MakeRow(string label, bool ready, string actionText, Action action)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.justifyContent = Justify.SpaceBetween;
        row.style.marginBottom = 6;
        row.style.paddingLeft = 8;
        row.style.paddingRight = 8;
        row.style.paddingTop = 6;
        row.style.paddingBottom = 6;
        row.style.backgroundColor = new Color(0f, 0f, 0f, 0.08f);
        row.style.borderTopLeftRadius = 6;
        row.style.borderTopRightRadius = 6;
        row.style.borderBottomLeftRadius = 6;
        row.style.borderBottomRightRadius = 6;

        Label statusLabel = new Label((ready ? "✅  " : "❌  ") + label);
        row.Add(statusLabel);

        if (!ready)
        {
            if (action != null)
            {
                row.Add(new Button(action) { text = actionText });
            }
            else if (!string.IsNullOrEmpty(actionText))
            {
                Label pending = new Label(actionText);
                pending.style.color = new Color(0.6f, 0.6f, 0.6f);
                row.Add(pending);
            }
        }
        return row;
    }
}
