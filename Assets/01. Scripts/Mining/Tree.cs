using System.Collections;
using UnityEngine;

public class Tree : MonoBehaviour
{
    [SerializeField] private int minHitsToFell = 3;
    [SerializeField] private int maxHitsToFell = 5;
    [SerializeField] private int woodPerHit = 1;
    [SerializeField] private int fellBonusWood = 3;
    [SerializeField] private float chopAbandonTimeout = 5f;
    [SerializeField] private float respawnDelay = 6f;
    [SerializeField] private Transform visual;

    private Collider triggerCollider;
    private int requiredHits;
    private int currentHits;
    private float lastHitTime;
    private bool isAvailable = true;

    public bool IsAvailable => isAvailable;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        requiredHits = Random.Range(minHitsToFell, maxHitsToFell + 1);
    }

    private void Update()
    {
        if (isAvailable && currentHits > 0 && currentHits < requiredHits
            && Time.time - lastHitTime > chopAbandonTimeout)
        {
            currentHits = 0;
        }
    }

    public bool TryChop(out int woodAmount)
    {
        woodAmount = 0;

        if (!isAvailable)
        {
            return false;
        }

        currentHits++;
        lastHitTime = Time.time;

        if (currentHits >= requiredHits)
        {
            woodAmount = fellBonusWood;
            Fell();
            return true;
        }

        woodAmount = woodPerHit;
        return true;
    }

    private void Fell()
    {
        isAvailable = false;
        currentHits = 0;
        SetVisible(false);
        StartCoroutine(RespawnAfterDelay());
    }

    private void SetVisible(bool visible)
    {
        if (visual != null)
        {
            visual.gameObject.SetActive(visible);
        }

        if (triggerCollider != null)
        {
            triggerCollider.enabled = visible;
        }
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        requiredHits = Random.Range(minHitsToFell, maxHitsToFell + 1);
        isAvailable = true;
        SetVisible(true);
    }
}
