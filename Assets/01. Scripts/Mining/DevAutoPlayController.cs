#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
        Rack,
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
    private WeaponRack targetRack;
    // Snapshot of gold when a Counter goal starts - lets IsGoalStillValid detect "the sale actually
    // went through" the same way GuidedTutorial's own SellWeapon step does (SalesCurrency.Gold >
    // lastGold), rather than assuming one TryFulfill() tap was enough.
    private int goalStartGold;
    // How much of the current Resource layer to carry before heading to deposit - null means "the
    // ordinary rule" (fill to CarryStack capacity, e.g. the fixed GatherWood1/GatherOre1 steps or
    // free-farming). Set to a specific amount by TryResupplyForSellWeapon so a multi-unit customer
    // order gets gathered in one batched trip instead of one recipe's worth at a time.
    private int? resourceCarryTarget;
    // Set while DrivePreciseCraftRoutine is actually playing the silhouette/minigames - pauses the
    // rest of this controller's goal logic (movement, gathering, etc.) so it doesn't fight with a
    // multi-second coroutine that's driving its own UI interactions.
    private bool isDrivingPreciseCraft;

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

        // Both this bot and PlayerKeyboardInput unconditionally write to the same
        // PlayerMotor.SetKeyboardInput slot every frame, so whichever one runs last in a given
        // frame silently wins - with the bot on, real WASD presses were getting overwritten right
        // back to whatever the bot wanted (사용자 요청 2026-08-24: "WASD 조작이 안돼"). Yielding
        // here whenever a real key is actually held means the player can always manually cut in;
        // goal/gathering logic itself just pauses for that frame and resumes cleanly once they
        // let go, rather than the two fighting over the same input every tick.
        if (RealKeyboardInputHeld())
        {
            return;
        }

        if (isDrivingPreciseCraft)
        {
            return; // DrivePreciseCraftRoutine has the wheel until it clears this flag
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
            // During Step.PreciseCraft the tutorial needs a ToolInventory-bound (equippable) craft
            // - actually played through the real silhouette/minigame UI now (사용자 요청
            // 2026-08-24), which takes several seconds across many frames, so it hands off to its
            // own coroutine instead of resolving inline like QUICK CRAFT does.
            bool wantPrecise = !GuidedTutorial.HasCompletedTutorial && GuidedTutorial.CurrentStep == GuidedTutorial.Step.PreciseCraft;

            if (wantPrecise)
            {
                if (targetSmithy.DevBeginPreciseCraft())
                {
                    isDrivingPreciseCraft = true;
                    StartCoroutine(DrivePreciseCraftRoutine(targetSmithy));
                }

                currentGoal = Goal.None;
            }
            else if (targetSmithy.TryDevQuickCraft(out CraftGrade grade, out int amount))
            {
                RecordCraft(grade, amount);

                // QUICK CRAFT output lands on the rack, not directly on the player - walk over and
                // wait for it to transfer instead of assuming wherever the craft happened is
                // already close enough (사용자 요청 2026-08-24: the rack is a genuinely separate
                // area, not merged with the furnace's own interact zone).
                if (targetSmithy.WeaponRack != null)
                {
                    currentGoal = Goal.Rack;
                    targetRack = targetSmithy.WeaponRack;
                    targetOre = null;
                    targetWood = null;
                    targetDepot = null;
                    targetCounter = null;
                    targetMonster = null;
                    targetTransform = targetRack.transform;
                }
                else
                {
                    currentGoal = Goal.None;
                }
            }
            else
            {
                currentGoal = Goal.None;
            }
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
        // frame regardless of this controller; just standing close enough is sufficient. Goal.Rack
        // is the same - WeaponRack's own Update() pulls from it once the bot is in range.
    }

    private bool IsGoalStillValid()
    {
        switch (currentGoal)
        {
            case Goal.Resource:
                // Also bail out once the relevant layer reaches its target - otherwise the bot
                // keeps standing at the node (auto-mining/chopping wastes every hit once
                // CarryStack.TryAdd starts silently failing) instead of noticing it's done and
                // heading to the depot. The target is either resourceCarryTarget (a specific batch
                // amount - see TryResupplyForSellWeapon) or, absent that, plain CarryStack
                // capacity. During the tutorial's fixed wood-then-ore sequence, also bail out the
                // instant that step is no longer the matching Gather* step (e.g. the tutorial
                // already advanced to CarryWood1) instead of waiting to fill up - keeps the bot
                // from over-chopping past what the current step actually needs.
                if (targetOre != null)
                {
                    bool oreTargetReached = resourceCarryTarget.HasValue
                        ? cachedCarryStack.GetCount(CarryLayer.Ore) >= resourceCarryTarget.Value
                        : cachedCarryStack.IsFull(CarryLayer.Ore);
                    return ShouldKeepGathering(isWood: false) && targetOre.IsAvailable && !oreTargetReached;
                }

                if (targetWood != null)
                {
                    bool woodTargetReached = resourceCarryTarget.HasValue
                        ? cachedCarryStack.GetCount(CarryLayer.Wood) >= resourceCarryTarget.Value
                        : cachedCarryStack.IsFull(CarryLayer.Wood);
                    return ShouldKeepGathering(isWood: true) && targetWood.IsAvailable && !woodTargetReached;
                }

                return false;

            case Goal.Depot:
                return targetDepot != null && CarriedAnyAcceptedLayer(targetDepot);

            case Goal.Smithy:
                return targetSmithy != null;

            case Goal.Rack:
                return targetRack != null && cachedCarryStack.GetCount(CarryLayer.Weapon) <= 0;

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

    // Whether it's still appropriate to be gathering this specific resource right now. The
    // tutorial's opening sequence is a fixed wood-then-ore order (GatherWood1 must finish before
    // GatherOre1 even starts, 사용자 요청 2026-08-24: "채석장을 먼저 가려고 하잖아" - the old
    // need-based pick ignored that order entirely and went for whichever raw bank was emptier,
    // which is ore on a brand new save with 0 of everything). PreciseCraft's own dynamic re-gather
    // sub-step and SellWeapon's own batch-resupply (TryResupplyForSellWeapon, for when a customer
    // wants more than one QUICK CRAFT's worth) have no such fixed order - each re-gathers whichever
    // the recipe is short on - and once the tutorial's done there's no order to respect either.
    // (This one was missing SellWeapon originally - a Resource goal picked during that step would
    // fail this check on the very next tick and get cancelled, immediately reselected, cancelled
    // again... a visible stutter, 사용자 요청 2026-08-24: "다시 자원을 캐서 무기를 만들러 가야지
    // 버벅이는 버그".)
    private static bool ShouldKeepGathering(bool isWood)
    {
        if (GuidedTutorial.HasCompletedTutorial)
        {
            return true;
        }

        GuidedTutorial.Step step = GuidedTutorial.CurrentStep;

        if (step == GuidedTutorial.Step.PreciseCraft || step == GuidedTutorial.Step.SellWeapon)
        {
            return true;
        }

        return isWood ? step == GuidedTutorial.Step.Move || step == GuidedTutorial.Step.GatherWood1
                       : step == GuidedTutorial.Step.GatherOre1;
    }

    // Mirrors PlayerKeyboardInput's own key list - deliberately not shared code, since that class
    // has no reason to expose this and this bot has no reason to depend on it beyond this check.
    private static bool RealKeyboardInputHeld()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return false;
        }

        return keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed
            || keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed || keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed;
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

            case GuidedTutorial.Step.CarryWood1:
            case GuidedTutorial.Step.CarryOre1:
                // Whatever was just gathered has to go to the box before anything else - without
                // this, the generic fallback below would fall back to its need-based pick (which
                // could send the bot straight to the *other* resource instead of depositing what's
                // already on its back, since the carried amount is usually well under capacity and
                // never trips the IsFull check further down).
                if (TrySetDepotGoalIfCarrying())
                {
                    return;
                }

                break;

            case GuidedTutorial.Step.SellWeapon:
                if (TrySetCounterGoal())
                {
                    return;
                }

                if (TryResupplyForSellWeapon())
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
                // Actually enters and fights now instead of a dev bypass (사용자 요청 2026-08-24:
                // "스테이지 진행 자체를 오토 플레이는 스킵해 버리는 것 같은데 직접 플레이까지
                // 하도록 해줘야지") - no combat AI needed here at all: monsters spawn at the lane's
                // far end and walk to the player on their own (StageEncounterController), and
                // PlayerCombat auto-attacks anything within its own range, so simply entering and
                // standing still is enough to clear every wave. EnterStage no-ops harmlessly while
                // already mid-fight/mid-transition, so it's safe to just call it every tick.
                if (StageEncounterController.Instance.IsEncounterActive)
                {
                    return; // let the fight resolve on its own - don't fall into the gather logic below
                }

                StageSceneController.Instance.EnterStage(StageBank.HighestStageCleared + 1);
                return;

            case GuidedTutorial.Step.DungeonProgress:
                // Same real-play approach as StageProgress. Unlike a stage, the dungeon keeps
                // auto-descending floor after floor with no natural stopping point - once the
                // tutorial's own condition (DeepestFloorCleared > 0, i.e. Floor 1's boss is dead)
                // is already satisfied, retreat cleanly instead of continuing deeper while this
                // bot's attention has already moved on to whatever comes after the tutorial.
                if (DungeonEncounterController.Instance.IsEncounterActive)
                {
                    if (DungeonBank.DeepestFloorCleared > 0)
                    {
                        DungeonSceneController.Instance.RequestRetreat();
                    }

                    return;
                }

                DungeonSceneController.Instance.EnterDungeon();
                return;
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
            targetRack = null;
            targetTransform = smithy.transform;
            return;
        }

        // Which type to gather: the tutorial's fixed wood-then-ore opening sequence (Move/
        // GatherWood1 -> wood, GatherOre1 -> ore) overrides the generic need-based pick, which
        // otherwise ignores step order entirely and would go for whichever raw bank is emptier -
        // ore, on a brand new save with 0 of everything (사용자 요청 2026-08-24). Everywhere else
        // (PreciseCraft's own dynamic re-gather, or post-tutorial free-farming) keeps the original
        // need-based heuristic, since "just whichever is closer" can get stuck looping on one
        // resource type forever if its field sits closer to the player's usual path than the other.
        bool wantOre;
        if (tutorialStep == GuidedTutorial.Step.GatherOre1)
        {
            wantOre = true;
        }
        else if (tutorialStep == GuidedTutorial.Step.Move || tutorialStep == GuidedTutorial.Step.GatherWood1)
        {
            wantOre = false;
        }
        else
        {
            CraftingStation referenceStation = FindNearest(stations, s => true, s => s.transform.position);
            wantOre = referenceStation == null || referenceStation.NeedsOre;
        }

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
            targetRack = null;
            resourceCarryTarget = null;
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
            targetRack = null;
            resourceCarryTarget = null;
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
            targetRack = null;
            resourceCarryTarget = null;
            targetTransform = ore.transform;
        }
        else
        {
            currentGoal = Goal.None;
            targetTransform = null;
        }
    }

    // Tutorial-only redirect for CarryWood1/CarryOre1 - go deposit whatever's already carried
    // right now, instead of letting the generic fallback below reach for its need-based pick
    // (which could send the bot after the *other* resource instead).
    private bool TrySetDepotGoalIfCarrying()
    {
        if (cachedCarryStack.GetCount(CarryLayer.Wood) <= 0 && cachedCarryStack.GetCount(CarryLayer.Ore) <= 0)
        {
            return false; // shouldn't normally happen mid CarryWood1/CarryOre1, but stay safe
        }

        StorageDepot depot = FindNearest(FindObjectsByType<StorageDepot>(FindObjectsSortMode.None), d => true, d => d.transform.position);
        if (depot == null)
        {
            return false;
        }

        SetDepotGoal(depot);
        return true;
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
        targetRack = null;
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
        targetRack = null;
        targetTransform = monster.transform;
        return true;
    }

    // When a customer wants more than one QUICK CRAFT's worth, gather enough raw material for
    // *all* the still-remaining count in one sustained trip instead of the old interleaved
    // "gather one recipe's worth -> craft -> sell -> repeat" cycle, which meant a full round trip
    // to the resource fields for every single unit the customer wanted (사용자 요청 2026-08-24:
    // "그만큼 다 캐고... 비용도 덜 들 것 같은데" - batch the trip). Wood first, then ore, matching
    // the same order the opening sequence teaches; deposits whatever's already carried from a
    // previous partial trip before gathering more, same "flush before continuing" rule
    // CarryWood1/CarryOre1 use.
    private bool TryResupplyForSellWeapon()
    {
        CraftingStation smithy = FindNearest(FindObjectsByType<CraftingStation>(FindObjectsSortMode.None), s => true, s => s.transform.position);
        if (smithy == null)
        {
            return false;
        }

        if (smithy.CanCraft)
        {
            currentGoal = Goal.Smithy;
            targetSmithy = smithy;
            targetOre = null;
            targetWood = null;
            targetDepot = null;
            targetCounter = null;
            targetMonster = null;
            targetRack = null;
            targetTransform = smithy.transform;
            return true;
        }

        if (cachedCarryStack.GetCount(CarryLayer.Wood) > 0 || cachedCarryStack.GetCount(CarryLayer.Ore) > 0)
        {
            StorageDepot depot = FindNearest(FindObjectsByType<StorageDepot>(FindObjectsSortMode.None), d => true, d => d.transform.position);
            if (depot == null)
            {
                return false;
            }

            SetDepotGoal(depot);
            return true;
        }

        int remaining = RemainingOrderCount();

        if (smithy.NeedsWood)
        {
            WoodNode wood = FindNearest(FindObjectsByType<WoodNode>(FindObjectsSortMode.None), n => n.IsAvailable, n => n.transform.position);
            if (wood == null)
            {
                return false;
            }

            currentGoal = Goal.Resource;
            targetWood = wood;
            targetOre = null;
            targetDepot = null;
            targetSmithy = null;
            targetCounter = null;
            targetMonster = null;
            targetRack = null;
            resourceCarryTarget = smithy.WoodAmount * remaining;
            targetTransform = wood.transform;
            return true;
        }

        if (smithy.NeedsOre)
        {
            OreNode ore = FindNearest(FindObjectsByType<OreNode>(FindObjectsSortMode.None), n => n.IsAvailable, n => n.transform.position);
            if (ore == null)
            {
                return false;
            }

            currentGoal = Goal.Resource;
            targetOre = ore;
            targetWood = null;
            targetDepot = null;
            targetSmithy = null;
            targetCounter = null;
            targetMonster = null;
            targetRack = null;
            resourceCarryTarget = smithy.OreAmount * remaining;
            targetTransform = ore.transform;
            return true;
        }

        return false;
    }

    // How many more units the front customer's order still needs - falls back to 1 (a single
    // recipe's worth) if there's no customer/queue to read yet, so resupply still makes forward
    // progress instead of gathering an unbounded amount.
    private int RemainingOrderCount()
    {
        OrderQueueManager counter = FindNearest(FindObjectsByType<OrderQueueManager>(FindObjectsSortMode.None), c => true, c => c.transform.position);
        if (counter == null || counter.Queue.Count == 0)
        {
            return 1;
        }

        CustomerOrder order = counter.Queue[0];
        return Mathf.Max(1, order.RequestedCount - order.DeliveredCount);
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
        targetRack = null;
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

    // Actually plays through the real precise-craft flow (silhouette panel + both minigames)
    // instead of a fixed-quality bypass (사용자 요청 2026-08-24: "미니게임을 실제로 플레이") - fills
    // materials, confirms FORGE, then drives the melting/hammering minigames by reading their live
    // state each frame and reacting to it (not a blind fixed-timing script), so the resulting grade
    // is genuinely earned and this exercises the same UI regression testing is meant to catch bugs
    // in. Runs until CraftingMinigameUI.ShowGradeResult finishes and CraftingStation.IsCrafting
    // drops back to false, then hands control back to RunAutoPlayTick.
    private IEnumerator DrivePreciseCraftRoutine(CraftingStation smithy)
    {
        CraftingSilhouetteUI silhouette = CraftingSilhouetteUI.Instance;
        CraftingMinigameUI minigame = CraftingMinigameUI.Instance;

        // --- Material selection: fill ore+wood (no enchant) and confirm FORGE the moment both
        // are filled. Bounded by a safety timer in case something upstream never actually starts
        // the flow, so this can never hang the whole controller forever.
        float safety = 0f;
        while (smithy.IsCrafting && !minigame.IsTemperaturePhaseActive && safety < 10f)
        {
            silhouette.DevFillRequiredMaterials();
            silhouette.DevConfirmForge();
            safety += Time.deltaTime;
            yield return null;
        }

        // --- Melting (temperature) minigame: bang-bang controller - do a full down-then-up
        // stroke whenever the value dips near the sweet spot's lower edge, otherwise let it coast.
        // strokeBump/coolRate are fixed parameters (not randomized run to run), so this reliably
        // climbs into and holds the zone without needing anything beyond the live value.
        while (minigame.IsTemperaturePhaseActive)
        {
            if (minigame.CurrentTemperatureValue < minigame.TemperatureSweetMin + 0.03f)
            {
                minigame.PumpHandle.DevSetNormalizedY(0f);
                yield return null;
                minigame.PumpHandle.DevSetNormalizedY(1f);
                yield return null;
                minigame.PumpHandle.DevSetNormalizedY(0f);
            }

            yield return null;
        }

        // --- Hammering minigame: hold for exactly targetPercent% of the charge duration, then
        // release - reads each round's actual (randomized) target instead of guessing.
        while (minigame.IsHammeringPhaseActive)
        {
            int targetPercent = minigame.CurrentHammerTargetPercent;
            float holdDuration = minigame.HammerChargeDuration * Mathf.Clamp01(targetPercent / 100f);

            minigame.MarkerHoldTracker.DevSetHeld(true);
            float held = 0f;
            while (held < holdDuration && minigame.IsHammeringPhaseActive)
            {
                held += Time.deltaTime;
                yield return null;
            }

            minigame.MarkerHoldTracker.DevSetHeld(false);

            // Let this round's hit result/pause play out before reacting to the next round's
            // target - avoids racing CurrentHammerTargetPercent right as it changes.
            yield return new WaitForSeconds(0.5f);
        }

        // ShowGradeResult still has its own short flourish/wait after the hammering phase ends -
        // wait for CraftingStation.IsCrafting to actually drop before handing control back, so the
        // bot doesn't start walking off mid-result-screen.
        float resultSafety = 0f;
        while (smithy.IsCrafting && resultSafety < 5f)
        {
            resultSafety += Time.deltaTime;
            yield return null;
        }

        isDrivingPreciseCraft = false;
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
