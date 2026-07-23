using System.Collections;
using UnityEngine;

public class OreNode : MonoBehaviour
{
    [SerializeField] private int maxOre = 8;
    [SerializeField] private float mineTickInterval = 0.6f;
    [SerializeField] private float respawnDelay = 6f;
    [SerializeField] private Transform visual;

    private int remainingOre;
    private Coroutine respawnRoutine;

    public float MineTickInterval => mineTickInterval;
    public bool IsDepleted => remainingOre <= 0;

    private void Awake()
    {
        remainingOre = maxOre;
    }

    public bool TryMineTick()
    {
        if (remainingOre <= 0)
        {
            return false;
        }

        remainingOre--;

        if (remainingOre <= 0)
        {
            Deplete();
        }

        return true;
    }

    private void Deplete()
    {
        if (visual != null)
        {
            visual.gameObject.SetActive(false);
        }

        if (respawnRoutine != null)
        {
            StopCoroutine(respawnRoutine);
        }

        respawnRoutine = StartCoroutine(RespawnAfterDelay());
    }

    private IEnumerator RespawnAfterDelay()
    {
        yield return new WaitForSeconds(respawnDelay);
        remainingOre = maxOre;
        if (visual != null)
        {
            visual.gameObject.SetActive(true);
        }
        respawnRoutine = null;
    }
}
