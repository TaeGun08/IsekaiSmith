using System.Collections;
using UnityEngine;

public class OreNode : MonoBehaviour
{
    [SerializeField] private int hitsToBreak = 5;
    [SerializeField] private int minOreReward = 2;
    [SerializeField] private int maxOreReward = 3;
    [SerializeField] private float mineAbandonTimeout = 5f;
    [SerializeField] private float respawnDelay = 4f;
    [SerializeField] private float hitShakeAmplitude = 0.06f;
    [SerializeField] private float hitShakeFrequency = 30f;
    [SerializeField] private float hitShakeDuration = 0.2f;
    [SerializeField] private Transform visual;

    private Collider triggerCollider;
    private Vector3 visualRestPosition;
    private int currentHits;
    private float lastHitTime;
    private bool isAvailable = true;
    private Coroutine hitShakeRoutine;

    public bool IsAvailable => isAvailable;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();

        if (visual != null)
        {
            visualRestPosition = visual.localPosition;
        }
    }

    private void Update()
    {
        if (isAvailable && currentHits > 0 && currentHits < hitsToBreak
            && Time.time - lastHitTime > mineAbandonTimeout)
        {
            currentHits = 0;
        }
    }

    public bool TryMine(out int oreAmount)
    {
        oreAmount = 0;

        if (!isAvailable)
        {
            return false;
        }

        currentHits++;
        lastHitTime = Time.time;

        if (currentHits < hitsToBreak)
        {
            PlayHitShake();
            return true;
        }

        oreAmount = Random.Range(minOreReward, maxOreReward + 1);
        Break();
        return true;
    }

    private void PlayHitShake()
    {
        if (visual == null)
        {
            return;
        }

        if (hitShakeRoutine != null)
        {
            StopCoroutine(hitShakeRoutine);
        }

        hitShakeRoutine = StartCoroutine(HitShakeRoutine());
    }

    private IEnumerator HitShakeRoutine()
    {
        float elapsed = 0f;

        while (elapsed < hitShakeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / hitShakeDuration);
            float damper = 1f - t;
            float offset = Mathf.Sin(t * hitShakeFrequency) * hitShakeAmplitude * damper;
            visual.localPosition = visualRestPosition + new Vector3(offset, 0f, 0f);
            yield return null;
        }

        visual.localPosition = visualRestPosition;
        hitShakeRoutine = null;
    }

    private void Break()
    {
        isAvailable = false;
        currentHits = 0;

        if (hitShakeRoutine != null)
        {
            StopCoroutine(hitShakeRoutine);
            hitShakeRoutine = null;
        }

        if (visual != null)
        {
            visual.localPosition = visualRestPosition;
        }

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
