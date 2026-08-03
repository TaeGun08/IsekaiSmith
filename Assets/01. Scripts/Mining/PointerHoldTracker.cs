using UnityEngine;
using UnityEngine.EventSystems;

// Tracks press-and-hold for touch/mouse alike (mobile-friendly - no keyboard dependency).
// Also exposes a one-frame "just released" pulse so callers can capture a value (e.g. a
// power gauge) at the exact moment of release.
public class PointerHoldTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsHeld { get; private set; }
    public bool WasReleasedThisFrame { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsHeld = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (IsHeld)
        {
            WasReleasedThisFrame = true;
        }

        IsHeld = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // Finger/mouse sliding off the button while held shouldn't leave it stuck "held".
        if (IsHeld)
        {
            WasReleasedThisFrame = true;
        }

        IsHeld = false;
    }

    private void LateUpdate()
    {
        // Cleared after all this frame's Update-timed coroutines have had a chance to read it.
        WasReleasedThisFrame = false;
    }

    private void OnDisable()
    {
        IsHeld = false;
        WasReleasedThisFrame = false;
    }
}
