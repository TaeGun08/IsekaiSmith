using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Single reusable forge-upgrade floor spot (customer_order_design_v7.html §2) - not a button or
// confirm dialog, just a place to walk through: standing near it with enough gold silently
// absorbs the cost and steps ForgeUpgrade to the next tier (Rough -> Common -> Fine). The same
// node re-labels itself for the next tier afterward instead of a new node per tier, so only one
// of these ever needs to exist in the scene.
public class ForgeUpgradeNode : MonoBehaviour
{
    [SerializeField] private float absorbRadius = 2f;
    [SerializeField] private float absorbInterval = 0.4f;

    private float absorbTimer;
    private TMP_Text label;
    private Transform labelRoot;

    private void Awake()
    {
        InteractionPadIndicator.Attach(transform, absorbRadius);
        BuildLabel();
        RefreshLabel();
    }

    private void BuildLabel()
    {
        var canvasGO = new GameObject("ForgeUpgradeLabel", typeof(Canvas), typeof(CanvasScaler));
        canvasGO.transform.SetParent(transform, false);
        canvasGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        canvasGO.transform.localScale = Vector3.one * 0.01f;
        labelRoot = canvasGO.transform;

        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var rect = canvasGO.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(260f, 60f);

        var textGO = new GameObject("Text", typeof(RectTransform));
        textGO.transform.SetParent(canvasGO.transform, false);
        var textRect = textGO.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        label = textGO.AddComponent<TextMeshProUGUI>();
        label.fontSize = 30;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.95f, 0.85f, 0.4f);
    }

    private void Update()
    {
        if (ForgeUpgrade.IsMaxed)
        {
            return;
        }

        // Billboard only while still relevant - once maxed, RefreshLabel already deactivated
        // labelRoot and this method bails out above before reaching it.
        if (Camera.main != null)
        {
            labelRoot.rotation = Camera.main.transform.rotation;
        }

        absorbTimer -= Time.deltaTime;
        if (absorbTimer > 0f || PlayerMotor.Instance == null)
        {
            return;
        }

        float sqrDist = (PlayerMotor.Instance.transform.position - transform.position).sqrMagnitude;
        if (sqrDist > absorbRadius * absorbRadius)
        {
            return;
        }

        absorbTimer = absorbInterval;
        if (ForgeUpgrade.TryUpgrade())
        {
            RefreshLabel();
            ToastUI.Instance.Show("Forge upgraded: " + CraftGradeUtility.DisplayName(ForgeUpgrade.CurrentTier) + "!", 2.5f);
        }
    }

    private void RefreshLabel()
    {
        if (ForgeUpgrade.IsMaxed)
        {
            labelRoot.gameObject.SetActive(false);
            return;
        }

        labelRoot.gameObject.SetActive(true);
        label.text = "Unlock " + CraftGradeUtility.DisplayName(ForgeUpgrade.NextTier.Value) + " - " + ForgeUpgrade.NextCost + "G";
    }
}
