using System.Collections;
using UnityEngine;

public class WoodNode : MonoBehaviour
{
    [SerializeField] private int hitsToFell = 5;
    [SerializeField] private int minWoodReward = 2;
    [SerializeField] private int maxWoodReward = 3;
    [SerializeField] private float chopAbandonTimeout = 5f;
    [SerializeField] private float respawnDelay = 6f;
    [SerializeField] private float respawnJitterRadius = 1.5f;
    [SerializeField] private float hitShakeAngle = 10f;
    [SerializeField] private float hitShakeDuration = 0.15f;
    [SerializeField] private float fallAngle = 85f;
    [SerializeField] private float fallDuration = 0.7f;
    [SerializeField] private Transform visual;
    [SerializeField] private Transform stump;

    private Collider triggerCollider;
    private Vector3 spawnPosition;
    private int currentHits;
    private float lastHitTime;
    private bool isAvailable = true;
    private Coroutine hitShakeRoutine;

    public bool IsAvailable => isAvailable;

    private void Awake()
    {
        triggerCollider = GetComponent<Collider>();
        spawnPosition = transform.position;

        if (stump != null)
        {
            stump.gameObject.SetActive(false);
        }
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
            PlayHitShake();
            return true;
        }

        woodAmount = Random.Range(minWoodReward, maxWoodReward + 1);
        Fell();
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
        Quaternion tilted = Quaternion.AngleAxis(hitShakeAngle, GetFallAxis());

        float elapsed = 0f;
        while (elapsed < hitShakeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Sin(Mathf.Clamp01(elapsed / hitShakeDuration) * Mathf.PI);
            visual.localRotation = Quaternion.Slerp(Quaternion.identity, tilted, t);
            yield return null;
        }

        visual.localRotation = Quaternion.identity;
        hitShakeRoutine = null;
    }

    private void Fell()
    {
        isAvailable = false;
        currentHits = 0;

        if (hitShakeRoutine != null)
        {
            StopCoroutine(hitShakeRoutine);
            hitShakeRoutine = null;
        }

        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }

        StartCoroutine(FallAndRespawnRoutine());
    }

    private IEnumerator FallAndRespawnRoutine()
    {
        if (visual != null)
        {
            Quaternion fallenRotation = Quaternion.AngleAxis(fallAngle, GetFallAxis());

            float elapsed = 0f;
            while (elapsed < fallDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / fallDuration);
                t *= t;
                visual.localRotation = Quaternion.Slerp(Quaternion.identity, fallenRotation, t);
                yield return null;
            }

            visual.gameObject.SetActive(false);
            visual.localRotation = Quaternion.identity;
        }

        if (stump != null)
        {
            stump.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(respawnDelay);

        if (stump != null)
        {
            stump.gameObject.SetActive(false);
        }

        Vector2 offset = Random.insideUnitCircle * respawnJitterRadius;
        transform.position = spawnPosition + new Vector3(offset.x, 0f, offset.y);

        if (visual != null)
        {
            visual.gameObject.SetActive(true);
        }

        if (triggerCollider != null)
        {
            triggerCollider.enabled = true;
        }

        isAvailable = true;
    }

    private Vector3 GetFallAxis()
    {
        Vector3 awayDirection = transform.forward;

        if (PlayerMotor.Instance != null)
        {
            Vector3 diff = transform.position - PlayerMotor.Instance.transform.position;
            diff.y = 0f;

            if (diff.sqrMagnitude > 0.0001f)
            {
                awayDirection = diff.normalized;
            }
        }

        return Vector3.Cross(Vector3.up, awayDirection).normalized;
    }
}
