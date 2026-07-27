using System.Collections;
using UnityEngine;

public class OreNode : MonoBehaviour
{
    [SerializeField] private int hitsToBreak = 5;
    [SerializeField] private int minOreReward = 2;
    [SerializeField] private int maxOreReward = 3;
    [SerializeField] private float mineAbandonTimeout = 5f;
    [SerializeField] private float respawnDelay = 4f;
    [SerializeField] private float hitShakeAmplitude = 0.22f;
    [SerializeField] private float hitShakeFrequency = 40f;
    [SerializeField] private float hitShakeDuration = 0.22f;
    [SerializeField] private Transform visual;
    [SerializeField] private GameObject fragmentPrefab;
    [SerializeField] private int hitChipCount = 2;
    [SerializeField] private float hitChipSpeed = 1.5f;
    [SerializeField] private int breakFragmentCount = 7;
    [SerializeField] private float breakFragmentSpeed = 3f;
    [SerializeField] private float fragmentLifetime = 0.5f;
    [SerializeField] private float breakPunchScale = 1.18f;
    [SerializeField] private float breakPunchDuration = 0.08f;

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
            SpawnFragments(hitChipCount, hitChipSpeed);
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
            float offsetX = Mathf.Sin(t * hitShakeFrequency) * hitShakeAmplitude * damper;
            float offsetZ = Mathf.Sin(t * hitShakeFrequency * 1.3f + 1.57f) * hitShakeAmplitude * 0.6f * damper;
            visual.localPosition = visualRestPosition + new Vector3(offsetX, 0f, offsetZ);
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

        SpawnFragments(breakFragmentCount, breakFragmentSpeed);
        StartCoroutine(BreakPunchThenHide());
        StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator BreakPunchThenHide()
    {
        if (visual != null)
        {
            Vector3 baseScale = visual.localScale;
            Vector3 punchedScale = baseScale * breakPunchScale;

            float elapsed = 0f;
            while (elapsed < breakPunchDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / breakPunchDuration);
                visual.localScale = Vector3.Lerp(baseScale, punchedScale, t);
                yield return null;
            }

            visual.localScale = baseScale;
        }

        SetVisible(false);
    }

    private void SpawnFragments(int count, float speed)
    {
        if (fragmentPrefab == null || visual == null)
        {
            return;
        }

        Vector3 origin = visual.position;

        for (int i = 0; i < count; i++)
        {
            GameObject fragment = Instantiate(fragmentPrefab, origin, Random.rotation);

            Vector3 direction = Random.insideUnitSphere;
            direction.y = Mathf.Abs(direction.y) + 0.3f;
            direction.Normalize();

            StartCoroutine(FragmentRoutine(fragment.transform, direction, speed));
        }
    }

    private IEnumerator FragmentRoutine(Transform fragment, Vector3 direction, float speed)
    {
        Vector3 startPosition = fragment.position;
        Vector3 startScale = fragment.localScale;
        float elapsed = 0f;

        while (elapsed < fragmentLifetime && fragment != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fragmentLifetime);

            Vector3 travel = direction * speed * t;
            travel.y -= 4f * t * t;
            fragment.position = startPosition + travel;
            fragment.localScale = startScale * (1f - t);
            fragment.Rotate(direction * 360f * Time.deltaTime, Space.World);

            yield return null;
        }

        if (fragment != null)
        {
            Destroy(fragment.gameObject);
        }
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
