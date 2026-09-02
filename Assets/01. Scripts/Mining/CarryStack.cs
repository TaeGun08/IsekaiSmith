using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum CarryLayer
{
    Ore,
    Wood,
    ManaStone,
    Weapon
}

public class CarryStack : MonoBehaviour
{
    // Keep in sync with CarryLayer's member count - see LocalSlotPosition/Awake.
    private const int LayerCount = 4;

    [SerializeField] private Transform stackAnchor;
    [SerializeField] private int oreCapacity = 8;
    [SerializeField] private int woodCapacity = 8;
    // Starting capacity is intentionally small - field monster mana drops are meant to be a
    // trickle, not a farm. Also the first field meant to be raised later by a gold-cost carry
    // capacity upgrade (not built yet - see combat_design_v1.html follow-up notes).
    [SerializeField] private int manaCapacity = 6;
    // QUICK CRAFT output (customer_order_design_v7.html §2/§4) - carried from the smithy to the
    // sales counter instead of teleporting straight into ToolInventory. Small on purpose, same
    // rationale as manaCapacity: keeps the carry loop as a steady trickle of trips, not a single
    // dump-everything haul.
    [SerializeField] private int weaponCapacity = 6;
    [SerializeField] private float itemHeight = 0.5f;
    [SerializeField] private float woodItemHeight = 0.4f;
    [SerializeField] private float manaItemHeight = 0.22f;
    [SerializeField] private float weaponItemHeight = 0.3f;
    [SerializeField] private float woodBackOffset = 0.35f;
    [SerializeField] private float manaSideOffset = 0.4f;
    // Opposite side from manaSideOffset - keeps every layer's pile visually separate at a glance.
    [SerializeField] private float weaponSideOffset = 0.4f;
    [SerializeField] private float swayAmplitude = 6f;
    [SerializeField] private float swaySpeed = 6f;
    [SerializeField] private float flightDuration = 0.4f;
    [SerializeField] private float flightArcHeight = 1.5f;
    [SerializeField] private float dropDuration = 0.3f;
    [SerializeField] private float dropArcHeight = 0.4f;
    [SerializeField] private float dropScatterRadius = 0.6f;
    [SerializeField] private float pickupDelay = 0.5f;
    [SerializeField] private float depositFlightDuration = 0.35f;
    [SerializeField] private float depositArcHeight = 1f;
    [SerializeField] private float depositStagger = 0.08f;

    // Indexed by (int)CarryLayer - one array instead of a field pair per layer, so a future 4th
    // carryable layer only needs a new capacity field + LayerCount bump, not new branches
    // scattered through every method here.
    private readonly List<Transform>[] itemsByLayer = new List<Transform>[LayerCount];
    private readonly int[] reservedByLayer = new int[LayerCount];
    private int[] capacities;
    private Rigidbody body;

    private void Awake()
    {
        body = GetComponentInParent<Rigidbody>();
        capacities = new[] { oreCapacity, woodCapacity, manaCapacity, weaponCapacity };

        for (int i = 0; i < LayerCount; i++)
        {
            itemsByLayer[i] = new List<Transform>();
        }
    }

    public bool IsFull(CarryLayer layer)
    {
        return reservedByLayer[(int)layer] >= capacities[(int)layer];
    }

    // Hides (or restores) everything currently stacked on the player's back without discarding
    // any of it - used by StageSceneController/DungeonSceneController while a stage/dungeon
    // encounter is active, since combat only ever reads EquippedWeapon (independent of what's
    // physically carried) and there's no reason for raw materials/unsold weapons to visibly ride
    // along into a monster fight (사용자 요청 2026-08-24: "장비 데이터 관련해서만... 등에 뭐가
    // 쌓여 있으면 같이 넘어가지잖아"). In-flight items (still mid-FlyToStack, not yet parented to
    // stackAnchor) are unaffected either way - they finish landing normally.
    public void SetVisible(bool visible)
    {
        if (stackAnchor != null)
        {
            stackAnchor.gameObject.SetActive(visible);
        }
    }

    public int GetCount(CarryLayer layer)
    {
        return reservedByLayer[(int)layer];
    }

    public bool TryAdd(GameObject itemPrefab, Vector3 worldStartPosition, CarryLayer layer)
    {
        if (itemPrefab == null || stackAnchor == null || IsFull(layer))
        {
            return false;
        }

        int index = reservedByLayer[(int)layer];
        reservedByLayer[(int)layer]++;

        Vector3 targetLocalPosition = LocalSlotPosition(layer, index);

        GameObject instance = GameObjectPool.Instance.Spawn(itemPrefab, worldStartPosition, Quaternion.identity);
        StartCoroutine(FlyToStack(instance.transform, worldStartPosition, targetLocalPosition, layer));
        return true;
    }

    // Same end result as TryAdd, but for an item that already exists in the world (e.g. a weapon
    // sitting on WeaponRack's shelf) instead of spawning a fresh instance - skips the drop/scatter
    // and pickup-delay phases (the item is already "resting" somewhere, not freshly knocked loose),
    // going straight into the same arc-flight-to-stack finish TryAdd uses.
    public bool TryReceiveExisting(Transform existingItem, CarryLayer layer)
    {
        if (existingItem == null || stackAnchor == null || IsFull(layer))
        {
            return false;
        }

        int index = reservedByLayer[(int)layer];
        reservedByLayer[(int)layer]++;

        Vector3 targetLocalPosition = LocalSlotPosition(layer, index);
        existingItem.SetParent(null, true);
        StartCoroutine(FlyArcToStack(existingItem, existingItem.position, targetLocalPosition, layer));
        return true;
    }

    // Each layer gets its own stacking column so piles never visually overlap: ore stacks
    // straight up at the anchor, wood stacks up behind it, mana shards stack up to the side.
    private Vector3 LocalSlotPosition(CarryLayer layer, int index)
    {
        switch (layer)
        {
            case CarryLayer.Wood:
                return new Vector3(0f, index * woodItemHeight, -woodBackOffset);
            case CarryLayer.ManaStone:
                return new Vector3(manaSideOffset, index * manaItemHeight, 0f);
            case CarryLayer.Weapon:
                return new Vector3(-weaponSideOffset, index * weaponItemHeight, 0f);
            default:
                return new Vector3(0f, index * itemHeight, 0f);
        }
    }

    // Weapons lie flat, laid crosswise over the back (blade running left-right) instead of
    // pointing straight forward/back like every other layer's default identity rotation - reads
    // clearly as "a sword laid down" rather than "a lance" (사용자 요청 2026-08-21: "손에 검을 들
    // 때 가로로 눕혀서 쌓이도록"). Every other layer keeps the plain identity rotation it already had.
    private static Quaternion LocalSlotRotation(CarryLayer layer)
    {
        return layer == CarryLayer.Weapon ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
    }

    public void Clear(CarryLayer layer)
    {
        List<Transform> items = itemsByLayer[(int)layer];

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null)
            {
                items[i].SetParent(null, true);
                GameObjectPool.Instance.Despawn(items[i].gameObject);
            }
        }

        items.Clear();
        reservedByLayer[(int)layer] = 0;
    }

    // Drops everything carried, across every layer at once - used when the player dies (user
    // request: "죽게 되면, 플레이어 등에 있는 자원들은 다 사라지게"). Loops by index instead of
    // hardcoding each CarryLayer so a future 4th layer doesn't need a matching update here too.
    public void ClearAll()
    {
        for (int i = 0; i < LayerCount; i++)
        {
            Clear((CarryLayer)i);
        }
    }

    // Pulls exactly the top (most recently stacked) item and flies it to targetPosition, instead
    // of Deposit()'s "transfer the whole pile in one instant" - lets a depot pace deposits out one
    // at a time (see StorageDepot/OrderQueueManager) so the conveyor-belt effect actually reads as
    // individual items leaving, not the whole stack teleporting away the moment the count updates.
    // Removes from the end of the list (the most recently added, i.e. visually topmost item) so
    // every remaining item keeps the exact slot index - and therefore position - it already had;
    // no repositioning pass needed. Returns false if the layer is already empty.
    public bool TryDepositOne(CarryLayer layer, Vector3 targetPosition)
    {
        List<Transform> items = itemsByLayer[(int)layer];
        if (items.Count == 0)
        {
            return false;
        }

        int lastIndex = items.Count - 1;
        Transform item = items[lastIndex];
        items.RemoveAt(lastIndex);
        reservedByLayer[(int)layer]--;

        if (item != null)
        {
            item.SetParent(null, true);
            StartCoroutine(DepositFlightRoutine(item, targetPosition, 0f));
        }

        return true;
    }

    public void Deposit(CarryLayer layer, Vector3 targetPosition)
    {
        List<Transform> items = itemsByLayer[(int)layer];

        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];

            if (item == null)
            {
                continue;
            }

            item.SetParent(null, true);
            StartCoroutine(DepositFlightRoutine(item, targetPosition, i * depositStagger));
        }

        items.Clear();
        reservedByLayer[(int)layer] = 0;
    }

    private IEnumerator DepositFlightRoutine(Transform item, Vector3 targetPosition, float delay)
    {
        if (delay > 0f)
        {
            yield return new WaitForSeconds(delay);
        }

        if (item == null)
        {
            yield break;
        }

        Vector3 startPosition = item.position;
        float elapsed = 0f;

        // No shrink-while-flying-in anymore (사용자 요청 2026-08-24: "점점 작아지는 연출은 없어도
        // 될 것 같아") - item stays full size for the whole arc and just despawns on arrival.
        while (elapsed < depositFlightDuration && item != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / depositFlightDuration);

            Vector3 flatPosition = Vector3.Lerp(startPosition, targetPosition, t);
            float arc = depositArcHeight * Mathf.Sin(t * Mathf.PI);

            item.position = flatPosition + Vector3.up * arc;
            item.Rotate(Vector3.up, 540f * Time.deltaTime, Space.World);

            yield return null;
        }

        if (item != null)
        {
            GameObjectPool.Instance.Despawn(item.gameObject);
        }
    }

    private IEnumerator FlyToStack(Transform item, Vector3 startWorldPosition, Vector3 targetLocalPosition, CarryLayer layer)
    {
        Vector2 scatter = Random.insideUnitCircle * dropScatterRadius;
        Vector3 groundPosition = startWorldPosition + new Vector3(scatter.x, 0f, scatter.y);

        float dropElapsed = 0f;
        while (dropElapsed < dropDuration && item != null)
        {
            dropElapsed += Time.deltaTime;
            float dropT = Mathf.Clamp01(dropElapsed / dropDuration);
            Vector3 dropPosition = Vector3.Lerp(startWorldPosition, groundPosition, dropT);
            dropPosition.y += dropArcHeight * Mathf.Sin(dropT * Mathf.PI);
            item.position = dropPosition;
            yield return null;
        }

        if (item == null)
        {
            yield break;
        }

        item.position = groundPosition;

        yield return new WaitForSeconds(pickupDelay);

        if (item == null)
        {
            yield break;
        }

        yield return FlyArcToStack(item, groundPosition, targetLocalPosition, layer);
    }

    // Shared tail of both pickup paths (freshly spawned via TryAdd, or an existing item handed over
    // by TryReceiveExisting) - arcs from startWorldPosition to the target stack slot and parents it
    // there on arrival.
    private IEnumerator FlyArcToStack(Transform item, Vector3 startWorldPosition, Vector3 targetLocalPosition, CarryLayer layer)
    {
        float elapsed = 0f;

        while (elapsed < flightDuration && item != null && stackAnchor != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flightDuration);

            Vector3 targetWorldPosition = stackAnchor.TransformPoint(targetLocalPosition);
            Vector3 flatPosition = Vector3.Lerp(startWorldPosition, targetWorldPosition, t);
            float arc = flightArcHeight * Mathf.Sin(t * Mathf.PI);

            item.position = flatPosition + Vector3.up * arc;
            item.Rotate(Vector3.up, 480f * Time.deltaTime, Space.World);

            yield return null;
        }

        if (item == null)
        {
            yield break;
        }

        if (stackAnchor == null)
        {
            GameObjectPool.Instance.Despawn(item.gameObject);
            yield break;
        }

        item.SetParent(stackAnchor, false);
        item.localPosition = targetLocalPosition;
        item.localRotation = LocalSlotRotation(layer);

        itemsByLayer[(int)layer].Add(item);
    }

    private void Update()
    {
        if (stackAnchor == null)
        {
            return;
        }

        float speed = body != null ? body.linearVelocity.magnitude : 0f;
        float sway = Mathf.Sin(Time.time * swaySpeed) * swayAmplitude * Mathf.Clamp01(speed / 5f);

        for (int i = 0; i < LayerCount; i++)
        {
            ApplySway(itemsByLayer[i], (CarryLayer)i, sway);
        }
    }

    // Composes the sway on top of each layer's own resting rotation (LocalSlotRotation) instead of
    // overwriting localRotation outright - previously this always wrote a Z-only Euler, which was
    // harmless while every layer rested at identity but would have silently un-rotated the Weapon
    // layer's crosswise-lie the moment the player started moving.
    private static void ApplySway(List<Transform> items, CarryLayer layer, float sway)
    {
        Quaternion rest = LocalSlotRotation(layer);

        for (int i = 0; i < items.Count; i++)
        {
            float weight = (i + 1f) / items.Count;
            items[i].localRotation = rest * Quaternion.Euler(0f, 0f, sway * weight);
        }
    }
}
