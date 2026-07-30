using UnityEngine;
using UnityEngine.EventSystems;

// Tracks press-and-hold for touch/mouse alike (mobile-friendly - no keyboard dependency).
public class PointerHoldTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsHeld { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        IsHeld = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Finger/mouse sliding off the button while held shouldn't leave it stuck "held".
        IsHeld = false;
    }

    private void OnDisable()
    {
        IsHeld = false;
    }
}
