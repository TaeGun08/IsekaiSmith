using UnityEngine;

// Auto-attacks the nearest live Monster in range - same "no dedicated input, just proximity" rule
// PlayerWoodcutting/PlayerMining already use (this game has never had a combo/twitch-skill attack
// button). Self-bootstrapping singleton (GuidedTutorial-style) since there's no scene-attached
// component to wire this to. See combat_design_v1.html §4.
public class PlayerCombat : MonoBehaviour
{
    // TODO Phase 2 (game_design_doc.html §9 / combat_design_v1.html §6): once the equipped-weapon
    // system exists, replace this fixed value with a calculation based on the equipped weapon's
    // ore grade instead.
    private const float BaseDamage = 10f;
    private const float AttackInterval = 0.6f;
    private const float AttackRadius = 1.2f;

    private static PlayerCombat instance;

    public static PlayerCombat Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("PlayerCombat");
                instance = go.AddComponent<PlayerCombat>();
                DontDestroyOnLoad(go);
            }

            return instance;
        }
    }

    private float attackTimer;
    private ToolSwing playerToolSwing;

    // Referencing Instance is enough to bootstrap this singleton - kept as an explicit call at
    // the ResourceHUD.Start() call site for readability, same as GuidedTutorial's pattern.
    public void Activate()
    {
    }

    private void Update()
    {
        if (PlayerMotor.Instance == null)
        {
            return;
        }

        attackTimer -= Time.deltaTime;
        if (attackTimer > 0f)
        {
            return;
        }

        Monster target = FindNearestInRange();
        if (target == null)
        {
            return;
        }

        attackTimer = AttackInterval;
        target.TakeDamage(BaseDamage);

        if (playerToolSwing == null)
        {
            playerToolSwing = PlayerMotor.Instance.GetComponentInChildren<ToolSwing>();
        }

        // Placeholder swing (no dedicated weapon visual exists yet) - real weapon models arrive
        // with the equipped-weapon system (Phase 2).
        playerToolSwing?.PlayAxeSwing();
    }

    private Monster FindNearestInRange()
    {
        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        Monster[] candidates = FindObjectsByType<Monster>(FindObjectsSortMode.None);

        Monster nearest = null;
        float nearestSqrDist = AttackRadius * AttackRadius;

        foreach (Monster candidate in candidates)
        {
            if (!candidate.IsAvailable)
            {
                continue;
            }

            float sqrDist = (candidate.transform.position - playerPos).sqrMagnitude;
            if (sqrDist <= nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = candidate;
            }
        }

        return nearest;
    }
}
