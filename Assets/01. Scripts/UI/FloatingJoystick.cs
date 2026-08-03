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
        Sprite circle = UIShapes.Circle();
        background.GetComponent<Image>().sprite = circle;
        handle.GetComponent<Image>().sprite = circle;
        SetVisible(false);
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
