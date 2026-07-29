using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new Vector3(3f, 15f, -7f);
    [SerializeField] private float followSpeed = 100f;
    [SerializeField] private float lookAtHeight = 1f;

    [Header("Hit Shake")]
    [SerializeField] private float defaultShakeAmplitude = 0.15f;
    [SerializeField] private float defaultShakeDuration = 0.12f;
    [SerializeField] private float shakeFrequency = 30f;

    private float shakeTotalDuration;
    private float shakeTimeRemaining;
    private float shakeAmplitude;
    private float noiseSeedX;
    private float noiseSeedY;

    private void Awake()
    {
        Instance = this;
        noiseSeedX = Random.Range(0f, 100f);
        noiseSeedY = Random.Range(0f, 100f);
    }

    public void Shake()
    {
        Shake(defaultShakeAmplitude, defaultShakeDuration);
    }

    public void Shake(float amplitude, float duration)
    {
        shakeAmplitude = amplitude;
        shakeTotalDuration = duration;
        shakeTimeRemaining = duration;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * lookAtHeight);

        if (shakeTimeRemaining > 0f)
        {
            shakeTimeRemaining -= Time.deltaTime;
            float fadeOut = Mathf.Clamp01(shakeTimeRemaining / shakeTotalDuration);
            float t = Time.time * shakeFrequency;
            Vector3 shakeOffset = new Vector3(
                (Mathf.PerlinNoise(t, noiseSeedX) - 0.5f) * 2f,
                (Mathf.PerlinNoise(t, noiseSeedY) - 0.5f) * 2f,
                0f) * shakeAmplitude * fadeOut;
            transform.position += shakeOffset;
        }
    }
}
