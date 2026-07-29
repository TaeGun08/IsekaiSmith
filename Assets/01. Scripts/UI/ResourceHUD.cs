using TMPro;
using UnityEngine;

public class ResourceHUD : MonoBehaviour
{
    [SerializeField] private TMP_Text woodText;
    [SerializeField] private TMP_Text oreText;

    private int lastWood = -1;
    private int lastOre = -1;

    private void Update()
    {
        int wood = StorageDepot.TotalWood;
        int ore = StorageDepot.TotalOre;

        if (wood == lastWood && ore == lastOre)
        {
            return;
        }

        lastWood = wood;
        lastOre = ore;

        if (woodText != null)
        {
            woodText.text = "Wood " + wood;
        }

        if (oreText != null)
        {
            oreText.text = "Ore " + ore;
        }
    }
}
