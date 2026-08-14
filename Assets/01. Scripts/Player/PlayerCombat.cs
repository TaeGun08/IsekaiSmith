using System.Collections.Generic;
using UnityEngine;

// Auto-attacks the nearest live Monster in range - same "no dedicated input, just proximity" rule
// PlayerWoodcutting/PlayerMining already use (this game has never had a combo/twitch-skill attack
// button). Self-bootstrapping singleton (GuidedTutorial-style) since there's no scene-attached
// component to wire this to. See combat_design_v1.html §4.
public class PlayerCombat : MonoBehaviour
{
    // Base interval before WeaponTypeUtility.AttackIntervalMultiplier scales it per equipped
    // weapon type (see below) - this exact value is Sword's multiplier of 1x, so the pre-weapon-
    // types combat feel is unchanged.
    private const float BaseAttackInterval = 0.6f;
    private const float AttackRadius = 1.2f;

    // "매우 낮은 품질" 마석 드랍 (user request) - a trickle, not a guaranteed farm; also gated by
    // the player's mana carry capacity so a drop the player can't hold is just skipped rather than
    // wasted or force-added. See combat_design_v1.html follow-up notes.
    private const float ManaDropChance = 0.45f;

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
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private float attackTimer;
    private ToolSwing playerToolSwing;
    private CarryStack playerCarryStack;

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

        Monster target = FindNearestInRange();
        if (target == null)
        {
            return;
        }

        // Face the monster while it's in range, same gate PlayerWoodcutting uses for trees (don't
        // fight the player's own steering while they're actively moving) - user report: swings
        // didn't look aimed at the monster at all.
        if (!PlayerMotor.Instance.HasMovementInput)
        {
            PlayerMotor.Instance.FaceTarget(target.transform.position);
        }

        if (attackTimer > 0f)
        {
            return;
        }

        // Follows the player's equipped weapon (EquippedWeapon.Resolve) instead of a flat value -
        // "장착 무기 = 내가 만든 그 무기" (game_design_doc.html §9). Falls back to the best-owned
        // weapon when nothing's been explicitly equipped yet (weapon_diversity_design_v1.html §8),
        // and Iron Sword/no-element's stats match the old flat 10dmg/0.6s exactly, so a session
        // with nothing crafted yet plays identically to before this system existed.
        EquippedWeapon.Resolve(out WeaponType equippedWeaponType, out OreGrade equippedOreGrade, out ManaElement equippedElement, out ManaGrade equippedManaGrade);
        float damage = OreGradeUtility.AttackPower(equippedOreGrade) * WeaponTypeUtility.AttackPowerMultiplier(equippedWeaponType);

        attackTimer = BaseAttackInterval * WeaponTypeUtility.AttackIntervalMultiplier(equippedWeaponType);
        Vector3 targetPosition = target.transform.position;
        bool defeated = target.TakeDamage(damage);

        // No point afflicting a monster that's already gone.
        if (!defeated && equippedElement != ManaElement.None)
        {
            target.ApplyStatusEffect(equippedElement, ManaGradeUtility.PowerMultiplier(equippedManaGrade));
        }

        // Impact spark + camera shake so a landed hit actually reads as one (user report:
        // "현재 너무 허전해") - a bigger, distinct burst on the killing blow instead of stacking
        // both on the same hit. Tinted by the weapon's enchant element when it has one, instead of
        // the flat default gold.
        if (defeated)
        {
            HitEffects.Instance.MonsterDefeated(targetPosition);
        }
        else
        {
            HitEffects.Instance.PlayerHitMonster(targetPosition, ManaElementUtility.SparkColor(equippedElement));
        }

        if (playerToolSwing == null)
        {
            playerToolSwing = PlayerMotor.Instance.GetComponentInChildren<ToolSwing>();
        }

        // Swing shape follows the equipped weapon type (see ToolSwing.PlayWeaponSwing) - still
        // placeholder meshes, not the actual crafted weapon's own model.
        playerToolSwing?.PlayWeaponSwing(equippedWeaponType);

        if (defeated)
        {
            TryDropManaStone(targetPosition);
        }
    }

    private void TryDropManaStone(Vector3 position)
    {
        if (Random.value > ManaDropChance)
        {
            return;
        }

        if (playerCarryStack == null)
        {
            playerCarryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
        }

        if (playerCarryStack == null || playerCarryStack.IsFull(CarryLayer.ManaStone))
        {
            return; // very low quality drop - not worth forcing, just skip if the player's full up
        }

        playerCarryStack.TryAdd(CarryItemTemplates.ManaStoneChip, position, CarryLayer.ManaStone);
    }

    private Monster FindNearestInRange()
    {
        Vector3 playerPos = PlayerMotor.Instance.transform.position;
        IReadOnlyList<Monster> candidates = FieldMonsterSpawner.Instance.Monsters;

        Monster nearest = null;
        float nearestSqrDist = AttackRadius * AttackRadius;

        for (int i = 0; i < candidates.Count; i++)
        {
            Monster candidate = candidates[i];
            if (!candidate.IsAvailable)
            {
                continue;
            }

            // Horizontal-only distance, matching Monster's own attack-range check exactly - the
            // monster's sphere sits half a unit above its pivot while the player's own pivot
            // height differs, so a plain 3D distance here could disagree with Monster's decision
            // to stop and attack (bug report: "몬스터가 멈추는데 플레이어가 공격을 안 하는 경우").
            Vector3 toCandidate = candidate.transform.position - playerPos;
            toCandidate.y = 0f;
            float sqrDist = toCandidate.sqrMagnitude;

            if (sqrDist <= nearestSqrDist)
            {
                nearestSqrDist = sqrDist;
                nearest = candidate;
            }
        }

        return nearest;
    }
}
