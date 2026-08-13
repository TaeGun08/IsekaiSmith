using System.Collections;
using UnityEngine;

// Orchestrates the "juice" around a player death - collapse pose, then a screen fade masks the
// teleport back to the smithy, then stands back up. Split out of PlayerHealth (which stays a
// plain static data class, matching ResourceBank/Reputation) since this needs coroutines/a
// MonoBehaviour host. Subscribes to PlayerHealth.OnDeath. Self-bootstrapping singleton
// (GuidedTutorial-style). See combat_design_v1.html follow-up: user feedback - "차라리 페이드
// 인아웃이 되면서 자연스럽게 순간이동 되는 연출이 더 좋을 것 같아... 캐릭터가 쓰러지고 난 후에".
public class PlayerDeathPresentation : MonoBehaviour
{
    private const float CollapseDuration = 0.35f;
    private const float CollapseAngle = 85f;

    private static PlayerDeathPresentation instance;

    public static PlayerDeathPresentation Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("PlayerDeathPresentation");
                instance = go.AddComponent<PlayerDeathPresentation>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    // Referencing Instance is enough to subscribe (via OnEnable) - kept as an explicit call at
    // the ResourceHUD.Start() call site for readability, same as GuidedTutorial's pattern.
    public void Activate()
    {
    }

    private void OnEnable()
    {
        PlayerHealth.OnDeath += HandleDeath;
    }

    private void OnDisable()
    {
        PlayerHealth.OnDeath -= HandleDeath;
    }

    private bool sequenceRunning;

    private void HandleDeath()
    {
        // Guards against a second OnDeath firing while a sequence is already mid-flight (the
        // 1.5s invulnerability window should already prevent that in practice, but overlapping
        // coroutines would otherwise fight over the same model.localRotation and glitch visibly).
        if (sequenceRunning)
        {
            return;
        }

        StartCoroutine(DeathSequence());
    }

    private IEnumerator DeathSequence()
    {
        sequenceRunning = true;

        if (PlayerMotor.Instance == null)
        {
            sequenceRunning = false;
            yield break;
        }

        Transform model = PlayerMotor.Instance.transform.Find("Model");

        if (model != null)
        {
            yield return Collapse(model);
        }

        bool faded = false;
        ScreenFade.Instance.FadeOutAndIn(TeleportToSmithy, () => faded = true);
        yield return new WaitUntil(() => faded);

        if (model != null)
        {
            model.localRotation = Quaternion.identity;
        }

        sequenceRunning = false;
    }

    private static IEnumerator Collapse(Transform model)
    {
        Quaternion start = model.localRotation;
        Quaternion collapsed = start * Quaternion.Euler(CollapseAngle, 0f, 0f);
        float elapsed = 0f;

        while (elapsed < CollapseDuration)
        {
            elapsed += Time.deltaTime;
            model.localRotation = Quaternion.Slerp(start, collapsed, Mathf.Clamp01(elapsed / CollapseDuration));
            yield return null;
        }

        model.localRotation = collapsed;
    }

    private static void TeleportToSmithy()
    {
        if (PlayerMotor.Instance == null)
        {
            return;
        }

        GameObject counterGO = GameObject.Find("SalesCounter");
        Vector3 respawnPoint = counterGO != null ? counterGO.transform.position : PlayerMotor.Instance.transform.position;
        PlayerMotor.Instance.transform.position = respawnPoint;
    }
}
