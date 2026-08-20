using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Final-form onboarding, replacing TutorialUI's read-and-skip slideshow: a single welcome card,
// then a top banner + a floor arrow that points at the next objective and auto-advances the
// moment the player actually does it (no "tap to continue"). Self-contained runtime UI, same
// pattern as every other UI class in this project. See guided_tutorial_design_v2.html for the
// full step-by-step breakdown this class implements - wood -> box -> ore -> box -> QUICK CRAFT ->
// carry the result to the counter and sell it -> hunt a field monster for a Mana Stone -> precise
// CRAFT (gathering a bit more wood/ore first if needed) -> equip it -> clear all Stages -> clear a
// Dungeon floor. Runs the whole early loop end to end, not just gather->sell.
public class GuidedTutorial : MonoBehaviour
{
    private const string SeenPrefsKey = "GuidedTutorialSeen";

    private static GuidedTutorial instance;

    public static GuidedTutorial Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("GuidedTutorial");
                instance = go.AddComponent<GuidedTutorial>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private enum Step
    {
        Welcome,
        Move,
        GatherWood1, CarryWood1, GatherOre1, CarryOre1, QuickCraft,
        // The old GatherWood2/CarryWood2/GatherOre2/CarryOre2 quartet (a second fixed gather round
        // before PreciseCraft) is gone as separate steps - PreciseCraft now folds "gather whatever
        // the recipe is still short on" into itself dynamically (see UpdatePreciseCraftBanner),
        // since QUICK CRAFT no longer refunds any leftover materials to plan around and the exact
        // shortfall (wood, ore, or neither) varies run to run.
        SellWeapon,
        HuntMonster,
        PreciseCraft,
        Equip,
        StageProgress,
        DungeonProgress,
        Done
    }

    // Recipe amounts are read from the live CraftingStation each cycle (see FindReferences) so
    // this never drifts out of sync with whatever the actual recipe requires; these are just the
    // fallback if that station can't be found for some reason.
    private const int FallbackWoodTarget = 1;
    private const int FallbackOreTarget = 2;

    private Step step = Step.Welcome;
    private bool running;

    private GameObject welcomePanel;
    private GameObject bannerRoot;
    private TMP_Text bannerText;
    private Transform arrowRoot;

    private Transform smithy;
    private Transform storageCrateWood;
    private Transform storageCrateOre;
    private Transform salesCounter;
    private CraftingStation craftingStation;
    private StorageDepot woodDepot;
    private StorageDepot oreDepot;
    private OrderQueueManager orderQueueManager;
    private CarryStack playerCarryStack;

    // Gather steps have no formal "interaction radius" field to read (chopping/mining is trigger-
    // collider driven, not a polled distance check like the other three stops) - this is an
    // approximation of that trigger footprint, just for deciding when to hide the arrow.
    private const float GatherHideRadius = 1.2f;

    private int lastWood;
    private int lastOre;
    private int lastGold;
    private bool quickCraftDone;
    private bool preciseCraftDone;

    private int WoodTarget => craftingStation != null ? craftingStation.WoodAmount : FallbackWoodTarget;
    private int OreTarget => craftingStation != null ? craftingStation.OreAmount : FallbackOreTarget;

    // Features unlock progressively as the tutorial teaches the mechanic each depends on, instead
    // of showing every icon from the very first frame (user request: "튜토리얼 중에는 튜토리얼만
    // 오로지 깰 수 있게... 가방 스테이지 등이 열리는 게 좋을 것 같아"). PlayerInventoryUI/
    // StageSelectUI poll these to decide whether their icon is even visible.
    //
    // HasCompletedTutorial short-circuits both - a player who finished the tutorial in a *past*
    // session sees everything unlocked immediately, even though this fresh instance's own `step`
    // field starts back at Welcome (Begin() is never called again once SeenPrefsKey is set).
    public static bool HasCompletedTutorial => PlayerPrefs.GetInt(SeenPrefsKey, 0) != 0;

    // Dev-only shortcut (DevAutoPlayController's dev panel) - the "UNLOCK ALL STAGES" button only
    // touches StageBank, not this gate, so testing stages/dungeon still required playing through
    // the whole tutorial first to unlock their icons. This sets the same flag Step.Done does,
    // instantly unlocking Equipment/Stages/BlackMarket.
    public static void SkipToComplete()
    {
        PlayerPrefs.SetInt(SeenPrefsKey, 1);
        PlayerPrefs.Save();
    }

    // Equipment has nothing to show until the player owns a precise-crafted weapon - QUICK CRAFT
    // output is sell-only now (carried straight to the sales counter, never ToolInventory - see
    // customer_order_design_v7.html §0 Q2), so unlocking any earlier than this would open an empty
    // BAG with nothing to equip. Unlocks the instant PreciseCraft succeeds (entering Step.Equip).
    public static bool IsEquipmentUnlocked => HasCompletedTutorial || (instance != null && instance.step >= Step.Equip);

    // Stages are themselves a taught step now (Step.StageProgress) rather than something that only
    // appears after the tutorial ends - the STAGE icon has to be visible by the time that step's
    // banner tells the player to tap it.
    public static bool IsStagesUnlocked => HasCompletedTutorial || (instance != null && instance.step >= Step.StageProgress);

    // The black market deals in gold and rare-grade materials, neither of which mean anything
    // before the tutorial teaches selling - same threshold as Stages, and same gap this closes as
    // Equipment/Stages did (previously the merchant could show up mid-tutorial with no explanation).
    public static bool IsBlackMarketUnlocked => HasCompletedTutorial;

    private void Awake()
    {
        BuildWelcomeCard();
        BuildBanner();
        BuildArrow();
        BuildHelpButton();
        welcomePanel.SetActive(false);
        bannerRoot.SetActive(false);
        arrowRoot.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        CraftingStation.OnCrafted += HandleCrafted;
    }

    private void OnDisable()
    {
        CraftingStation.OnCrafted -= HandleCrafted;
    }

    private void HandleCrafted(bool wasQuickCraft)
    {
        if (wasQuickCraft)
        {
            quickCraftDone = true;
        }
        else
        {
            preciseCraftDone = true;
        }
    }

    // Called once from ResourceHUD.Start() - see that class's comment for why that particular
    // component is the bootstrap point.
    public void ShowIfFirstTime()
    {
        if (PlayerPrefs.GetInt(SeenPrefsKey, 0) != 0)
        {
            return;
        }

        Begin();
    }

    public void Begin()
    {
        FindReferences();

        step = Step.Welcome;
        running = true;
        bannerRoot.SetActive(false);
        arrowRoot.gameObject.SetActive(false);
        welcomePanel.SetActive(true);
    }

    private void FindReferences()
    {
        GameObject smithyGO = GameObject.Find("Smithy");
        smithy = smithyGO != null ? smithyGO.transform : null;
        craftingStation = smithyGO != null ? smithyGO.GetComponentInChildren<CraftingStation>() : null;

        GameObject woodCrateGO = GameObject.Find("StorageCrate1");
        storageCrateWood = woodCrateGO != null ? woodCrateGO.transform : null;
        woodDepot = woodCrateGO != null ? woodCrateGO.GetComponent<StorageDepot>() : null;

        GameObject oreCrateGO = GameObject.Find("StorageCrate2");
        storageCrateOre = oreCrateGO != null ? oreCrateGO.transform : null;
        oreDepot = oreCrateGO != null ? oreCrateGO.GetComponent<StorageDepot>() : null;

        GameObject counterGO = GameObject.Find("SalesCounter");
        salesCounter = counterGO != null ? counterGO.transform : null;
        orderQueueManager = counterGO != null ? counterGO.GetComponent<OrderQueueManager>() : null;
    }

    private void OnWelcomeStart()
    {
        welcomePanel.SetActive(false);
        bannerRoot.SetActive(true);
        EnterStep(Step.Move);
    }

    private void EnterStep(Step next)
    {
        step = next;

        switch (step)
        {
            case Step.Move:
                bannerText.text = "Drag anywhere on screen to move";
                break;
            case Step.CarryWood1:
                bannerText.text = "Bring the wood to the box";
                break;
            case Step.CarryOre1:
                bannerText.text = "Bring the ore to the box";
                break;
            case Step.QuickCraft:
                bannerText.text = "At the Smithy, tap QUICK CRAFT for an instant result";
                quickCraftDone = false;
                break;
            case Step.SellWeapon:
                bannerText.text = "Carry the weapon to the counter and sell it";
                break;
            case Step.PreciseCraft:
                preciseCraftDone = false;
                break;
            case Step.Equip:
                bannerText.text = "Open your BAG and equip your new weapon";
                break;
            case Step.Done:
                bannerText.text = "You're all set - have fun!";
                arrowRoot.gameObject.SetActive(false);
                PlayerPrefs.SetInt(SeenPrefsKey, 1);
                PlayerPrefs.Save();
                running = false;
                Invoke(nameof(HideBanner), 3f);

                // Stages already unlocked back at Step.StageProgress (its own toast fired there) -
                // only the black market is new at this final gate.
                ToastUI.Instance.Show("Black Market Unlocked!", 4f);
                break;
        }

        lastWood = ResourceBank.Get(ResourceType.Wood);
        lastOre = ResourceBank.Get(ResourceType.Ore);
        lastGold = SalesCurrency.Gold;

        if (playerCarryStack == null && PlayerMotor.Instance != null)
        {
            playerCarryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
        }
    }

    private void HideBanner()
    {
        bannerRoot.SetActive(false);
    }

    private void Update()
    {
        if (!running)
        {
            return;
        }

        UpdateArrow();

        switch (step)
        {
            case Step.Move:
                if (PlayerMotor.Instance != null && PlayerMotor.Instance.HasMovementInput)
                {
                    EnterStep(Step.GatherWood1);
                }
                break;
            case Step.GatherWood1:
                if (TickGather("Chop wood at the nearest tree", CarryLayer.Wood, WoodTarget))
                {
                    EnterStep(Step.CarryWood1);
                }
                break;
            case Step.CarryWood1:
                if (ResourceBank.Get(ResourceType.Wood) > lastWood)
                {
                    EnterStep(Step.GatherOre1);
                }
                break;
            case Step.GatherOre1:
                if (TickGather("Mine ore at the nearest rock", CarryLayer.Ore, OreTarget))
                {
                    EnterStep(Step.CarryOre1);
                }
                break;
            case Step.CarryOre1:
                if (ResourceBank.Get(ResourceType.Ore) > lastOre)
                {
                    EnterStep(Step.QuickCraft);
                }
                break;
            case Step.QuickCraft:
                if (quickCraftDone)
                {
                    EnterStep(Step.SellWeapon);
                }
                break;
            case Step.SellWeapon:
                if (SalesCurrency.Gold > lastGold)
                {
                    EnterStep(Step.HuntMonster);
                }
                break;
            case Step.HuntMonster:
                // Mana Stones are a probability drop (PlayerCombat.BaseManaDropChance), not a
                // guaranteed kill reward, so completion is "carrying at least one" rather than
                // "landed N hits" - an unlucky player just naturally ends up fighting a few more.
                if (TickGather("Defeat a field monster for a Mana Stone", CarryLayer.ManaStone, 1))
                {
                    EnterStep(Step.PreciseCraft);
                }
                break;
            case Step.PreciseCraft:
                if (preciseCraftDone)
                {
                    EnterStep(Step.Equip);
                }
                else
                {
                    UpdatePreciseCraftBanner();
                }
                break;
            case Step.Equip:
                if (EquippedWeapon.HasExplicitChoice)
                {
                    EnterStep(Step.StageProgress);
                    ToastUI.Instance.Show("Stages Unlocked!", 3f);
                }
                break;
            case Step.StageProgress:
                bannerText.text = "Clear all Stages at STAGE (" + StageBank.HighestStageCleared + "/" + StageBank.StageCount + ")";
                if (StageBank.AllStagesCleared)
                {
                    EnterStep(Step.DungeonProgress);
                }
                break;
            case Step.DungeonProgress:
                bannerText.text = "Dive into the DUNGEON and clear Floor 1";
                if (DungeonBank.DeepestFloorCleared > 0)
                {
                    EnterStep(Step.Done);
                }
                break;
        }
    }

    // PreciseCraft folds "gather whatever the recipe is still short on" into itself instead of
    // fixed GatherWood2/CarryOre2 steps (see the Step enum comment) - reuses the exact same
    // Needs*/Target reads DevAutoPlayController's auto-play loop already relies on, so the arrow/
    // banner never drifts out of sync with what CraftingStation.CanCraft actually checks.
    private void UpdatePreciseCraftBanner()
    {
        if (craftingStation != null && craftingStation.NeedsWood)
        {
            if (TickGather("Chop a little more wood", CarryLayer.Wood, WoodTarget))
            {
                bannerText.text = "Bring the wood to the box";
            }

            return;
        }

        if (craftingStation != null && craftingStation.NeedsOre)
        {
            if (TickGather("Mine a little more ore", CarryLayer.Ore, OreTarget))
            {
                bannerText.text = "Bring the ore to the box";
            }

            return;
        }

        bannerText.text = "Now try CRAFT for a hands-on, higher-quality result";
    }

    // Shared by all four "gather N of X" steps - shows live progress in the banner and reports
    // whether the target has been reached. Carried (not-yet-deposited) amount, not the bank total,
    // since the point is teaching "chop first, then go deposit" as two distinct actions.
    private bool TickGather(string label, CarryLayer layer, int target)
    {
        int carried = playerCarryStack != null ? playerCarryStack.GetCount(layer) : 0;
        bannerText.text = label + " (" + Mathf.Min(carried, target) + "/" + target + ")";
        return carried >= target;
    }

    private void UpdateArrow()
    {
        // Stage/Dungeon fights run as additive overlays on top of this same scene (see
        // StageSceneController/DungeonSceneController), so this instance stays alive and Update()
        // keeps ticking straight through them - suppress the floor arrow during an active encounter
        // so it doesn't float around in the middle of combat; the banner text still updates.
        if (StageEncounterController.Instance.IsEncounterActive || DungeonEncounterController.Instance.IsEncounterActive)
        {
            arrowRoot.gameObject.SetActive(false);
            return;
        }

        Transform target = null;
        float hideRadius = GatherHideRadius;

        switch (step)
        {
            case Step.GatherWood1:
                target = FindNearestAvailable<WoodNode>(n => n.IsAvailable);
                break;
            case Step.CarryWood1:
                target = storageCrateWood;
                hideRadius = woodDepot != null ? woodDepot.DepositRadius : GatherHideRadius;
                break;
            case Step.GatherOre1:
                target = FindNearestAvailable<OreNode>(n => n.IsAvailable);
                break;
            case Step.CarryOre1:
                target = storageCrateOre;
                hideRadius = oreDepot != null ? oreDepot.DepositRadius : GatherHideRadius;
                break;
            case Step.QuickCraft:
                target = smithy;
                hideRadius = craftingStation != null ? craftingStation.InteractRadius : GatherHideRadius;
                break;
            case Step.SellWeapon:
                target = salesCounter;
                hideRadius = orderQueueManager != null ? orderQueueManager.InteractRadius : GatherHideRadius;
                break;
            case Step.HuntMonster:
                target = FindNearestAvailable<Monster>(m => m.IsAvailable);
                break;
            case Step.PreciseCraft:
                target = ResolvePreciseCraftArrowTarget(out hideRadius);
                break;
            // Equip/StageProgress/DungeonProgress all point at screen-space UI icons (BAG/STAGE),
            // not a world position - no arrow target makes sense for them, same as Welcome/Done.
        }

        if (target == null || PlayerMotor.Instance == null)
        {
            arrowRoot.gameObject.SetActive(false);
            return;
        }

        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        Vector3 toTarget = target.position - playerPos;
        toTarget.y = 0f;

        // Hide once the player is already close enough to interact with the target (matches that
        // target's own real interaction/deposit radius, not a fixed dead zone) - shows again the
        // moment they're far enough away (or the target is off the beaten path) to need directing.
        if (toTarget.sqrMagnitude < hideRadius * hideRadius)
        {
            arrowRoot.gameObject.SetActive(false);
            return;
        }

        arrowRoot.gameObject.SetActive(true);
        Vector3 direction = toTarget.normalized;
        arrowRoot.position = playerPos + direction * 1.6f + Vector3.up * 1.3f;
        arrowRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);
    }

    // Mirrors UpdatePreciseCraftBanner()'s own Needs*/carried-count reads so the arrow always
    // points at whatever that banner is currently asking for (gather -> carry to box -> smithy),
    // instead of a separately-maintained copy of the same logic that could drift out of sync.
    private Transform ResolvePreciseCraftArrowTarget(out float hideRadius)
    {
        hideRadius = GatherHideRadius;

        if (craftingStation != null && craftingStation.NeedsWood)
        {
            int carried = playerCarryStack != null ? playerCarryStack.GetCount(CarryLayer.Wood) : 0;
            if (carried >= WoodTarget)
            {
                hideRadius = woodDepot != null ? woodDepot.DepositRadius : GatherHideRadius;
                return storageCrateWood;
            }

            return FindNearestAvailable<WoodNode>(n => n.IsAvailable);
        }

        if (craftingStation != null && craftingStation.NeedsOre)
        {
            int carried = playerCarryStack != null ? playerCarryStack.GetCount(CarryLayer.Ore) : 0;
            if (carried >= OreTarget)
            {
                hideRadius = oreDepot != null ? oreDepot.DepositRadius : GatherHideRadius;
                return storageCrateOre;
            }

            return FindNearestAvailable<OreNode>(n => n.IsAvailable);
        }

        hideRadius = craftingStation != null ? craftingStation.InteractRadius : GatherHideRadius;
        return smithy;
    }

    // Trees/ore chunks are scattered at runtime by TreeFieldSpawner/ResourceFieldSpawner, not
    // fixed scene objects, so the arrow has to re-search for the closest live one every time
    // instead of pointing at a cached reference (which would go stale the moment it's harvested).
    private Transform FindNearestAvailable<T>(System.Func<T, bool> isAvailable) where T : Component
    {
        if (PlayerMotor.Instance == null)
        {
            return null;
        }

        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        T[] candidates = FindObjectsByType<T>(FindObjectsSortMode.None);

        T nearest = null;
        float nearestSqrDist = float.MaxValue;

        foreach (T candidate in candidates)
        {
            if (!isAvailable(candidate))
            {
                continue;
            }

            float sqrDist = (candidate.transform.position - playerPos).sqrMagnitude;
            if (sqrDist < nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = candidate;
            }
        }

        return nearest != null ? nearest.transform : null;
    }

    private void BuildWelcomeCard()
    {
        var canvasGO = new GameObject("GuidedTutorialWelcomeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        welcomePanel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        welcomePanel.transform.SetParent(canvasGO.transform, false);
        var panelRect = welcomePanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        welcomePanel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        var card = new GameObject("Card", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(welcomePanel.transform, false);
        var cardRect = card.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(860f, 620f);
        card.GetComponent<Image>().color = new Color(0.98f, 0.93f, 0.85f, 0.98f);

        var title = MakeText(card.transform, "Title", 40, new Vector2(0f, -80f), new Vector2(760f, 60f));
        title.color = new Color(0.16f, 0.13f, 0.1f);
        title.fontStyle = FontStyles.Bold;
        title.text = "Isekai Smith";

        var body = MakeText(card.transform, "Body", 26, new Vector2(0f, -200f), new Vector2(700f, 260f));
        body.color = new Color(0.25f, 0.21f, 0.17f);
        body.text = "Rebuild a blacksmith's forge in a ruined world.\n\nFollow the arrow on the ground -\nit'll show you where to go next.";

        Button startButton = MakeButton(card.transform, "StartButton", new Vector2(0f, 70f), new Vector2(280f, 110f), "Start", new Color(0.71f, 0.4f, 0.11f));
        startButton.onClick.AddListener(OnWelcomeStart);
    }

    private void BuildBanner()
    {
        var canvasGO = new GameObject("GuidedTutorialBannerCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above DevAutoPlayController's dev-only panel (order 20, top-left, editor/dev builds
        // only) so the banner is never hidden behind it while testing - still below the welcome/
        // help canvases (50), which are full-screen or genuinely modal.
        canvas.sortingOrder = 25;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        bannerRoot = new GameObject("Banner", typeof(RectTransform), typeof(Image), typeof(Outline));
        bannerRoot.transform.SetParent(canvasGO.transform, false);
        var bannerRect = bannerRoot.GetComponent<RectTransform>();
        // Right edge, slightly below vertical center (사용자 요청: "오른쪽 중단보다 살짝 아래") -
        // the previous top-center full-width banner sat right in the middle of this game's
        // busiest screen real estate (resource panel/dev overlay both compete for the top edge),
        // which is why it kept needing more contrast/sorting-order just to stay legible - the
        // form factor and placement were the actual problem, not just layering. This corner has
        // nothing else in it, and shrinking it into a labeled card reads as a persistent quest
        // tracker instead of a transient alert banner.
        bannerRect.anchorMin = new Vector2(1f, 0.5f);
        bannerRect.anchorMax = new Vector2(1f, 0.5f);
        bannerRect.pivot = new Vector2(1f, 0.5f);
        bannerRect.anchoredPosition = new Vector2(-24f, -140f);
        bannerRect.sizeDelta = new Vector2(320f, 104f);
        bannerRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
        var outline = bannerRoot.GetComponent<Outline>();
        outline.effectColor = new Color(1f, 0.86f, 0.5f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        // Small "QUEST" label up top so the card reads unambiguously as "your current objective"
        // at a glance, instead of an unlabeled floating bar.
        var header = MakeText(bannerRoot.transform, "Header", 16, new Vector2(0f, -10f), new Vector2(300f, 22f));
        header.color = new Color(1f, 0.86f, 0.5f, 0.8f);
        header.fontStyle = FontStyles.Bold;
        header.text = "QUEST";

        bannerText = MakeText(bannerRoot.transform, "Text", 22, new Vector2(0f, -36f), new Vector2(292f, 62f));
        bannerText.color = Color.white;
        bannerText.fontStyle = FontStyles.Bold;
        bannerText.textWrappingMode = TextWrappingModes.Normal;
    }

    // Plain placeholder pointer (a single bright bar, like a compass needle) rather than a real
    // arrow mesh/sprite - matches this project's current art bar (Player is a bare capsule).
    // Points its local +Z at the target via LookRotation, floating just ahead of the player.
    private void BuildArrow()
    {
        var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arrow.name = "GuidedTutorialArrow";
        Destroy(arrow.GetComponent<Collider>());
        arrow.transform.localScale = new Vector3(0.18f, 0.18f, 1.4f);
        var renderer = arrow.GetComponent<MeshRenderer>();
        renderer.material.color = new Color(1f, 0.85f, 0.25f);
        arrowRoot = arrow.transform;
    }

    private void BuildHelpButton()
    {
        var canvasGO = new GameObject("GuidedTutorialHelpCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 1f;

        // Bottom-left, matching where TutorialUI's help button used to live (top-right is
        // ResourceHUD's resource panel, top-left is DevAutoPlayController's debug buttons).
        Button helpButton = MakeButton(canvasGO.transform, "HelpButton", new Vector2(24f, 24f), new Vector2(96f, 96f), "?", new Color(0f, 0f, 0f, 0.5f));
        var helpRect = helpButton.GetComponent<RectTransform>();
        helpRect.anchorMin = new Vector2(0f, 0f);
        helpRect.anchorMax = new Vector2(0f, 0f);
        helpRect.pivot = new Vector2(0f, 0f);
        helpButton.onClick.AddListener(Begin);
    }

    private TMP_Text MakeText(Transform parent, string name, int fontSize, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        return tmp;
    }

    private Button MakeButton(Transform parent, string name, Vector2 anchoredPos, Vector2 size, string text, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = color;

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(go.transform, false);
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 30;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;

        return go.GetComponent<Button>();
    }
}
