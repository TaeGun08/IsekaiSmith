using UnityEngine;
using UnityEngine.EventSystems;

// Tracks press-and-hold for touch/mouse alike (mobile-friendly - no keyboard dependency).
// Used by the furnace bellows lever: hold to draw air, release to pump.
public class PointerHoldTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public bool IsHeld { get; private set; }
    public bool WasReleasedThisFrame { get; private set; }
    public float HeldDuration { get; private set; }

    public void OnPointerDown(PointerEventData eventData)
    {
        IsHeld = true;
        HeldDuration = 0f;
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
        // Finger/mouse sliding off the lever while held shouldn't leave it stuck "held".
        if (IsHeld)
        {
            WasReleasedThisFrame = true;
        }

        IsHeld = false;
    }

    private void Update()
    {
        if (IsHeld)
        {
            HeldDuration += Time.deltaTime;
        }
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
        HeldDuration = 0f;
    }

    // Dev-only: drives IsHeld/WasReleasedThisFrame directly (the exact same fields OnPointerDown/
    // OnPointerUp write to) without a real pointer event - used by DevAutoPlayController to
    // actually play the hammering minigame.
    public void DevSetHeld(bool held)
    {
        if (held)
        {
            if (!IsHeld)
            {
                IsHeld = true;
                HeldDuration = 0f;
            }
        }
        else
        {
            if (IsHeld)
            {
                WasReleasedThisFrame = true;
            }

            IsHeld = false;
        }
    }
}
