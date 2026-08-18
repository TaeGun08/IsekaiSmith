#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Developer-only auto-play bot for regression/economy testing (dev_autoplay_design.html).
// Drives the same public entry points a real player uses (PlayerMotor input, StorageDepot's own
// proximity deposit, CraftingStation) instead of a separate simulated economy, so it exercises
// the real gameplay code paths. Compiled out of release builds entirely via the preprocessor
// guard around this whole file - never present outside the editor/dev builds.
public class DevAutoPlayController : MonoBehaviour
{
    private static DevAutoPlayController instance;

    public static DevAutoPlayController Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("DevAutoPlayController");
                instance = go.AddComponent<DevAutoPlayController>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        _ = Instance;
    }

    private static readonly float[] SpeedSteps = { 1f, 2f, 4f, 8f };
    private const float ArriveThreshold = 1.4f;
    private const float RescanCooldown = 0.5f;

    private enum Goal
    {
        None,
        Resource,
        Depot,
        Smithy
    }

    private Button toggleButton;
    private TMP_Text toggleLabel;
    private Button speedButton;
    private TMP_Text speedLabel;
    private Button merchantButton;
    private TMP_Text logText;

    private bool autoPlayEnabled;
    private int speedIndex;
    private float rescanTimer;

    private CarryStack cachedCarryStack;

    private Goal currentGoal = Goal.None;
    private Transform targetTransform;
    private OreNode targetOre;
    private WoodNode targetWood;
    private StorageDepot targetDepot;
    private CraftingStation targetSmithy;

    private readonly Dictionary<CraftGrade, int> gradeCounts = new Dictionary<CraftGrade, int>();
    private int totalCrafts;

    private void Awake()
    {
        BuildUI();
    }

    private void Update()
    {
        if (!autoPlayEnabled)
        {
            return;
        }

        RunAutoPlayTick();
    }

    private void BuildUI()
    {
        var canvasGO = new GameObject("DevAutoPlayCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(20f, -20f);
        panelRect.sizeDelta = new Vector2(300f, 410f);

        toggleButton = MakeButton(panel.transform, "ToggleButton", new Vector2(0f, 0f), new Vector2(280f, 64f), out toggleLabel);
        toggleButton.onClick.AddListener(ToggleAutoPlay);

        speedButton = MakeButton(panel.transform, "SpeedButton", new Vector2(0f, -74f), new Vector2(280f, 64f), out speedLabel);
        speedButton.onClick.AddListener(CycleSpeed);

        // Dev-mode panel - "암시장 상인을 임의로 부를 수 있는 개발자 모드... 오토 모드랑 배속도
        // 거기로 합쳐줬으면 해" (user request): this panel already housed AUTO PLAY/SPEED, so it's
        // the natural home for a manual merchant trigger too, instead of a fourth separate dev-only
        // control living somewhere else.
        merchantButton = MakeButton(panel.transform, "MerchantButton", new Vector2(0f, -148f), new Vector2(280f, 64f), out TMP_Text merchantLabel);
        merchantLabel.text = "SPAWN MERCHANT";
        merchantButton.GetComponent<Image>().color = new Color(0.35f, 0.25f, 0.4f); // matches the merchant's own shady-purple tint
        merchantButton.onClick.AddListener(() => BlackMarketMerchant.Instance.ForceBeginVisit());

        logText = MakeText(panel.transform, "Log", 14, new Vector2(0f, -222f), new Vector2(280f, 160f));
        logText.alignment = TextAlignmentOptions.TopLeft;

        RefreshToggleVisual();
        RefreshSpeedVisual();
        UpdateLog();
    }

    private TMP_Text MakeText(Transform parent, string name, int fontSize, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        return tmp;
    }

    private Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, out TMP_Text label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.4f, 0.38f, 0.34f);

        label = MakeText(go.transform, "Label", 18, Vector2.zero, size);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;

        return go.GetComponent<Button>();
    }

    private void ToggleAutoPlay()
    {
        autoPlayEnabled = !autoPlayEnabled;

        if (!autoPlayEnabled)
        {
            currentGoal = Goal.None;
            targetTransform = null;

            if (PlayerMotor.Instance != null)
            {
                PlayerMotor.Instance.SetKeyboardInput(Vector2.zero);
            }

            speedIndex = 0;
            Time.timeScale = 1f;
            RefreshSpeedVisual();
        }

        RefreshToggleVisual();
    }

    private void CycleSpeed()
    {
        if (!autoPlayEnabled)
        {
            return;
        }

        speedIndex = (speedIndex + 1) % SpeedSteps.Length;
        Time.timeScale = SpeedSteps[speedIndex];
        RefreshSpeedVisual();
    }

    private void RefreshToggleVisual()
    {
        toggleLabel.text = "AUTO PLAY: " + (autoPlayEnabled ? "ON" : "OFF");
        toggleButton.GetComponent<Image>().color = autoPlayEnabled ? new Color(0.35f, 0.6f, 0.35f) : new Color(0.4f, 0.38f, 0.34f);
    }

    private void RefreshSpeedVisual()
    {
        speedLabel.text = "SPEED: " + SpeedSteps[speedIndex] + "x";
    }

    private void RunAutoPlayTick()
    {
        if (PlayerMotor.Instance == null)
        {
            return;
        }

        if (cachedCarryStack == null)
        {
            cachedCarryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
            if (cachedCarryStack == null)
            {
                return;
            }
        }

        if (currentGoal != Goal.None && !IsGoalStillValid())
        {
            currentGoal = Goal.None;
        }

        if (currentGoal == Goal.None)
        {
            rescanTimer -= Time.deltaTime;
            if (rescanTimer > 0f)
            {
                PlayerMotor.Instance.SetKeyboardInput(Vector2.zero);
                return;
            }

            rescanTimer = RescanCooldown;
            ChooseGoal();
        }

        if (currentGoal == Goal.None || targetTransform == null)
        {
            PlayerMotor.Instance.SetKeyboardInput(Vector2.zero);
            return;
        }

        Vector3 toTarget = targetTransform.position - PlayerMotor.Instance.transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance > ArriveThreshold)
        {
            Vector2 direction = new Vector2(toTarget.x, toTarget.z).normalized;
            PlayerMotor.Instance.SetKeyboardInput(direction);
            return;
        }

        PlayerMotor.Instance.SetKeyboardInput(Vector2.zero);

        if (currentGoal == Goal.Smithy && targetSmithy != null)
        {
            if (targetSmithy.TryDevQuickCraft(out CraftGrade grade, out int amount))
            {
                RecordCraft(grade, amount);
            }

            currentGoal = Goal.None;
        }

        // Resource gathering and depot deposits are already handled automatically by
        // PlayerMining/PlayerWoodcutting/StorageDepot's own proximity checks once the bot is
        // standing in range - no extra interaction call needed for those two goals here.
    }

    private bool IsGoalStillValid()
    {
        switch (currentGoal)
        {
            case Goal.Resource:
                // Also bail out once the relevant layer fills up mid-gather - otherwise the bot
                // keeps standing at the node (auto-mining/chopping wastes every hit once
                // CarryStack.TryAdd starts silently failing) instead of noticing it's full and
                // heading to the depot.
                if (targetOre != null)
                {
                    return targetOre.IsAvailable && !cachedCarryStack.IsFull(CarryLayer.Ore);
                }

                if (targetWood != null)
                {
                    return targetWood.IsAvailable && !cachedCarryStack.IsFull(CarryLayer.Wood);
                }

                return false;

            case Goal.Depot:
                return targetDepot != null && cachedCarryStack.GetCount(targetDepot.AcceptedLayer) > 0;

            case Goal.Smithy:
                return targetSmithy != null;

            default:
                return false;
        }
    }

    private void ChooseGoal()
    {
        if (cachedCarryStack.IsFull(CarryLayer.Ore))
        {
            StorageDepot depot = FindNearest(FindObjectsByType<StorageDepot>(FindObjectsSortMode.None), d => d.AcceptedLayer == CarryLayer.Ore, d => d.transform.position);
            if (depot != null)
            {
                SetDepotGoal(depot);
                return;
            }
        }

        if (cachedCarryStack.IsFull(CarryLayer.Wood))
        {
            StorageDepot depot = FindNearest(FindObjectsByType<StorageDepot>(FindObjectsSortMode.None), d => d.AcceptedLayer == CarryLayer.Wood, d => d.transform.position);
            if (depot != null)
            {
                SetDepotGoal(depot);
                return;
            }
        }

        CraftingStation[] stations = FindObjectsByType<CraftingStation>(FindObjectsSortMode.None);
        CraftingStation smithy = FindNearest(stations, s => s.CanCraft, s => s.transform.position);
        if (smithy != null)
        {
            currentGoal = Goal.Smithy;
            targetSmithy = smithy;
            targetOre = null;
            targetWood = null;
            targetDepot = null;
            targetTransform = smithy.transform;
            return;
        }

        // Which type to gather is driven by what the recipe still needs, not by which node is
        // physically nearer - picking "just whichever is closer" can get stuck looping on one
        // resource type forever if its field sits closer to the player's usual path than the
        // other (e.g. always chopping wood because the lumber camp is nearer than the quarry).
        CraftingStation referenceStation = FindNearest(stations, s => true, s => s.transform.position);
        bool wantOre = referenceStation == null || referenceStation.NeedsOre;

        OreNode ore = FindNearest(FindObjectsByType<OreNode>(FindObjectsSortMode.None), n => n.IsAvailable, n => n.transform.position);
        WoodNode wood = FindNearest(FindObjectsByType<WoodNode>(FindObjectsSortMode.None), n => n.IsAvailable, n => n.transform.position);

        if (wantOre && ore != null)
        {
            currentGoal = Goal.Resource;
            targetOre = ore;
            targetWood = null;
            targetDepot = null;
            targetSmithy = null;
            targetTransform = ore.transform;
        }
        else if (wood != null)
        {
            currentGoal = Goal.Resource;
            targetWood = wood;
            targetOre = null;
            targetDepot = null;
            targetSmithy = null;
            targetTransform = wood.transform;
        }
        else if (ore != null)
        {
            // Wanted wood but none is available right now (mid-respawn) - mine ore instead of
            // idling, rather than waiting.
            currentGoal = Goal.Resource;
            targetOre = ore;
            targetWood = null;
            targetDepot = null;
            targetSmithy = null;
            targetTransform = ore.transform;
        }
        else
        {
            currentGoal = Goal.None;
            targetTransform = null;
        }
    }

    private void SetDepotGoal(StorageDepot depot)
    {
        currentGoal = Goal.Depot;
        targetDepot = depot;
        targetOre = null;
        targetWood = null;
        targetSmithy = null;
        targetTransform = depot.transform;
    }

    private static T FindNearest<T>(T[] candidates, Func<T, bool> filter, Func<T, Vector3> positionOf) where T : Component
    {
        if (PlayerMotor.Instance == null)
        {
            return null;
        }

        Vector3 origin = PlayerMotor.Instance.transform.position;
        T best = null;
        float bestSqrDist = float.MaxValue;

        for (int i = 0; i < candidates.Length; i++)
        {
            T candidate = candidates[i];
            if (candidate == null || !filter(candidate))
            {
                continue;
            }

            float sqrDist = (positionOf(candidate) - origin).sqrMagnitude;
            if (sqrDist < bestSqrDist)
            {
                bestSqrDist = sqrDist;
                best = candidate;
            }
        }

        return best;
    }

    private void RecordCraft(CraftGrade grade, int amount)
    {
        totalCrafts++;
        gradeCounts.TryGetValue(grade, out int count);
        gradeCounts[grade] = count + 1;

        Debug.Log("[AutoPlay] Craft #" + totalCrafts + " -> " + CraftGradeUtility.DisplayName(grade) + " x" + amount);
        UpdateLog();
    }

    private void UpdateLog()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Crafts: ").Append(totalCrafts).Append('\n');

        foreach (CraftGrade grade in (CraftGrade[])Enum.GetValues(typeof(CraftGrade)))
        {
            gradeCounts.TryGetValue(grade, out int count);
            sb.Append(CraftGradeUtility.DisplayName(grade)).Append(": ").Append(count).Append('\n');
        }

        logText.text = sb.ToString();
    }
}
#endif
