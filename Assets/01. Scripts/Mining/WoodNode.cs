using System.Collections;
using UnityEngine;

public class WoodNode : MonoBehaviour
{
    [SerializeField] private int hitsToFell = 5;
    [SerializeField] private int woodReward = 5;
    [SerializeField] private float chopAbandonTimeout = 5f;
    [SerializeField] private float respawnDelay = 6f;
    [SerializeField] private Transform visual;

    private Collider triggerCollider;
    private int currentHits;
    private float lastHitTime;
    private bool isAvailable = true;

    public bool IsAvailable => isAvailable;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        if (isAvailable && currentHits > 0 && currentHits < hitsToFell
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

        if (currentHits < hitsToFell)
        {
            return true;
        }

        woodAmount = woodReward;
        Fell();
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
        isAvailable = true;
        SetVisible(true);
    }
}
