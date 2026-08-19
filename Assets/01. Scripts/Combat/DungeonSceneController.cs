using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Owns the additive load/unload of DungeonScene and moving the player into/out of it - the
// dungeon's own counterpart to StageSceneController (user request: "던전씬을 따로 만들어서
// 관리하는 게 좋을 것 같아"). Builds a circular arena instead of a lane (monsters spawn in a ring
// around its center, see DungeonEncounterController.SpawnRing), in a completely separate world-
// space area from the stage lane so the two additive scenes can never overlap even if something
// ever loaded both at once.
public class DungeonSceneController : MonoBehaviour
{
    private const string DungeonSceneName = "DungeonScene";

    private const float ArenaRadius = 14f;
    // Jittered per visit so the arena isn't in the exact same spot every time
    // (dungeon_design_v1.html §2's stand-in for "매번 새로 생성된 것" without a real procedural
    // generator) - moot now that the dungeon is one-time-only, but harmless to keep.
    private const float ArenaOriginJitterRadius = 60f;
    private static readonly Vector3 ArenaOrigin = new Vector3(500f, 0f, -500f);

    private static DungeonSceneController instance;

    public static DungeonSceneController Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("DungeonSceneController");
                instance = go.AddComponent<DungeonSceneController>();
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
        DungeonEncounterController.Instance.OnEncounterEnded += HandleEncounterEnded;
    }

    public bool CanEnter => !isTransitioning && DungeonEncounterController.Instance.CanBegin;

    public void EnterDungeon()
    {
        if (!CanEnter)
        {
            return;
        }

        StartCoroutine(EnterRoutine());
    }

    // Wired to StageEncounterUI's RETREAT button while a dungeon encounter is active.
    public void RequestRetreat()
    {
        DungeonEncounterController.Instance.RequestRetreat();
    }

    private IEnumerator EnterRoutine()
    {
        isTransitioning = true;
        playerReturnPosition = PlayerMotor.Instance.transform.position;

        yield return SceneManager.LoadSceneAsync(DungeonSceneName, LoadSceneMode.Additive);

        Vector2 jitter = Random.insideUnitCircle * ArenaOriginJitterRadius;
        Vector3 arenaCenter = ArenaOrigin + new Vector3(jitter.x, 0f, jitter.y);

        BuildArenaGround(arenaCenter);

        PlayerMotor.Instance.Teleport(arenaCenter);
        DungeonEncounterController.Instance.BeginEncounter(arenaCenter);

        isTransitioning = false;
    }

    // A single flattened, circular blockout - pure functional shape (surrounds the player evenly
    // on every side, matching the ring spawn pattern), no decoration. Parented into DungeonScene
    // via MoveGameObjectToScene so it's destroyed automatically on unload.
    private void BuildArenaGround(Vector3 center)
    {
        var groundGO = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        groundGO.name = "DungeonArenaGround";
        groundGO.transform.position = center + Vector3.down * 0.1f;
        // Default Cylinder is radius 0.5, height 2 - scale X/Z to the target radius (x2 for
        // diameter) and flatten Y into a thin disc.
        groundGO.transform.localScale = new Vector3(ArenaRadius * 2f, 0.1f, ArenaRadius * 2f);
        groundGO.GetComponent<Renderer>().material.color = new Color(0.3f, 0.27f, 0.34f); // dark stone, distinct from the stage lane's tan

        Scene dungeonScene = SceneManager.GetSceneByName(DungeonSceneName);
        if (dungeonScene.IsValid())
        {
            SceneManager.MoveGameObjectToScene(groundGO, dungeonScene);
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

        AsyncOperation unload = SceneManager.UnloadSceneAsync(DungeonSceneName);
        if (unload != null)
        {
            yield return unload;
        }

        isTransitioning = false;
    }
}
