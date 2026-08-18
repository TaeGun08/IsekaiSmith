using UnityEngine;

// Randomly-appearing NPC vendor (game_design_doc.html §5, black_market_design_v1.html) - sells
// one grade above the player's current OreBank/ManaBank Ceiling at a steep price ("정상 유통
// 경로로는 안 팔리는 희귀 재료"), and buys back low-grade stock at a discount for quick
// emergency cash. Self-bootstrapping singleton, same convention as every other system here.
public class BlackMarketMerchant : MonoBehaviour
{
    private const float CheckInterval = 90f;
    private const float AppearChance = 0.35f;
    private const float VisitDuration = 120f;
    private const float InteractRadius = 2.5f;

    private const int SellPriceMultiplier = 6;
    private const float BuyPriceMultiplier = 0.5f;
    private const int OreOfferMinQty = 2;
    private const int OreOfferMaxQty = 4;
    private const int ManaOfferMinQty = 2;
    private const int ManaOfferMaxQty = 4;

    public const int QuickSellOreAmount = 5;
    public const int QuickSellManaAmount = 5;
    public const int QuickSellWoodAmount = 10;

    private static BlackMarketMerchant instance;

    public static BlackMarketMerchant Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("BlackMarketMerchant");
                instance = go.AddComponent<BlackMarketMerchant>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private struct OreOffer
    {
        public OreGrade Grade;
        public int Remaining;
        public int PricePerUnit;
    }

    private struct ManaOffer
    {
        public ManaGrade Grade;
        public int Remaining;
        public int PricePerUnit;
    }

    private GameObject visualRoot;
    private float checkTimer;
    private float visitTimer;
    private bool visiting;
    private bool promptShown;

    private OreOffer oreOffer;
    private ManaOffer manaOffer;

    public bool IsVisiting => visiting;
    public bool HasOreOffer => visiting && oreOffer.Remaining > 0;
    public bool HasManaOffer => visiting && manaOffer.Remaining > 0;
    public OreGrade OreOfferGrade => oreOffer.Grade;
    public int OreOfferRemaining => oreOffer.Remaining;
    public int OreOfferPricePerUnit => oreOffer.PricePerUnit;
    public ManaGrade ManaOfferGrade => manaOffer.Grade;
    public int ManaOfferRemaining => manaOffer.Remaining;
    public int ManaOfferPricePerUnit => manaOffer.PricePerUnit;

    // Referencing Instance is enough to bootstrap this singleton - same explicit-call-for-
    // readability convention as PlayerCombat/PlayerInventoryUI's Activate().
    public void Activate()
    {
    }

    private void Awake()
    {
        // First appearance check happens after one full interval, not immediately on scene load -
        // avoids "the shady merchant is standing there before the player has even started".
        checkTimer = CheckInterval;
    }

    private void Update()
    {
        if (visiting)
        {
            visitTimer -= Time.deltaTime;
            if (visitTimer <= 0f || (oreOffer.Remaining <= 0 && manaOffer.Remaining <= 0))
            {
                EndVisit();
                return;
            }

            UpdatePrompt();
            return;
        }

        checkTimer -= Time.deltaTime;
        if (checkTimer > 0f)
        {
            return;
        }

        checkTimer = CheckInterval;
        if (Random.value <= AppearChance)
        {
            BeginVisit();
        }
    }

    // Dev-only manual trigger (DevAutoPlayController's dev panel) - bypasses the timer/chance
    // roll entirely. Ends any current visit first so repeated taps always produce a fresh, fully-
    // stocked visit instead of just extending whatever's already there.
    public void ForceBeginVisit()
    {
        if (visiting)
        {
            EndVisit();
        }

        BeginVisit();
    }

    private void BeginVisit()
    {
        visiting = true;
        visitTimer = VisitDuration;
        RollOffers();
        SpawnVisual();
    }

    private void RollOffers()
    {
        OreGrade oreGrade = NextGrade(OreBank.Ceiling, OreGrade.Orichalcum);
        int oreQty = Random.Range(OreOfferMinQty, OreOfferMaxQty + 1);
        oreOffer = new OreOffer
        {
            Grade = oreGrade,
            Remaining = oreQty,
            PricePerUnit = MaterialPricing.OreValue(oreGrade) * SellPriceMultiplier
        };

        ManaGrade manaGrade = NextGrade(ManaBank.Ceiling, ManaGrade.Pristine);
        int manaQty = Random.Range(ManaOfferMinQty, ManaOfferMaxQty + 1);
        manaOffer = new ManaOffer
        {
            Grade = manaGrade,
            Remaining = manaQty,
            PricePerUnit = MaterialPricing.ManaValue(manaGrade) * SellPriceMultiplier
        };
    }

    // One grade above the player's current gathering ceiling, clamped to the top grade - "정상
    // 유통 경로로는 안 팔리는" only makes sense while there's still a higher grade to sell.
    private static TGrade NextGrade<TGrade>(TGrade ceiling, TGrade maxGrade) where TGrade : struct, System.Enum
    {
        int next = System.Convert.ToInt32(ceiling) + 1;
        int max = System.Convert.ToInt32(maxGrade);
        return (TGrade)(object)Mathf.Min(next, max);
    }

    private void SpawnVisual()
    {
        GameObject smithyGO = GameObject.Find("Smithy");
        Vector3 basePosition = smithyGO != null ? smithyGO.transform.position : Vector3.zero;
        // A fixed offset to the side of the smithy - functional placement (clear of the building's
        // own small footprint), not decoration.
        Vector3 groundPosition = basePosition + new Vector3(5f, 0f, 1f);

        visualRoot = new GameObject("BlackMarketMerchant");
        visualRoot.transform.SetParent(transform, false);
        visualRoot.transform.position = groundPosition;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(visualRoot.transform, false);
        body.transform.localPosition = new Vector3(0f, 1f, 0f);
        Destroy(body.GetComponent<Collider>()); // visual only, same convention as Customer/Monster
        body.GetComponent<Renderer>().material.color = new Color(0.28f, 0.18f, 0.32f); // shady purple

        InteractionPadIndicator.Attach(visualRoot.transform, InteractRadius);
    }

    private void UpdatePrompt()
    {
        bool nearPlayer = PlayerMotor.Instance != null && visualRoot != null &&
            (PlayerMotor.Instance.transform.position - visualRoot.transform.position).sqrMagnitude <= InteractRadius * InteractRadius;

        if (!nearPlayer)
        {
            HidePromptIfShown();
            return;
        }

        if (!promptShown)
        {
            InteractionPromptUI.Instance.ShowSingle("Black Market", "TRADE", () =>
            {
                InteractionPromptUI.Instance.Hide();
                promptShown = false;
                BlackMarketUI.Instance.Open();
            });
            promptShown = true;
        }
    }

    private void HidePromptIfShown()
    {
        if (promptShown)
        {
            InteractionPromptUI.Instance.Hide();
            promptShown = false;
        }
    }

    private void EndVisit()
    {
        visiting = false;
        HidePromptIfShown();

        if (visualRoot != null)
        {
            Destroy(visualRoot);
            visualRoot = null;
        }

        BlackMarketUI.Instance.CloseIfOpen();
    }

    // Buy = player spends gold for oreOffer/manaOffer stock (BlackMarketUI's BUY buttons).
    public bool TryBuyOre()
    {
        if (!HasOreOffer || !SalesCurrency.TrySpend(oreOffer.PricePerUnit))
        {
            return false;
        }

        OreBank.AddDirect(oreOffer.Grade, 1);
        oreOffer.Remaining--;
        return true;
    }

    public bool TryBuyMana()
    {
        if (!HasManaOffer || !SalesCurrency.TrySpend(manaOffer.PricePerUnit))
        {
            return false;
        }

        ManaBank.AddDirect(manaOffer.Grade, 1);
        manaOffer.Remaining--;
        return true;
    }

    // Sell = player's quick-sell buttons (BlackMarketUI). Cheapest-grade-first so crafting stock
    // isn't touched (OreBank/ManaBank.TrySpendCheapest).
    public bool TryQuickSellOre(out int goldEarned)
    {
        goldEarned = 0;
        if (!OreBank.TrySpendCheapest(QuickSellOreAmount, out OreGrade grade))
        {
            return false;
        }

        // Rounds the *total*, not the per-unit price, so a cheap material (e.g. Iron: 2 x 0.5 = 1
        // per unit, rounds fine either way, but this avoids the same rounding landing on 0 for an
        // even cheaper future material) never comes out to 0 gold for a real sale.
        goldEarned = Mathf.RoundToInt(MaterialPricing.OreValue(grade) * BuyPriceMultiplier * QuickSellOreAmount);
        SalesCurrency.Add(goldEarned);
        return true;
    }

    public bool TryQuickSellMana(out int goldEarned)
    {
        goldEarned = 0;
        if (!ManaBank.TrySpendCheapest(QuickSellManaAmount, out ManaGrade grade))
        {
            return false;
        }

        goldEarned = Mathf.RoundToInt(MaterialPricing.ManaValue(grade) * BuyPriceMultiplier * QuickSellManaAmount);
        SalesCurrency.Add(goldEarned);
        return true;
    }

    public bool TryQuickSellWood(out int goldEarned)
    {
        goldEarned = 0;
        if (!ResourceBank.TrySpend(ResourceType.Wood, QuickSellWoodAmount))
        {
            return false;
        }

        goldEarned = Mathf.RoundToInt(MaterialPricing.WoodValue * BuyPriceMultiplier * QuickSellWoodAmount);
        SalesCurrency.Add(goldEarned);
        return true;
    }
}
