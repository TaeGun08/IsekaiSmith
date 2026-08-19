using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owns the additive load/unload of StageScene and moving the player into/out of it
// (stage_system_design_v2.html §3) - StageEncounterController only knows about waves/monsters,
// StageSelectUI only knows about which stage the player tapped; this is the one place that
// actually touches SceneManager and PlayerMotor.Teleport.
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

    // Dungeon visits jitter the lane's position/length around LaneOrigin instead of reusing the
    // exact same spot every time - dungeon_design_v1.html §2's stand-in for "매 방문 랜덤 생성"
    // without a real procedural room/corridor generator (out of this pass's budget). Stages stay
    // at the fixed LaneOrigin/LaneLength for predictability - only the dungeon is meant to feel
    // "different every time".
    private const float DungeonLaneMinLength = 35f;
    private const float DungeonLaneMaxLength = 55f;
    private const float DungeonOriginJitterRadius = 60f;

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

    public bool CanEnterDungeon => !isTransitioning && StageEncounterController.Instance.CanBeginDungeon;

    public void EnterStage(int stageNumber)
    {
        if (!CanEnter(stageNumber))
        {
            return;
        }

        StartCoroutine(EnterRoutine(LaneOrigin, LaneLength, nePoint => StageEncounterController.Instance.BeginEncounter(stageNumber, nePoint)));
    }

    public void EnterDungeon()
    {
        if (!CanEnterDungeon)
        {
            return;
        }

        Vector2 jitter = Random.insideUnitCircle * DungeonOriginJitterRadius;
        Vector3 origin = LaneOrigin + new Vector3(jitter.x, 0f, jitter.y);
        float length = Random.Range(DungeonLaneMinLength, DungeonLaneMaxLength);

        StartCoroutine(EnterRoutine(origin, length, nePoint => StageEncounterController.Instance.BeginDungeonEncounter(nePoint)));
    }

    // Wired to StageEncounterUI's RETREAT button.
    public void RequestRetreat()
    {
        StageEncounterController.Instance.RequestRetreat();
    }

    private IEnumerator EnterRoutine(Vector3 laneOrigin, float laneLength, System.Action<Vector3> beginEncounter)
    {
        isTransitioning = true;
        playerReturnPosition = PlayerMotor.Instance.transform.position;

        yield return SceneManager.LoadSceneAsync(StageSceneName, LoadSceneMode.Additive);

        BuildLaneGround(laneOrigin, laneLength);

        Vector3 nePoint = laneOrigin + LaneDirection * laneLength;

        PlayerMotor.Instance.Teleport(laneOrigin);
        beginEncounter(nePoint);
        ShowFirstEntryHintIfNeeded();

        isTransitioning = false;
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
    private void BuildLaneGround(Vector3 laneOrigin, float laneLength)
    {
        var groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundGO.name = "StageLaneGround";

        Vector3 midpoint = laneOrigin + LaneDirection * (laneLength * 0.5f);
        groundGO.transform.position = midpoint + Vector3.down * 0.1f;
        groundGO.transform.rotation = Quaternion.LookRotation(LaneDirection, Vector3.up);
        // A bit longer than the SW/NE points themselves so the spawn/entry points aren't sitting
        // right at the ground's edge.
        groundGO.transform.localScale = new Vector3(LaneWidth, 0.2f, laneLength + LaneWidth);
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

        AsyncOperation unload = SceneManager.UnloadSceneAsync(StageSceneName);
        if (unload != null)
        {
            yield return unload;
        }

        isTransitioning = false;
    }
}
