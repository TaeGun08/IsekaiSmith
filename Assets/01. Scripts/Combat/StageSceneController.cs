using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owns the additive load/unload of StageScene and moving the player into/out of it
// (stage_system_design_v2.html §3) - StageEncounterController only knows about waves/monsters,
// StageSelectUI only knows about which stage the player tapped; this is the one place that
// actually touches SceneManager and PlayerMotor.Teleport.
//
// Stage-only - dungeon entry/exit moved to its own DungeonSceneController (user request:
// "던전씬을 따로 만들어서 관리하는 게 좋을 것 같아").
public class StageSceneController : MonoBehaviour
{
    private const string StageSceneName = "StageScene";
    private const string FirstEntryHintPrefsKey = "StageFirstEntryHintSeen";

    // A self-contained rectangle far from the field's own geometry (both scenes are loaded
    // simultaneously - additive, not a replace) so nothing can visually or physically overlap.
    // Diagonal SW->NE orientation per the user's request ("남서쪽에서 북동쪽 진행방향").
    private const float LaneLength = 40f;
    private const float LaneWidth = 10f;
    private static readonly Vector3 LaneOrigin = new Vector3(500f, 0f, 500f);
    private static readonly Vector3 LaneDirection = new Vector3(1f, 0f, 1f).normalized;

    private static StageSceneController instance;

    public static StageSceneController Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("StageSceneController");
                instance = go.AddComponent<StageSceneController>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private Vector3 playerReturnPosition;
    private bool isTransitioning;

    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        StageEncounterController.Instance.OnEncounterEnded += HandleEncounterEnded;
    }

    public bool CanEnter(int stageNumber)
    {
        return !isTransitioning && StageEncounterController.Instance.CanBegin(stageNumber);
    }

    public void EnterStage(int stageNumber)
    {
        if (!CanEnter(stageNumber))
        {
            return;
        }

        StartCoroutine(EnterRoutine(stageNumber));
    }

    // Wired to StageEncounterUI's RETREAT button.
    public void RequestRetreat()
    {
        StageEncounterController.Instance.RequestRetreat();
    }

    private IEnumerator EnterRoutine(int stageNumber)
    {
        isTransitioning = true;
        playerReturnPosition = PlayerMotor.Instance.transform.position;

        yield return SceneManager.LoadSceneAsync(StageSceneName, LoadSceneMode.Additive);

        BuildLaneGround();

        Vector3 swPoint = LaneOrigin;
        Vector3 nePoint = LaneOrigin + LaneDirection * LaneLength;

        PlayerMotor.Instance.Teleport(swPoint);
        SetCarryVisible(false);
        StageEncounterController.Instance.BeginEncounter(stageNumber, nePoint);
        ShowFirstEntryHintIfNeeded();

        isTransitioning = false;
    }

    // Hides whatever's stacked on the player's back for the duration of the fight - only
    // EquippedWeapon matters in combat, not raw materials/unsold weapons riding along
    // (사용자 요청 2026-08-24, see CarryStack.SetVisible).
    private static void SetCarryVisible(bool visible)
    {
        CarryStack carryStack = PlayerMotor.Instance.GetComponentInChildren<CarryStack>();
        if (carryStack != null)
        {
            carryStack.SetVisible(visible);
        }
    }

    // One-time explanation of how a stage fight actually works - a first-time player has no other
    // way to learn "waves keep coming, RETREAT is free" before the monsters are already closing in.
    // By the time this shows, there's a few seconds of walk time before the first wave reaches the
    // player (they spawn at the far NE end), so it never blocks input.
    private void ShowFirstEntryHintIfNeeded()
    {
        if (PlayerPrefs.GetInt(FirstEntryHintPrefsKey, 0) != 0)
        {
            return;
        }

        ToastUI.Instance.Show("Defeat every wave to clear the stage.\nRETREAT anytime - no reward, but no penalty.", 4.5f);
        PlayerPrefs.SetInt(FirstEntryHintPrefsKey, 1);
        PlayerPrefs.Save();
    }

    // A single flattened, diagonally-rotated cube spanning the lane - pure functional blockout,
    // no decoration (stage_system_design_v2.html §1 "장식은 여전히 손 안 댐"). Parented into
    // StageScene itself via MoveGameObjectToScene so it's destroyed automatically on unload
    // instead of needing to be tracked and cleaned up manually.
    private void BuildLaneGround()
    {
        var groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundGO.name = "StageLaneGround";

        Vector3 midpoint = LaneOrigin + LaneDirection * (LaneLength * 0.5f);
        groundGO.transform.position = midpoint + Vector3.down * 0.1f;
        groundGO.transform.rotation = Quaternion.LookRotation(LaneDirection, Vector3.up);
        // A bit longer than the SW/NE points themselves so the spawn/entry points aren't sitting
        // right at the ground's edge.
        groundGO.transform.localScale = new Vector3(LaneWidth, 0.2f, LaneLength + LaneWidth);
        groundGO.GetComponent<Renderer>().material.color = new Color(0.55f, 0.5f, 0.38f);

        Scene stageScene = SceneManager.GetSceneByName(StageSceneName);
        if (stageScene.IsValid())
        {
            SceneManager.MoveGameObjectToScene(groundGO, stageScene);
        }
    }

    private void HandleEncounterEnded(bool cleared)
    {
        StartCoroutine(ExitRoutine());
    }

    private IEnumerator ExitRoutine()
    {
        isTransitioning = true;

        PlayerMotor.Instance.Teleport(playerReturnPosition);
        SetCarryVisible(true);

        AsyncOperation unload = SceneManager.UnloadSceneAsync(StageSceneName);
        if (unload != null)
        {
            yield return unload;
        }

        isTransitioning = false;
    }
}
