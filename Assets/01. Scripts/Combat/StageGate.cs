using UnityEngine;

// The one physical entry point into the stage system (stage_system_design_v1.html §1/§4) -
// deliberately a single gate that always offers "the next stage" rather than one gate per stage,
// so no new terrain/placement is needed per stage (only this one object, positioned by deriving
// off FieldMonsterSpawner's own anchor - same "no guessed coordinates" rule it already follows).
// Self-bootstrapping singleton, same pattern as FieldMonsterSpawner/CraftingStation.
public class StageGate : MonoBehaviour
{
    private const float InteractRadius = 2.5f;
    private const float GateClearance = 4f; // extra distance beyond the field monsters' spawn ring

    private static StageGate instance;

    public static StageGate Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("StageGate");
                instance = go.AddComponent<StageGate>();
                DontDestroyOnLoad(go);
                go.transform.SetParent(RuntimeSystemsRoot.Instance, false);
            }

            return instance;
        }
    }

    private Renderer pillarRenderer;
    private bool promptShown;

    private static readonly Color ReadyColor = new Color(0.75f, 0.6f, 0.2f);
    private static readonly Color ClearedColor = new Color(0.4f, 0.68f, 0.45f);

    // Called once from ResourceHUD.Start() alongside the other bootstraps - idempotent so a stray
    // second call can't double-place the gate.
    public void Bootstrap()
    {
        if (pillarRenderer != null)
        {
            return;
        }

        Vector3 position = FieldMonsterSpawner.Instance.Anchor
            + FieldMonsterSpawner.Instance.OutwardDirection * (FieldMonsterSpawner.Instance.SpawnRadius + GateClearance);

        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "StageGate";
        pillar.transform.SetParent(transform, false);
        pillar.transform.position = position + Vector3.up * 1.2f;
        pillar.transform.localScale = new Vector3(1.1f, 1.2f, 1.1f);
        Object.Destroy(pillar.GetComponent<Collider>()); // visual only, matches Monster's convention

        pillarRenderer = pillar.GetComponent<Renderer>();

        InteractionPadIndicator.Attach(transform, InteractRadius);
        transform.position = position;
    }

    private void Update()
    {
        if (pillarRenderer == null)
        {
            return;
        }

        UpdateVisual();

        if (StageEncounterController.Instance.IsEncounterActive)
        {
            HidePromptIfShown();
            return;
        }

        bool nearPlayer = PlayerMotor.Instance != null &&
            (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude <= InteractRadius * InteractRadius;

        if (!nearPlayer)
        {
            HidePromptIfShown();
            return;
        }

        if (!promptShown)
        {
            if (StageBank.AllStagesCleared)
            {
                InteractionPromptUI.Instance.ShowSingle("All Stages Cleared", null, null);
            }
            else
            {
                int nextStage = StageBank.HighestStageCleared + 1;
                InteractionPromptUI.Instance.ShowSingle("Stage " + nextStage, "ENTER", () =>
                {
                    StageEncounterController.Instance.BeginEncounter(nextStage, transform.position);
                    InteractionPromptUI.Instance.Hide();
                    promptShown = false;
                });
            }

            promptShown = true;
        }
    }

    private void HidePromptIfShown()
    {
        if (promptShown)
        {
            InteractionPromptUI.Instance.Hide();
            promptShown = false;
        }
    }

    private void UpdateVisual()
    {
        Color target = StageBank.AllStagesCleared ? ClearedColor : ReadyColor;
        pillarRenderer.material.color = target;
    }
}
