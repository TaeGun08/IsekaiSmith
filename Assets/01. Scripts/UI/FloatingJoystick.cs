using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;


public class FloatingJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform canvasRect;
    [SerializeField] private RectTransform background;
    [SerializeField] private RectTransform handle;
    [SerializeField] private float radius = 100f;

    private Vector2 backgroundAnchoredPosition;

private void Awake()
    {
        Sprite circle = GetCircleSprite();
        background.GetComponent<Image>().sprite = circle;
        handle.GetComponent<Image>().sprite = circle;
        SetVisible(false);
    }

private static Sprite circleSprite;

    private static Sprite GetCircleSprite()
    {
        if (circleSprite != null)
        {
            return circleSprite;
        }

        const int size = 128;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        float radius = size / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                float alpha = Mathf.Clamp01(radius - dist);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();

        circleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return circleSprite;
    }


    public void OnPointerDown(PointerEventData eventData)
    {
        SetVisible(true);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        backgroundAnchoredPosition = localPoint;
        background.anchoredPosition = backgroundAnchoredPosition;
        handle.anchoredPosition = backgroundAnchoredPosition;

        PlayerMotor.Instance.SetJoystickInput(Vector2.zero);
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

        Vector2 offset = Vector2.ClampMagnitude(localPoint - backgroundAnchoredPosition, radius);
        handle.anchoredPosition = backgroundAnchoredPosition + offset;

        PlayerMotor.Instance.SetJoystickInput(offset / radius);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        SetVisible(false);
        PlayerMotor.Instance.SetJoystickInput(Vector2.zero);
    }

    private void SetVisible(bool visible)
    {
        background.gameObject.SetActive(visible);
        handle.gameObject.SetActive(visible);
    }
}
