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
    [SerializeField] private float pickupRadius = 2f;
    [SerializeField] private float stackItemHeight = 0.16f;
    [SerializeField] private float pickupIntervalStart = 0.32f;
    [SerializeField] private float pickupIntervalFloor = 0.06f;
    [SerializeField] private float pickupIntervalAcceleration = 0.82f;

    private readonly List<Transform> heldItems = new List<Transform>();
    private float pickupTimer;
    private float currentInterval;
    private CarryStack playerCarryStack;

    public static WeaponRack CreateAt(Vector3 worldPosition, Transform parent)
    {
        var go = new GameObject("WeaponRack");
        go.transform.SetParent(parent, true);
        go.transform.position = worldPosition;

        var rack = go.AddComponent<WeaponRack>();
        InteractionPadIndicator.Attach(go.transform, rack.pickupRadius);
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

        float sqrDist = (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude;
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
