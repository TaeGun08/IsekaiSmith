using System.Collections;
using UnityEngine;

public class OreNode : MonoBehaviour
{
    [SerializeField] private float respawnDelay = 4f;
    [SerializeField] private Transform visual;

    private Collider triggerCollider;
    private bool isAvailable = true;

    public bool IsAvailable => isAvailable;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
    }

    public bool TryCollect()
    {
        if (!isAvailable)
        {
            return false;
        }

        isAvailable = false;
        SetVisible(false);
        StartCoroutine(RespawnAfterDelay());
        return true;
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
