using System;
using System.Collections;
using UnityEngine;

// A monster's ranged attack - a small tinted sphere that flies in a straight line from where it
// was fired toward a fixed point (the player's position at the moment of firing, not a homing
// target - matches this project's placeholder-level combat fidelity elsewhere) and deals damage on
// arrival. Runtime-built, no prefab (same convention as Monster/HitEffects' spark bursts).
// See monster_variety_design_v1.html §3.
public class Projectile : MonoBehaviour
{
    private const float Speed = 9f;
    private const float ArriveThreshold = 0.35f;
    private const float MaxLifetime = 3f; // safety net if the target point is somehow never reached

    public static void Fire(Vector3 startPosition, Vector3 targetPosition, float damage, Action<Vector3> onHit, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Projectile";
        go.transform.position = startPosition;
        go.transform.localScale = Vector3.one * 0.3f;
        UnityEngine.Object.Destroy(go.GetComponent<Collider>()); // visual only - arrival is a plain distance check

        Renderer renderer = go.GetComponent<Renderer>();
        renderer.material.color = color;

        Projectile projectile = go.AddComponent<Projectile>();
        projectile.StartCoroutine(projectile.FlyRoutine(startPosition, targetPosition, damage, onHit));
    }

    private IEnumerator FlyRoutine(Vector3 start, Vector3 target, float damage, Action<Vector3> onHit)
    {
        float elapsed = 0f;

        while (elapsed < MaxLifetime)
        {
            elapsed += Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, target, Speed * Time.deltaTime);

            if ((transform.position - target).sqrMagnitude <= ArriveThreshold * ArriveThreshold)
            {
                break;
            }

            yield return null;
        }

        if (PlayerHealth.TakeDamage(damage))
        {
            HitEffects.Instance.MonsterHitPlayer(target);
        }

        onHit?.Invoke(target);
        Destroy(gameObject);
    }
}
