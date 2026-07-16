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
    private static readonly Color ThinkingColorA = new Color(0.62f, 0.52f, 0.84f);
    private static readonly Color ThinkingColorB = new Color(0.75f, 0.68f, 0.92f);
    private static readonly Color ReadingColorA = new Color(0.38f, 0.66f, 0.64f);
    private static readonly Color ReadingColorB = new Color(0.55f, 0.80f, 0.78f);
    private static readonly Color EditingColorA = new Color(1f, 0.62f, 0.25f);
    private static readonly Color EditingColorB = new Color(1f, 0.85f, 0.4f);
    private static readonly Color RunningColorA = new Color(0.85f, 0.47f, 0.34f);
    private static readonly Color RunningColorB = new Color(0.95f, 0.6f, 0.45f);
    private static readonly Color SuccessColor = new Color(0.4f, 0.75f, 0.5f);
    private static readonly Color ErrorColor = new Color(0.85f, 0.35f, 0.35f);
    private static readonly Color EyeColor = new Color(0.15f, 0.15f, 0.18f);

    private const float BodySize = 56f;
    private const float EyeSize = 8f;
    private const float EyeSpacing = 10f;
    private const float EyeYOffset = -6f;
    private const int OrbitDotCount = 3;
    private const float OrbitRadius = 42f;
    private const double SuccessFlashSeconds = 1.2;
    private const double ErrorFlashSeconds = 1.4;

    private readonly VisualElement body;
    private readonly VisualElement eyeLeft;
    private readonly VisualElement eyeRight;
    private readonly VisualElement[] orbitDots;
    private readonly Label stateLabel;

    private bool isBlinking;
    private double nextBlinkTime;
    private double blinkEndTime;

    private double flashStart;
    private double flashUntil;
    private bool flashIsError;

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

    // One-shot reactions to a turn actually finishing - called by the host window from
    // CompanionSession.Runner.OnTurnComplete/OnError, not derived from CurrentActivity (which
    // is already back to Idle/Thinking by the time those fire). Purely a timed visual overlay;
    // doesn't touch any session state.
    public void FlashSuccess()
    {
        flashStart = EditorApplication.timeSinceStartup;
        flashUntil = flashStart + SuccessFlashSeconds;
        flashIsError = false;
    }

    public void FlashError()
    {
        flashStart = EditorApplication.timeSinceStartup;
        flashUntil = flashStart + ErrorFlashSeconds;
        flashIsError = true;
    }

    // Called every animation tick with the active session's current activity - purely visual,
    // this class doesn't touch CompanionSession/session data itself.
    public void Tick(CharacterActivity activity, double t)
    {
        float width = resolvedStyle.width;
        float height = resolvedStyle.height;
        if (float.IsNaN(width) || width <= 0f || float.IsNaN(height) || height <= 0f)
        {
            // Not laid out yet (first tick or two right after being added) - nothing sensible
            // to position against yet; the next tick will pick it up once layout resolves.
            return;
        }

        bool busy = activity != CharacterActivity.Idle;
        bool flashing = t < flashUntil;

        Vector2 center = new Vector2(width / 2f, height / 2f + 4f);

        float bobAmplitude = busy ? 7f : 3f;
        float bobSpeed = busy ? 6f : 2f;
        float bobY = Mathf.Sin((float)t * bobSpeed) * bobAmplitude;

        GetActivityStyle(activity, out Color colorA, out Color colorB, out string label);
        if (flashing)
        {
            colorA = colorB = flashIsError ? ErrorColor : SuccessColor;
            label = flashIsError ? "문제가 발생했어요" : "완료!";
        }

        // A little physicality on top of the color/eye change: a quick horizontal shake that
        // decays over the error flash, a single scale "pop" over the success flash. Both are
        // pure functions of elapsed flash time, not stored velocity/state, so they can't drift
        // or accumulate across ticks.
        float shakeX = 0f;
        float bodyScale = 1f;
        if (flashing)
        {
            float elapsed = (float)(t - flashStart);
            if (flashIsError)
            {
                float decay = 1f - Mathf.Clamp01(elapsed / (float)ErrorFlashSeconds);
                shakeX = Mathf.Sin(elapsed * 40f) * 6f * decay;
            }
            else
            {
                float progress = Mathf.Clamp01(elapsed / (float)SuccessFlashSeconds);
                bodyScale = 1f + Mathf.Sin(progress * Mathf.PI) * 0.18f;
            }
        }

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
            Color dotColor = colorB;
            dotColor.a = 0.85f;
            dot.style.backgroundColor = dotColor;
        }

        Color bodyColor = (busy || flashing)
            ? Color.Lerp(colorA, colorB, Mathf.PingPong((float)t * 3f, 1f))
            : colorA;
        body.style.backgroundColor = bodyColor;
        body.style.left = center.x - BodySize / 2f + shakeX;
        body.style.top = center.y - BodySize / 2f + bobY;
        body.style.scale = new Scale(new Vector3(bodyScale, bodyScale, 1f));

        UpdateBlink(t);
        float eyeOpen = isBlinking ? 0.15f : 1f;
        if (flashing)
        {
            // Wide "happy" eyes on success, a brief squint on error - skips the blink cycle
            // entirely since the flash is short enough that a mid-flash blink would just read
            // as a glitch rather than an expression.
            eyeOpen = flashIsError ? 0.5f : 1.3f;
        }
        float eyeHeight = EyeSize * eyeOpen;
        float eyeY = center.y + bobY + EyeYOffset - eyeHeight / 2f;

        eyeLeft.style.height = eyeHeight;
        eyeLeft.style.left = center.x - EyeSpacing - EyeSize / 2f + shakeX;
        eyeLeft.style.top = eyeY;

        eyeRight.style.height = eyeHeight;
        eyeRight.style.left = center.x + EyeSpacing - EyeSize / 2f + shakeX;
        eyeRight.style.top = eyeY;

        stateLabel.text = label;
    }

    private static void GetActivityStyle(CharacterActivity activity, out Color colorA, out Color colorB, out string label)
    {
        switch (activity)
        {
            case CharacterActivity.Thinking:
                colorA = ThinkingColorA;
                colorB = ThinkingColorB;
                label = "생각하는 중...";
                break;
            case CharacterActivity.Reading:
                colorA = ReadingColorA;
                colorB = ReadingColorB;
                label = "파일 읽는 중...";
                break;
            case CharacterActivity.Editing:
                colorA = EditingColorA;
                colorB = EditingColorB;
                label = "코드 수정 중...";
                break;
            case CharacterActivity.Running:
                colorA = RunningColorA;
                colorB = RunningColorB;
                label = "명령 실행 중...";
                break;
            default:
                colorA = colorB = IdleBodyColor;
                label = "대기 중";
                break;
        }
    }

    // Shared with the sidebar's per-session busy dots (see ClaudeCompanionWindow.OnAnimationTick)
    // so a background tab's dot hints at what it's doing without needing to switch to it.
    public static Color GetIndicatorColor(CharacterActivity activity)
    {
        GetActivityStyle(activity, out Color colorA, out Color colorB, out _);
        return activity == CharacterActivity.Idle ? colorA : colorB;
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
