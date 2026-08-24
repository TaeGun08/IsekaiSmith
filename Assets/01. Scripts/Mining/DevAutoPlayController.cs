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
        Smithy,
        // Tutorial-only goals (사용자 요청 2026-08-24: "오토 플레이가 튜토리얼의 흐름을 자동으로
        // 플레이") - only ever chosen while GuidedTutorial is on the matching step, never part of
        // the post-tutorial endless gather/craft loop.
        Counter,
        Monster
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
    private OrderQueueManager targetCounter;
    private Monster targetMonster;
    // Snapshot of gold when a Counter goal starts - lets IsGoalStillValid detect "the sale actually
    // went through" the same way GuidedTutorial's own SellWeapon step does (SalesCurrency.Gold >
    // lastGold), rather than assuming one TryFulfill() tap was enough.
    private int goalStartGold;

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
        // Was 20 (above InteractionPromptUI/icons/StageEncounterUI/CraftingSilhouetteUI at 5-10,
        // which this tall 780px panel could cover - user report: "개발자 버튼이 다른 UI를 가려서
        // 플레이하기가 불편해"). -100 broke it a different way: FloatingJoystick's JoystickInputZone
        // is a *full-screen* raycast catcher at order 0 (tap-anywhere-to-place), so anything below
        // 0 loses every click to it, everywhere on screen (user report: "개발자 모드의 버튼들이
        // 안눌리고 이동 조이스틱만 활성화 돼"). 3 sits above that (clickable) and below the real
        // gameplay UI (5+, no longer covers it).
        canvas.sortingOrder = 3;
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
        panelRect.sizeDelta = new Vector2(300f, 780f);

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

        // "개발자 모드에 다양한 등급의 무기를 얻을 수 있게... 골드도 얻을 수 있도록" (user request) -
        // unblocks testing the black market's BUY (needs gold)/SELL (needs raw ore/mana/wood
        // stock) rows and the equipment screen (needs varied weapons) without a long farming grind.
        Button grantGoldButton = MakeButton(panel.transform, "GrantGoldButton", new Vector2(0f, -222f), new Vector2(280f, 64f), out TMP_Text grantGoldLabel);
        grantGoldLabel.text = "GRANT 1000 GOLD";
        grantGoldButton.GetComponent<Image>().color = new Color(0.55f, 0.45f, 0.15f);
        grantGoldButton.onClick.AddListener(() => SalesCurrency.Add(1000));

        Button grantMaterialsButton = MakeButton(panel.transform, "GrantMaterialsButton", new Vector2(0f, -296f), new Vector2(280f, 64f), out TMP_Text grantMaterialsLabel);
        grantMaterialsLabel.text = "GRANT MATERIALS";
        grantMaterialsButton.GetComponent<Image>().color = new Color(0.3f, 0.45f, 0.35f);
        grantMaterialsButton.onClick.AddListener(GrantMaterials);

        Button grantWeaponsButton = MakeButton(panel.transform, "GrantWeaponsButton", new Vector2(0f, -370f), new Vector2(280f, 64f), out TMP_Text grantWeaponsLabel);
        grantWeaponsLabel.text = "GRANT WEAPONS (ALL GRADES)";
        grantWeaponsButton.GetComponent<Image>().color = new Color(0.45f, 0.35f, 0.2f);
        grantWeaponsButton.onClick.AddListener(GrantWeapons);

        // "UNLOCK ALL STAGES" below only touches StageBank - the Equipment/Stages/BlackMarket
        // *icons* are gated separately by GuidedTutorial (user report: "UNLOCK ALL STAGES를 눌러도
        // 스테이지 버튼이 안 보임" pattern), so this is needed too before those buttons show up at all.
        Button skipTutorialButton = MakeButton(panel.transform, "SkipTutorialButton", new Vector2(0f, -444f), new Vector2(280f, 64f), out TMP_Text skipTutorialLabel);
        skipTutorialLabel.text = "SKIP TUTORIAL";
        skipTutorialButton.GetComponent<Image>().color = new Color(0.35f, 0.35f, 0.5f);
        skipTutorialButton.onClick.AddListener(GuidedTutorial.SkipToComplete);

        // Clears all 3 stages instantly - the dungeon itself only needs Stage 1
        // (DungeonBank.IsUnlocked), but this button still clears all 3 so Stage 2/3 content is
        // also reachable for testing without actually playing through each wave fight first.
        Button unlockStagesButton = MakeButton(panel.transform, "UnlockStagesButton", new Vector2(0f, -518f), new Vector2(280f, 64f), out TMP_Text unlockStagesLabel);
        unlockStagesLabel.text = "UNLOCK ALL STAGES";
        unlockStagesButton.GetComponent<Image>().color = new Color(0.2f, 0.4f, 0.45f);
        unlockStagesButton.onClick.AddListener(() =>
        {
            for (int i = 1; i <= StageBank.StageCount; i++)
            {
                StageBank.MarkCleared(i);
            }
        });

        logText = MakeText(panel.transform, "Log", 14, new Vector2(0f, -592f), new Vector2(280f, 160f));
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

    // 20 units of every Ore/Mana grade (AddDirect - bypasses the Ceiling roll, same as the black
    // market's own purchases) plus a chunk of wood - covers the black market's SELL thresholds and
    // most crafting recipes in one tap.
    private const int GrantMaterialAmount = 20;
    private const int GrantWoodAmount = 100;

    private void GrantMaterials()
    {
        foreach (OreGrade grade in (OreGrade[])System.Enum.GetValues(typeof(OreGrade)))
        {
            OreBank.AddDirect(grade, GrantMaterialAmount);
        }

        foreach (ManaGrade grade in (ManaGrade[])System.Enum.GetValues(typeof(ManaGrade)))
        {
            ManaBank.AddDirect(grade, GrantMaterialAmount);
        }

        ResourceBank.Add(ResourceType.Wood, GrantWoodAmount);
    }

    // One Fine-quality, no-enchant copy of every (WeaponType, OreGrade) combination - a full
    // spread for testing the equipment screen's equip/delete flow and the black market's SELL
    // rows without a real crafting session.
    private void GrantWeapons()
    {
        foreach (WeaponType weapon in (WeaponType[])System.Enum.GetValues(typeof(WeaponType)))
        {
            foreach (OreGrade ore in (OreGrade[])System.Enum.GetValues(typeof(OreGrade)))
            {
                ToolInventory.Add(weapon, ore, CraftGrade.Fine, ManaElement.None, ManaGrade.Crude, 1);
            }
        }
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
            // During Step.PreciseCraft the tutorial needs a ToolInventory-bound (equippable) craft,
            // not another counter-stock QUICK CRAFT - same recipe/inputs either way, only which
            // dev-bypass method gets called differs.
            bool wantPrecise = !GuidedTutorial.HasCompletedTutorial && GuidedTutorial.CurrentStep == GuidedTutorial.Step.PreciseCraft;
            bool crafted;
            CraftGrade grade;
            int amount;

            if (wantPrecise)
            {
                crafted = targetSmithy.TryDevPreciseCraft(WeaponType.Sword, out grade, out amount);
            }
            else
            {
                crafted = targetSmithy.TryDevQuickCraft(out grade, out amount);
            }

            if (crafted)
            {
                RecordCraft(grade, amount);
            }

            currentGoal = Goal.None;
        }
        else if (currentGoal == Goal.Counter && targetCounter != null)
        {
            // Not one-shot like Smithy - a tap can silently do nothing yet (no customer arrived,
            // no stock deposited, still on cooldown), so keep retrying every tick standing here.
            // IsGoalStillValid() is what actually notices the sale went through and ends this goal.
            targetCounter.TryFulfill();
        }

        // Resource gathering and depot deposits are already handled automatically by
        // PlayerMining/PlayerWoodcutting/StorageDepot's own proximity checks once the bot is
        // standing in range - no extra interaction call needed for those two goals here. Goal.
        // Monster needs nothing either - PlayerCombat auto-attacks anything in its own range every
        // frame regardless of this controller; just standing close enough is sufficient.
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
                return targetDepot != null && CarriedAnyAcceptedLayer(targetDepot);

            case Goal.Smithy:
                return targetSmithy != null;

            case Goal.Counter:
                // Stops being valid the instant the tutorial's own SellWeapon check would also
                // fire (gold increased) - or if something moved the tutorial past this step some
                // other way (e.g. SKIP TUTORIAL was pressed mid-goal).
                return targetCounter != null
                    && !GuidedTutorial.HasCompletedTutorial
                    && GuidedTutorial.CurrentStep == GuidedTutorial.Step.SellWeapon
                    && SalesCurrency.Gold <= goalStartGold;

            case Goal.Monster:
                return targetMonster != null && targetMonster.IsAvailable && !cachedCarryStack.IsFull(CarryLayer.ManaStone) && cachedCarryStack.GetCount(CarryLayer.ManaStone) < 1;

            default:
                return false;
        }
    }

    // Any accepted layer being full is reason enough to head to the (now singular) storage box -
    // it takes Wood/Ore/ManaStone all through the same object, so there's no more "which depot"
    // branching to do per resource.
    private bool CarriedAnyAcceptedLayer(StorageDepot depot)
    {
        CarryLayer[] layers = depot.AcceptedLayers;
        for (int i = 0; i < layers.Length; i++)
        {
            if (cachedCarryStack.GetCount(layers[i]) > 0)
            {
                return true;
            }
        }

        return false;
    }

    // Tutorial-driving (사용자 요청 2026-08-24): while GuidedTutorial hasn't finished yet, its
    // current step takes priority over the generic gather/deposit/craft loop below for whichever
    // steps need a different action entirely (selling, hunting, equipping, clearing a stage/
    // dungeon floor). Steps that the generic loop already handles correctly as-is (Move, the four
    // Gather/Carry steps, QuickCraft, PreciseCraft) fall straight through with no special case.
    private void ChooseGoal()
    {
        GuidedTutorial.Step tutorialStep = GuidedTutorial.HasCompletedTutorial ? GuidedTutorial.Step.Done : GuidedTutorial.CurrentStep;
        UpdateLog();

        switch (tutorialStep)
        {
            case GuidedTutorial.Step.Welcome:
                // Not a Goal - resolved instantly, no walking involved. Harmless to call every
                // rescan tick: SkipWelcomeCard() itself no-ops once step has already moved on.
                GuidedTutorial.Instance.SkipWelcomeCard();
                break;

            case GuidedTutorial.Step.SellWeapon:
                if (TrySetCounterGoal())
                {
                    return;
                }

                break;

            case GuidedTutorial.Step.HuntMonster:
                if (TrySetMonsterGoal())
                {
                    return;
                }

                break;

            case GuidedTutorial.Step.Equip:
                TryAutoEquip();
                break;

            case GuidedTutorial.Step.StageProgress:
                // Same dev bypass the "UNLOCK ALL STAGES" button already uses - actually fighting
                // through a wave encounter is out of scope for this bot (no combat AI here beyond
                // the passive auto-attack PlayerCombat already does on anything in melee range).
                StageBank.MarkCleared(1);
                break;

            case GuidedTutorial.Step.DungeonProgress:
                DungeonBank.ReportFloorCleared(1);
                break;
        }

        if (cachedCarryStack.IsFull(CarryLayer.Ore) || cachedCarryStack.IsFull(CarryLayer.Wood))
        {
            StorageDepot depot = FindNearest(FindObjectsByType<StorageDepot>(FindObjectsSortMode.None), d => true, d => d.transform.position);
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
            targetCounter = null;
            targetMonster = null;
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
            targetCounter = null;
            targetMonster = null;
            targetTransform = ore.transform;
        }
        else if (wood != null)
        {
            currentGoal = Goal.Resource;
            targetWood = wood;
            targetOre = null;
            targetDepot = null;
            targetSmithy = null;
            targetCounter = null;
            targetMonster = null;
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
            targetCounter = null;
            targetMonster = null;
            targetTransform = ore.transform;
        }
        else
        {
            currentGoal = Goal.None;
            targetTransform = null;
        }
    }

    // Weapon on the player's back (rack pickup already happened) but nowhere to sell it yet -
    // wait rather than wander off; returns false so ChooseGoal falls through to the generic loop
    // (which, mid-SellWeapon, has nothing better to do either, so it'll just idle safely).
    private bool TrySetCounterGoal()
    {
        if (cachedCarryStack.GetCount(CarryLayer.Weapon) <= 0)
        {
            return false;
        }

        OrderQueueManager counter = FindNearest(FindObjectsByType<OrderQueueManager>(FindObjectsSortMode.None), c => true, c => c.transform.position);
        if (counter == null)
        {
            return false;
        }

        currentGoal = Goal.Counter;
        targetCounter = counter;
        targetOre = null;
        targetWood = null;
        targetDepot = null;
        targetSmithy = null;
        targetMonster = null;
        targetTransform = counter.transform;
        goalStartGold = SalesCurrency.Gold;
        return true;
    }

    private bool TrySetMonsterGoal()
    {
        if (cachedCarryStack.GetCount(CarryLayer.ManaStone) >= 1)
        {
            return false; // already have one - GuidedTutorial's own Update() will advance on its own
        }

        Monster monster = FindNearest(FindObjectsByType<Monster>(FindObjectsSortMode.None), m => m.IsAvailable, m => m.transform.position);
        if (monster == null)
        {
            return false;
        }

        currentGoal = Goal.Monster;
        targetMonster = monster;
        targetOre = null;
        targetWood = null;
        targetDepot = null;
        targetSmithy = null;
        targetCounter = null;
        targetTransform = monster.transform;
        return true;
    }

    private void TryAutoEquip()
    {
        // TryGetBestWeapon returning false means nothing's actually owned yet - must not call
        // Equip() with its Sword/Iron/None/Crude fallback values in that case, or
        // EquippedWeapon.HasExplicitChoice would flip true and fast-forward the tutorial past a
        // precise craft that hasn't actually happened.
        if (ToolInventory.TryGetBestWeapon(out WeaponType weapon, out OreGrade oreGrade, out ManaElement element, out ManaGrade manaGrade))
        {
            EquippedWeapon.Equip(weapon, oreGrade, element, manaGrade);
        }
    }

    private void SetDepotGoal(StorageDepot depot)
    {
        currentGoal = Goal.Depot;
        targetDepot = depot;
        targetOre = null;
        targetWood = null;
        targetSmithy = null;
        targetCounter = null;
        targetMonster = null;
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
        sb.Append("Tutorial: ").Append(GuidedTutorial.HasCompletedTutorial ? "Done" : GuidedTutorial.CurrentStep.ToString()).Append('\n');
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
