using System.Collections.Generic;
using UnityEngine;

// Staging area for freshly QUICK CRAFT'd weapons - see weapon_rack_and_order_polish_v1.html §2.
// Previously CraftingStation flew a finished weapon straight onto the player's back the instant it
// was done; the player asked for a visible separation instead: the weapon piles up here first, and
// only walking into this rack's pickup radius transfers it onto the player's CarryStack (same
// conveyor-belt pacing StorageDepot/OrderQueueManager already use elsewhere). Created at runtime by
// CraftingStation, same "satellite object" pattern InteractionPadIndicator already uses - no prefab
// editing required.
public class WeaponRack : MonoBehaviour
{
    [SerializeField] private float stackItemHeight = 0.16f;
    [SerializeField] private float pickupIntervalStart = 0.32f;
    [SerializeField] private float pickupIntervalFloor = 0.06f;
    [SerializeField] private float pickupIntervalAcceleration = 0.82f;

    private readonly List<Transform> heldItems = new List<Transform>();
    private float pickupTimer;
    private float currentInterval;
    private CarryStack playerCarryStack;

    // Pickup eligibility is checked against pickupCenter/pickupRadius, NOT this rack's own
    // transform - the rack sits visually offset to the side of the furnace (so finished weapons
    // are easy to see), but if pickup range were centered on that same offset point with an
    // arbitrary small radius, standing on the *far* side of the furnace to craft (e.g. at the
    // anvil) could put the player outside it - weapons would pile up here forever, never reaching
    // the counter (버그 리포트 2026-08-24). Centering on the furnace itself with the exact same
    // radius CraftingStation already uses for its own interact check guarantees "anywhere you
    // could have just crafted, you can also pick up" by construction, regardless of how far to the
    // side the rack's visual spot ends up.
    private Transform pickupCenter;
    private float pickupRadius;

    public static WeaponRack CreateAt(Vector3 worldPosition, Transform parent, Transform pickupCenter, float pickupRadius)
    {
        var go = new GameObject("WeaponRack");
        go.transform.SetParent(parent, true);
        go.transform.position = worldPosition;

        var rack = go.AddComponent<WeaponRack>();
        rack.pickupCenter = pickupCenter != null ? pickupCenter : go.transform;
        rack.pickupRadius = pickupRadius;
        // No separate ground indicator here - CraftingStation already draws one for this exact
        // same center/radius (its own interactRadius), so a second identical circle would just be
        // a redundant overlay.
        return rack;
    }

    private void Awake()
    {
        currentInterval = pickupIntervalStart;
    }

    // Called by CraftingStation once per unit produced - spawns a real prop that sits visibly on
    // the rack (not on the player) until someone walks up and picks it up.
    public void AddWeapon()
    {
        GameObject instance = GameObjectPool.Instance.Spawn(CarryItemTemplates.QuickCraftWeaponProp, transform.position, Quaternion.identity);
        instance.transform.SetParent(transform, false);
        instance.transform.localPosition = new Vector3(0f, heldItems.Count * stackItemHeight, 0f);
        instance.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // matches CarryStack's own "laid down" weapon rotation
        heldItems.Add(instance.transform);
    }

    private void Update()
    {
        if (heldItems.Count == 0 || PlayerMotor.Instance == null)
        {
            ResetPacing();
            return;
        }

        float sqrDist = (PlayerMotor.Instance.transform.position - pickupCenter.position).sqrMagnitude;
        if (sqrDist > pickupRadius * pickupRadius)
        {
            ResetPacing();
            return;
        }

        pickupTimer -= Time.deltaTime;
        if (pickupTimer > 0f)
        {
            return;
        }

        if (playerCarryStack == null)
        {
            playerCarryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
            if (playerCarryStack == null)
            {
                return;
            }
        }

        if (playerCarryStack.IsFull(CarryLayer.Weapon))
        {
            pickupTimer = pickupIntervalStart;
            return;
        }

        int lastIndex = heldItems.Count - 1;
        Transform item = heldItems[lastIndex];
        heldItems.RemoveAt(lastIndex);

        if (!playerCarryStack.TryReceiveExisting(item, CarryLayer.Weapon))
        {
            heldItems.Add(item); // shouldn't normally happen right after the IsFull check above, but stay consistent if it does
            pickupTimer = pickupIntervalStart;
            return;
        }

        pickupTimer = currentInterval;
        currentInterval = Mathf.Max(pickupIntervalFloor, currentInterval * pickupIntervalAcceleration);
    }

    private void ResetPacing()
    {
        pickupTimer = 0f;
        currentInterval = pickupIntervalStart;
    }
}
