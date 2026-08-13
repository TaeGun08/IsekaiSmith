using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Drives the physical Customer instances for OrderQueueManager's slots - a pure presentation
// layer that polls manager.Slots each frame (same pattern the old SalesCounterUI used) and diffs
// against its own last-known customer array: order appeared -> spawn + walk in, order gone ->
// walk the existing customer out and let it self-despawn. Runs regardless of player distance -
// customers should keep arriving/waiting/leaving on their own even while the player is off
// gathering, not just pop into existence the moment the player walks up to the counter
// (playtest feedback). OrderQueueManager has no reference back to this class and doesn't know
// customers exist - it only ever sees slot indices. See customer_order_design_v4.html.
[RequireComponent(typeof(OrderQueueManager))]
public class CustomerVisualManager : MonoBehaviour
{
    [SerializeField] private Transform[] queuePoints;
    [SerializeField] private Transform spawnPoint;

    private OrderQueueManager manager;
    private Customer[] customers;
    private int[] trackedOrderIds;
    private const int NoOrder = -1;

    private TMP_Text rushLabel;

    private void Awake()
    {
        manager = GetComponent<OrderQueueManager>();
        int slotCount = queuePoints != null ? queuePoints.Length : 0;
        customers = new Customer[slotCount];
        trackedOrderIds = new int[slotCount];
        for (int i = 0; i < trackedOrderIds.Length; i++)
        {
            trackedOrderIds[i] = NoOrder;
        }

        BuildRushLabel();
        BuildCounterVisual();
        BuildApproachPath();
    }

    // SalesCounter had no visual at all - just an empty GameObject holding scripts (playtest
    // feedback). A single tinted box is enough at this project's current art bar (matches the
    // Player capsule / crate-box level of fidelity elsewhere) - not a real prop, just something
    // to stand at. Built here instead of a scene prefab so it needs no live scene wiring.
    private void BuildCounterVisual()
    {
        var counter = GameObject.CreatePrimitive(PrimitiveType.Cube);
        counter.name = "CounterVisual";
        counter.transform.SetParent(transform, false);
        counter.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        counter.transform.localScale = new Vector3(3.2f, 1f, 0.9f);
        counter.GetComponent<MeshRenderer>().material.color = new Color(0.45f, 0.32f, 0.2f);
    }

    // A flat tinted strip from the spawn point to the counter so the queue reads as "customers
    // walk in along this" rather than popping up on bare grass (playtest feedback asked for a
    // path). Not terrain texture painting (that needs an editor-time alphamap pass, out of scope
    // here) - just a simple ground-level plane, consistent with this class's code-only approach.
    private void BuildApproachPath()
    {
        if (spawnPoint == null)
        {
            return;
        }

        var path = GameObject.CreatePrimitive(PrimitiveType.Cube);
        path.name = "ApproachPathVisual";
        path.transform.SetParent(transform, false);
        Destroy(path.GetComponent<Collider>()); // decorative ground strip - must not block movement

        float halfLength = Mathf.Abs(spawnPoint.localPosition.z) / 2f + 0.2f;
        path.transform.localPosition = new Vector3(0f, 0.02f, spawnPoint.localPosition.z / 2f);
        path.transform.localScale = new Vector3(3.4f, 0.05f, halfLength * 2f);
        path.GetComponent<MeshRenderer>().material.color = new Color(0.62f, 0.52f, 0.36f);
    }

    private void BuildRushLabel()
    {
        var canvasGO = new GameObject("RushLabelCanvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        canvasGO.transform.localPosition = new Vector3(0f, 2.6f, 0f);
        canvasGO.transform.localScale = Vector3.one * 0.01f;
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var rect = canvasGO.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(400f, 60f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(canvasGO.transform, false);
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        rushLabel = textGO.AddComponent<TextMeshProUGUI>();
        rushLabel.text = "RUSH HOUR";
        rushLabel.fontSize = 40;
        rushLabel.fontStyle = FontStyles.Bold;
        rushLabel.alignment = TextAlignmentOptions.Center;
        rushLabel.color = new Color(1f, 0.55f, 0.25f);
        canvasGO.SetActive(false);
    }

    private void Update()
    {
        if (spawnPoint == null || queuePoints == null || queuePoints.Length == 0)
        {
            return; // not wired up in the Inspector yet
        }

        rushLabel.transform.parent.gameObject.SetActive(manager.InRush);
        if (rushLabel.transform.parent.gameObject.activeSelf && Camera.main != null)
        {
            rushLabel.transform.parent.rotation = Camera.main.transform.rotation;
        }

        IReadOnlyList<CustomerOrder> slots = manager.Slots;
        int count = Mathf.Min(slots.Count, customers.Length);

        for (int i = 0; i < count; i++)
        {
            CustomerOrder order = slots[i];

            if (order == null)
            {
                if (customers[i] != null)
                {
                    customers[i].ExitAndDespawn(spawnPoint.position);
                    customers[i] = null;
                    trackedOrderIds[i] = NoOrder;
                }

                continue;
            }

            if (customers[i] == null || trackedOrderIds[i] != order.Id)
            {
                int slotIndex = i;
                Color tint = Color.HSVToRGB((order.Id * 0.618033f) % 1f, 0.55f, 0.85f);
                customers[i] = Customer.Spawn(spawnPoint.position, tint, () => manager.TryFulfill(slotIndex), transform);
                customers[i].WalkTo(queuePoints[i].position);
                trackedOrderIds[i] = order.Id;
            }

            customers[i].SetOrder(order.MinGrade, order.Patience01);
        }
    }
}
