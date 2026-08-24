using System;
using System.Collections;
using UnityEngine;

// Unified forge workstation: furnace (melt) and anvil (hammer) are placed right next to each
// other, so interacting with either is treated as one combined crafting action instead of two
// separate stops. Flow: pick materials on the weapon silhouette -> melt (temperature minigame)
// -> hammer (hammering minigame) -> weapon is done.
public class CraftingStation : MonoBehaviour
{
    // Fired on a successful craft (bool = was the QUICK CRAFT button, false = the precise
    // silhouette/minigame flow) - GuidedTutorial listens so it can require the player to actually
    // try each method instead of just detecting "a craft happened".
    public static event Action<bool> OnCrafted;


    // Fallback weapon type for QUICK CRAFT only - that path skips CraftingSilhouetteUI entirely so
    // there's no weapon-type picker to read from. The precise Craft flow instead lets the player
    // choose from any WeaponType via the silhouette panel's <Forge: X> selector, so one station
    // now forges all weapon types rather than needing a separate CraftingStation instance per type.
    // See weapon_diversity_design_v1.html §8.
    [SerializeField] private WeaponType quickCraftWeaponType = WeaponType.Sword;

    // No oreType field - ore is always graded (OreBank.TryGetBestAvailable/TrySpend). Mana is now
    // graded too (ManaBank.TryGetBestAvailable/TrySpend) - see mana_grade_and_ui_design_v1.html §1
    // - so it no longer needs a ResourceType field either, only wood is still a flat ResourceBank
    // count. Same recipe cost regardless of which WeaponType the player selects in the silhouette
    // panel - per-weapon recipe costs are deferred (weapon_diversity_design_v1.html §8).
    [SerializeField] private int oreAmount = 2;
    [SerializeField] private ResourceType woodType = ResourceType.Wood;
    [SerializeField] private int woodAmount = 1;
    [SerializeField] private int outputAmount = 1;
    [SerializeField] private float interactRadius = 2.5f;
    [SerializeField] private string stationTitle = "Forge: Sword";

    [SerializeField] private Transform furnacePulseVisual;
    [SerializeField] private Transform anvilPulseVisual;
    [SerializeField] private float pulseStrength = 0.12f;
    [SerializeField] private float pulseSpeed = 10f;

    // This component lives on FurnaceBody itself (Smithy.prefab), so transform.position/.right are
    // the furnace's own - QUICK CRAFT output used to spawn exactly there, which buried the sword
    // prop inside the furnace mesh at the moment it appeared (사용자 요청 2026-08-21: "완성된 검이
    // 시각적으로 잘보이도록... 용광로 왼쪽에 배치"). AnvilTop sits on the furnace's +right side
    // (local x -0.55 vs the furnace's -1.6), so -transform.right reliably means "away from the
    // anvil" - i.e. the furnace's left - regardless of how the whole Smithy prefab is rotated when
    // placed in a scene (transform.right already reflects that rotation; unlike a raw local-space
    // offset, it isn't distorted by the furnace's own non-uniform scale either).
    [SerializeField] private float weaponOutputLeftOffset = 1.3f;
    [SerializeField] private float weaponOutputHeightOffset = 0.3f;

    [Header("Smelting Minigame (Drag Pump) - drag the handle up/down; heat always fades")]
    [SerializeField] private float temperatureDuration = 9f;
    [SerializeField] private float sweetMin = 0.55f;
    [SerializeField] private float sweetMax = 0.75f;
    [SerializeField] private float strokeBump = 0.22f;
    [SerializeField] private float coolRate = 0.15f;
    [SerializeField] private float overheatPenaltyMultiplier = 1.5f;

    [Header("Hammering Minigame (Forge) - hold the marker, release on the target power %")]
    [SerializeField] private int hammerRounds = 4;
    [SerializeField] private float chargeDuration = 1.4f;
    [SerializeField] private float perfectTolerancePercent = 10f;
    [SerializeField] private float goodTolerancePercent = 30f;

    private Vector3 furnacePulseBaseScale;
    private Vector3 anvilPulseBaseScale;
    private bool isCrafting;
    private bool promptShown;
    private WeaponRack weaponRack;

    private void Awake()
    {
        if (furnacePulseVisual != null)
        {
            furnacePulseBaseScale = furnacePulseVisual.localScale;
        }

        if (anvilPulseVisual != null)
        {
            anvilPulseBaseScale = anvilPulseVisual.localScale;
        }

        InteractionPadIndicator.Attach(transform, interactRadius);

        // QUICK CRAFT output no longer flies straight onto the player - it piles up here first and
        // only transfers on approach (weapon_rack_and_order_polish_v1.html §2). Same world spot
        // finished weapons used to appear at. The rack sits weaponOutputLeftOffset away from this
        // transform, so its pickup radius has to reach at least interactRadius + that offset -
        // otherwise crafting from the far side (e.g. standing at the anvil) leaves the rack
        // unreachable and weapons never leave it (버그 수정 2026-08-24).
        weaponRack = WeaponRack.CreateAt(WeaponOutputPosition, transform, interactRadius + weaponOutputLeftOffset + 0.5f);
    }

    private void Update()
    {
        UpdatePulse();

        if (isCrafting)
        {
            return;
        }

        bool nearPlayer = PlayerMotor.Instance != null &&
            (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude <= interactRadius * interactRadius;

        if (!nearPlayer || !HasEnoughInputs())
        {
            if (promptShown)
            {
                InteractionPromptUI.Instance.Hide();
                promptShown = false;
            }

            return;
        }

        if (!promptShown)
        {
            InteractionPromptUI.Instance.Show(
                stationTitle,
                () => StartCoroutine(CraftWithSilhouetteAndMinigames()),
                () => ApplyCraft(0.5f, 0, true, quickCraftWeaponType, out _, out _, out _, out _));
            promptShown = true;
        }
    }

    private bool HasEnoughInputs()
    {
        return OreBank.TryGetBestAvailable(oreAmount, out _) && ResourceBank.Get(woodType) >= woodAmount;
    }

    // Public read of the same eligibility check Update() uses for the interaction prompt -
    // lets other systems (e.g. DevAutoPlayController) know whether a craft would succeed right
    // now without duplicating the recipe check.
    public bool CanCraft => !isCrafting && HasEnoughInputs();

    // Which input the recipe is still short on - lets DevAutoPlayController target gathering by
    // actual need instead of "whichever resource node happens to be physically closer", which
    // can get stuck looping on one resource type forever if its field is nearer to the player's
    // usual path than the other.
    public bool NeedsOre => !OreBank.TryGetBestAvailable(oreAmount, out _);
    public bool NeedsWood => ResourceBank.Get(woodType) < woodAmount;

    // Read-only recipe amounts - lets GuidedTutorial set its "gather N wood/ore" targets from the
    // actual recipe instead of a separately hardcoded number that could drift out of sync.
    public int OreAmount => oreAmount;
    public int WoodAmount => woodAmount;

    // Lets GuidedTutorial hide its floor arrow once the player is already within interacting
    // distance, instead of a separately hardcoded distance that could drift out of sync.
    public float InteractRadius => interactRadius;

    // Where a freshly QUICK CRAFT'd weapon actually appears - see weaponOutputLeftOffset's
    // declaration comment for why this isn't just transform.position.
    private Vector3 WeaponOutputPosition =>
        transform.position - transform.right * weaponOutputLeftOffset + Vector3.up * weaponOutputHeightOffset;

    // Bypasses the silhouette/minigame flow and applies the same fixed-quality result as the
    // QUICK CRAFT button. Used by DevAutoPlayController for automated loop testing.
    public bool TryDevQuickCraft(out CraftGrade grade, out int amount)
    {
        grade = CraftGrade.Rough;
        amount = 0;

        if (!CanCraft)
        {
            return false;
        }

        grade = ApplyCraft(0.5f, 0, true, quickCraftWeaponType, out amount, out _, out _, out _);
        return true;
    }

    private IEnumerator CraftWithSilhouetteAndMinigames()
    {
        isCrafting = true;
        InteractionPromptUI.Instance.Hide();
        promptShown = false;

        bool loaded = false;
        int manaSpent = 0;
        WeaponType chosenWeapon = quickCraftWeaponType;
        yield return CraftingSilhouetteUI.Instance.RunSilhouette((started, mana, weapon) =>
        {
            loaded = started;
            manaSpent = mana;
            chosenWeapon = weapon;
        });

        if (!loaded)
        {
            isCrafting = false;
            yield break;
        }

        float meltQuality = 0.5f;
        yield return CraftingMinigameUI.Instance.RunTemperature(
            "Melting", temperatureDuration, sweetMin, sweetMax, strokeBump, coolRate, overheatPenaltyMultiplier,
            q => meltQuality = q);

        float hammerQuality = 0.5f;
        yield return CraftingMinigameUI.Instance.RunHammering(
            "Forging", hammerRounds, chargeDuration, perfectTolerancePercent, goodTolerancePercent,
            q => hammerQuality = q);

        float quality = (meltQuality + hammerQuality) * 0.5f;
        CraftGrade grade = ApplyCraft(quality, manaSpent, false, chosenWeapon, out int amount, out OreGrade oreGrade, out ManaElement element, out ManaGrade manaGrade);
        yield return CraftingMinigameUI.Instance.ShowGradeResult(chosenWeapon, oreGrade, element, manaGrade, grade, amount);
        isCrafting = false;
    }

    private CraftGrade ApplyCraft(float quality, int manaSpent, bool isQuickCraft, WeaponType weapon, out int amount, out OreGrade oreGrade, out ManaElement element, out ManaGrade manaGrade)
    {
        amount = 0;
        oreGrade = OreGrade.Iron;
        element = ManaElement.None;
        manaGrade = ManaGrade.Crude;

        if (!HasEnoughInputs() || !OreBank.TryGetBestAvailable(oreAmount, out oreGrade))
        {
            return CraftGrade.Rough;
        }

        OreBank.TrySpend(oreGrade, oreAmount);
        ResourceBank.TrySpend(woodType, woodAmount);

        if (manaSpent > 0)
        {
            // Graded mana now lives in ManaBank (mana_grade_and_ui_design_v1.html §1), same split
            // as Ore/OreBank - ResourceBank.ManaStone stays a write-only "lifetime deposited"
            // signal. Auto-picks the best-graded stock with enough units, same rule OreBank uses.
            int actualMana = Mathf.Min(manaSpent, ManaBank.TotalCurrent);
            if (actualMana > 0 && ManaBank.TryGetBestAvailable(actualMana, out manaGrade))
            {
                ManaBank.TrySpend(manaGrade, actualMana);

                // Field-dropped mana carries no element of its own yet (combat_design_v1.html §7),
                // so which element the enchant lands on is rolled rather than chosen - see
                // ManaElementUtility.RollRandom(). Quick Craft never spends mana (no silhouette
                // step to feed it in through), so it can never produce an enchanted result.
                element = ManaElementUtility.RollRandom();
            }
        }

        // QUICK CRAFT no longer rolls a quality-derived grade - it always produces exactly
        // whatever ForgeUpgrade has currently unlocked (customer_order_design_v7.html §2), fixed
        // rather than a range. Precise crafting (the silhouette/minigame path) still uses the
        // quality roll as before.
        CraftGrade grade = isQuickCraft ? ForgeUpgrade.CurrentTier : CraftGradeUtility.GradeFor(quality);
        amount = outputAmount + CraftGradeUtility.BonusAmount(grade);

        if (isQuickCraft)
        {
            // Sell-only pipeline: physical props onto WeaponRack (smithy -> rack -> sales counter),
            // never ToolInventory - QUICK CRAFT output is no longer equippable, only precise-crafted
            // gear is (customer_order_design_v7.html §0 Q2). Weapon/oreGrade/element/manaGrade are
            // meaningless for this path since only the count and grade matter once it's counter
            // stock; grade itself gets resolved again at deposit time (CounterStock reads
            // ForgeUpgrade.CurrentTier then), same "resolve at deposit, not at gather" convention
            // OreBank.DepositMined already uses.
            for (int i = 0; i < amount; i++)
            {
                weaponRack.AddWeapon();
            }
        }
        else
        {
            ToolInventory.Add(weapon, oreGrade, grade, element, manaGrade, amount);
        }

        OnCrafted?.Invoke(isQuickCraft);
        return grade;
    }

    private void UpdatePulse()
    {
        SetPulse(furnacePulseVisual, ref furnacePulseBaseScale);
        SetPulse(anvilPulseVisual, ref anvilPulseBaseScale);
    }

    private void SetPulse(Transform visual, ref Vector3 baseScale)
    {
        if (visual == null)
        {
            return;
        }

        if (!isCrafting)
        {
            visual.localScale = baseScale;
            return;
        }

        float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseStrength;
        visual.localScale = baseScale * pulse;
    }
}
