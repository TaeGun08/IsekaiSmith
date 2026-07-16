using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Procedural vector "character" for the Companion window's status stage, built from plain
// VisualElements (position: absolute + border-radius: 50% via the "stage-circle" USS class)
// instead of a hand-tinted Texture2D circle. Nothing here calls MarkDirtyRepaint - the host
// window drives Tick() on its own schedule (see ClaudeCompanionWindow.OnTick), and UI Toolkit
// only actually repaints a panel when a style/layout value changes, so this stays cheap while
// idle instead of forcing a full-window redraw every frame the way the old IMGUI version did.
public class CharacterStageElement : VisualElement
{
    private static readonly Color IdleBodyColor = new Color(0.55f, 0.62f, 0.72f);
    private static readonly Color BusyBodyColorA = new Color(1f, 0.62f, 0.25f);
    private static readonly Color BusyBodyColorB = new Color(1f, 0.85f, 0.4f);
    private static readonly Color OrbitDotColor = new Color(1f, 0.85f, 0.4f, 0.85f);
    private static readonly Color EyeColor = new Color(0.15f, 0.15f, 0.18f);

    private const float BodySize = 56f;
    private const float EyeSize = 8f;
    private const float EyeSpacing = 10f;
    private const float EyeYOffset = -6f;
    private const int OrbitDotCount = 3;
    private const float OrbitRadius = 42f;

    private readonly VisualElement body;
    private readonly VisualElement eyeLeft;
    private readonly VisualElement eyeRight;
    private readonly VisualElement[] orbitDots;
    private readonly Label stateLabel;

    private bool isBlinking;
    private double nextBlinkTime;
    private double blinkEndTime;

    public CharacterStageElement()
    {
        AddToClassList("character-stage");

        body = MakeCircle(BodySize);
        Add(body);

        eyeLeft = MakeCircle(EyeSize);
        eyeLeft.style.backgroundColor = EyeColor;
        Add(eyeLeft);

        eyeRight = MakeCircle(EyeSize);
        eyeRight.style.backgroundColor = EyeColor;
        Add(eyeRight);

        orbitDots = new VisualElement[OrbitDotCount];
        for (int i = 0; i < OrbitDotCount; i++)
        {
            VisualElement dot = MakeCircle(8f);
            dot.style.backgroundColor = OrbitDotColor;
            dot.style.display = DisplayStyle.None;
            orbitDots[i] = dot;
            Add(dot);
        }

        stateLabel = new Label();
        stateLabel.AddToClassList("stage-state-label");
        Add(stateLabel);
    }

    private static VisualElement MakeCircle(float size)
    {
        VisualElement circle = new VisualElement();
        circle.AddToClassList("stage-circle");
        circle.style.width = size;
        circle.style.height = size;
        // Purely decorative - never steals a click/hover from anything else on the stage.
        circle.pickingMode = PickingMode.Ignore;
        return circle;
    }

    // Called every animation tick with the active session's busy state - purely visual, this
    // class doesn't touch CompanionSession/session data itself.
    public void Tick(bool busy, double t)
    {
        float width = resolvedStyle.width;
        float height = resolvedStyle.height;
        if (float.IsNaN(width) || width <= 0f || float.IsNaN(height) || height <= 0f)
        {
            // Not laid out yet (first tick or two right after being added) - nothing sensible
            // to position against yet; the next tick will pick it up once layout resolves.
            return;
        }

        Vector2 center = new Vector2(width / 2f, height / 2f + 4f);

        float bobAmplitude = busy ? 7f : 3f;
        float bobSpeed = busy ? 6f : 2f;
        float bobY = Mathf.Sin((float)t * bobSpeed) * bobAmplitude;

        for (int i = 0; i < OrbitDotCount; i++)
        {
            VisualElement dot = orbitDots[i];
            if (!busy)
            {
                dot.style.display = DisplayStyle.None;
                continue;
            }
            dot.style.display = DisplayStyle.Flex;
            float angle = (float)t * 4f + i * (Mathf.PI * 2f / OrbitDotCount);
            Vector2 dotPos = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle) * 0.6f) * OrbitRadius;
            dot.style.left = dotPos.x - 4f;
            dot.style.top = dotPos.y - 4f;
        }

        Color bodyColor = busy
            ? Color.Lerp(BusyBodyColorA, BusyBodyColorB, Mathf.PingPong((float)t * 3f, 1f))
            : IdleBodyColor;
        body.style.backgroundColor = bodyColor;
        body.style.left = center.x - BodySize / 2f;
        body.style.top = center.y - BodySize / 2f + bobY;

        UpdateBlink(t);
        float eyeOpen = isBlinking ? 0.15f : 1f;
        float eyeHeight = EyeSize * eyeOpen;
        float eyeY = center.y + bobY + EyeYOffset - eyeHeight / 2f;

        eyeLeft.style.height = eyeHeight;
        eyeLeft.style.left = center.x - EyeSpacing - EyeSize / 2f;
        eyeLeft.style.top = eyeY;

        eyeRight.style.height = eyeHeight;
        eyeRight.style.left = center.x + EyeSpacing - EyeSize / 2f;
        eyeRight.style.top = eyeY;

        stateLabel.text = busy ? "열심히 작업 중..." : "대기 중";
    }

    private void UpdateBlink(double t)
    {
        if (nextBlinkTime <= 0)
        {
            nextBlinkTime = t + Random.Range(2f, 5f);
        }

        if (!isBlinking && t >= nextBlinkTime)
        {
            isBlinking = true;
            blinkEndTime = t + 0.12;
        }
        else if (isBlinking && t >= blinkEndTime)
        {
            isBlinking = false;
            nextBlinkTime = t + Random.Range(2f, 5f);
        }
    }
}
