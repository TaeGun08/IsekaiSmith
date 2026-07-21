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
    // 2026-07-16: bumped saturation across the board (esp. Running, which read as a dusty
    // brown before) - "dark theme is fine, but wants actual color presence" feedback.
    private static readonly Color IdleBodyColor = new Color(0.55f, 0.62f, 0.72f);
    private static readonly Color ThinkingColorA = new Color(0.58f, 0.44f, 0.88f);
    private static readonly Color ThinkingColorB = new Color(0.72f, 0.62f, 0.96f);
    private static readonly Color ReadingColorA = new Color(0.22f, 0.68f, 0.64f);
    private static readonly Color ReadingColorB = new Color(0.38f, 0.85f, 0.80f);
    private static readonly Color EditingColorA = new Color(1f, 0.62f, 0.25f);
    private static readonly Color EditingColorB = new Color(1f, 0.85f, 0.4f);
    private static readonly Color RunningColorA = new Color(0.95f, 0.36f, 0.20f);
    private static readonly Color RunningColorB = new Color(1f, 0.54f, 0.30f);
    private static readonly Color SuccessColor = new Color(0.28f, 0.80f, 0.46f);
    private static readonly Color ErrorColor = new Color(0.92f, 0.26f, 0.26f);
    private static readonly Color EyeColor = new Color(0.15f, 0.15f, 0.18f);

    private const float BodySize = 56f;
    private const float HaloOuterSize = BodySize + 46f;
    private const float HaloInnerSize = BodySize + 20f;
    private const float RingSize = BodySize + 10f;
    private const float EyeSize = 8f;
    private const float EyeSpacing = 10f;
    private const float EyeYOffset = -6f;
    private const float MouthYOffset = 13f;
    private const float LabelReserve = 18f;
    private const float GlassesLensSize = 12f;
    private const float GlassesOffset = 11f;
    private const float ThoughtBubbleWidth = 22f;
    private const float ThoughtBubbleHeight = 14f;
    private const int OrbitDotCount = 3;
    private const float OrbitRadius = 42f;
    private const double SuccessFlashSeconds = 1.2;
    private const double ErrorFlashSeconds = 1.4;

    private readonly VisualElement haloOuter;
    private readonly VisualElement haloInner;
    private readonly VisualElement body;
    private readonly VisualElement ring;
    private readonly VisualElement eyeLeft;
    private readonly VisualElement eyeRight;
    private readonly VisualElement mouth;
    private readonly VisualElement glassesLeft;
    private readonly VisualElement glassesRight;
    private readonly VisualElement glassesBridge;
    private readonly VisualElement thoughtBubble;
    private readonly VisualElement thoughtTailBig;
    private readonly VisualElement thoughtTailSmall;
    private readonly VisualElement[] thoughtDots;
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

        // Halo layers are added first so they paint behind the body (VisualElement children
        // paint in the order they were added, like a painter's algorithm) - two overlapping,
        // low-alpha circles of decreasing opacity fake a soft radial glow, since USS has no
        // radial-gradient support to do this with a single element.
        haloOuter = MakeCircle(HaloOuterSize);
        Add(haloOuter);

        haloInner = MakeCircle(HaloInnerSize);
        Add(haloInner);

        body = MakeCircle(BodySize);
        Add(body);

        // Painted after the body so its ring sits on top at the body's edge; background stays
        // fully transparent (see "stage-ring" in USS) so only the border itself is visible.
        ring = MakeCircle(RingSize);
        ring.RemoveFromClassList("stage-circle");
        ring.AddToClassList("stage-ring");
        Add(ring);

        eyeLeft = MakeCircle(EyeSize);
        eyeLeft.style.backgroundColor = EyeColor;
        Add(eyeLeft);

        eyeRight = MakeCircle(EyeSize);
        eyeRight.style.backgroundColor = EyeColor;
        Add(eyeRight);

        mouth = new VisualElement();
        mouth.AddToClassList("stage-mouth");
        mouth.style.backgroundColor = EyeColor;
        mouth.pickingMode = PickingMode.Ignore;
        Add(mouth);

        // "Developer glasses" - shown only while Editing/Running (see Tick) - round frames on
        // top of the eyes plus a short bridge between them. Purely a costume layer; the eyes
        // underneath keep animating (blink etc.) same as always.
        glassesLeft = MakeCircle(GlassesLensSize);
        glassesLeft.RemoveFromClassList("stage-circle");
        glassesLeft.AddToClassList("stage-glasses-lens");
        Add(glassesLeft);

        glassesRight = MakeCircle(GlassesLensSize);
        glassesRight.RemoveFromClassList("stage-circle");
        glassesRight.AddToClassList("stage-glasses-lens");
        Add(glassesRight);

        glassesBridge = new VisualElement();
        glassesBridge.AddToClassList("stage-glasses-bridge");
        glassesBridge.pickingMode = PickingMode.Ignore;
        Add(glassesBridge);

        // Thought bubble - shown only while Thinking (see Tick): a small cloud above the head
        // with two trailing dots and a gentle "..." pulse inside, instead of relying on mouth
        // motion to sell "thinking" (the mouth's own chatter animation read as fish-like
        // flapping - user report, 2026-07-16 - so it's gone now; this replaces that job).
        thoughtTailBig = MakeCircle(6f);
        thoughtTailBig.RemoveFromClassList("stage-circle");
        thoughtTailBig.AddToClassList("stage-thought-tail");
        Add(thoughtTailBig);

        thoughtTailSmall = MakeCircle(4f);
        thoughtTailSmall.RemoveFromClassList("stage-circle");
        thoughtTailSmall.AddToClassList("stage-thought-tail");
        Add(thoughtTailSmall);

        thoughtBubble = new VisualElement();
        thoughtBubble.AddToClassList("stage-thought-bubble");
        thoughtBubble.pickingMode = PickingMode.Ignore;
        Add(thoughtBubble);

        thoughtDots = new VisualElement[3];
        for (int i = 0; i < thoughtDots.Length; i++)
        {
            VisualElement dot = MakeCircle(3f);
            dot.RemoveFromClassList("stage-circle");
            dot.AddToClassList("stage-thought-dot");
            thoughtDots[i] = dot;
            Add(dot);
        }

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

        // Reserve room for stateLabel at the very bottom (see LabelReserve) and center the
        // character in what's left, instead of the stage's full height - the character's own
        // body/eyes were overlapping (covering) the "대기 중" text at the old center (user
        // report, 2026-07-16), since a full-height center left barely any gap once bob motion
        // was added on top.
        float characterAreaHeight = height - LabelReserve;
        Vector2 center = new Vector2(width / 2f, characterAreaHeight / 2f);

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

        // Squash & stretch tied to the bob itself (classic animation principle) instead of a
        // separate timer, so it can't drift out of sync: stretched (taller/narrower) near the
        // top of the bob, squashed (shorter/wider) near the bottom.
        float normalizedBob = bobAmplitude > 0f ? bobY / bobAmplitude : 0f;
        float squash = busy ? normalizedBob * 0.07f : normalizedBob * 0.03f;
        body.style.scale = new Scale(new Vector3(bodyScale * (1f - squash), bodyScale * (1f + squash), 1f));

        // Soft breathing glow behind the body - slower/independent of the body's own busy
        // pulse so it doesn't just look like a blurry copy of it.
        float haloPulse = 0.5f + 0.5f * Mathf.Sin((float)t * 1.2f);
        Color haloColor = bodyColor;
        haloOuter.style.backgroundColor = new Color(haloColor.r, haloColor.g, haloColor.b, 0.05f + haloPulse * 0.04f);
        haloOuter.style.left = center.x - HaloOuterSize / 2f + shakeX;
        haloOuter.style.top = center.y - HaloOuterSize / 2f + bobY;
        haloInner.style.backgroundColor = new Color(haloColor.r, haloColor.g, haloColor.b, 0.09f + haloPulse * 0.06f);
        haloInner.style.left = center.x - HaloInnerSize / 2f + shakeX;
        haloInner.style.top = center.y - HaloInnerSize / 2f + bobY;

        // Signature ring: each of the 4 border sides samples between the body's own colorA/
        // colorB at a phase offset, faking a rotating gradient (USS has no conic-gradient
        // support) with only solid per-side border colors - deliberately the *same* two colors
        // driving the body (not an unrelated fixed palette) so the ring always harmonizes with
        // whatever color the body currently is (user report, 2026-07-16: an independent
        // violet/coral/gold ring clashed against the body's own color).
        ring.style.left = center.x - RingSize / 2f + shakeX;
        ring.style.top = center.y - RingSize / 2f + bobY;
        float ringRotation = (float)t * 0.6f;
        ring.style.borderTopColor = Color.Lerp(colorA, colorB, RingWave(ringRotation + 0f));
        ring.style.borderRightColor = Color.Lerp(colorA, colorB, RingWave(ringRotation + 1.57f));
        ring.style.borderBottomColor = Color.Lerp(colorA, colorB, RingWave(ringRotation + 3.14f));
        ring.style.borderLeftColor = Color.Lerp(colorA, colorB, RingWave(ringRotation + 4.71f));

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
        // Nominal (non-blink) eye center - used to place the mouth/glasses so they don't jitter
        // with the blink cycle.
        float eyeCenterY = center.y + bobY + EyeYOffset;
        float eyeY = eyeCenterY - eyeHeight / 2f;

        eyeLeft.style.height = eyeHeight;
        eyeLeft.style.left = center.x - EyeSpacing - EyeSize / 2f + shakeX;
        eyeLeft.style.top = eyeY;

        eyeRight.style.height = eyeHeight;
        eyeRight.style.left = center.x + EyeSpacing - EyeSize / 2f + shakeX;
        eyeRight.style.top = eyeY;

        // "Developer" costume: round glasses while actively editing/running code - a small,
        // static (no continuous motion) visual joke rather than another moving part.
        bool showGlasses = !flashing && (activity == CharacterActivity.Editing || activity == CharacterActivity.Running);
        DisplayStyle glassesDisplay = showGlasses ? DisplayStyle.Flex : DisplayStyle.None;
        glassesLeft.style.display = glassesDisplay;
        glassesRight.style.display = glassesDisplay;
        glassesBridge.style.display = glassesDisplay;
        if (showGlasses)
        {
            glassesLeft.style.left = center.x - GlassesOffset - GlassesLensSize / 2f + shakeX;
            glassesLeft.style.top = eyeCenterY - GlassesLensSize / 2f;
            glassesRight.style.left = center.x + GlassesOffset - GlassesLensSize / 2f + shakeX;
            glassesRight.style.top = eyeCenterY - GlassesLensSize / 2f;
            float bridgeWidth = 2f * GlassesOffset - GlassesLensSize;
            glassesBridge.style.width = bridgeWidth;
            glassesBridge.style.left = center.x - bridgeWidth / 2f + shakeX;
            glassesBridge.style.top = eyeCenterY - 0.75f;
        }

        // Thought bubble - shown only while genuinely Thinking (not while a concrete tool is
        // running, which already has its own orbit-dot/glasses tells).
        bool showThought = !flashing && activity == CharacterActivity.Thinking;
        DisplayStyle thoughtDisplay = showThought ? DisplayStyle.Flex : DisplayStyle.None;
        thoughtBubble.style.display = thoughtDisplay;
        thoughtTailBig.style.display = thoughtDisplay;
        thoughtTailSmall.style.display = thoughtDisplay;
        for (int i = 0; i < thoughtDots.Length; i++)
        {
            thoughtDots[i].style.display = thoughtDisplay;
        }
        if (showThought)
        {
            float headTopX = center.x;
            float headTopY = center.y - BodySize / 2f + bobY;

            thoughtTailBig.style.left = headTopX + 5f + shakeX;
            thoughtTailBig.style.top = headTopY - 9f;
            thoughtTailSmall.style.left = headTopX + 2f + shakeX;
            thoughtTailSmall.style.top = headTopY - 4f;

            float bubbleLeft = headTopX + 8f - ThoughtBubbleWidth / 2f + shakeX;
            float bubbleTop = headTopY - ThoughtBubbleHeight - 15f;
            thoughtBubble.style.left = bubbleLeft;
            thoughtBubble.style.top = bubbleTop;

            // A gentle "..." pulse well slower than the mouth-chatter this replaced, so it
            // reads as calm thinking rather than the earlier "flapping" complaint.
            for (int i = 0; i < thoughtDots.Length; i++)
            {
                float dotPhase = (float)(t * 2.5) - i * 0.5f;
                float dotOpacity = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(dotPhase));
                thoughtDots[i].style.opacity = dotOpacity;
                thoughtDots[i].style.left = bubbleLeft + 4f + i * 6f;
                thoughtDots[i].style.top = bubbleTop + ThoughtBubbleHeight / 2f - 1.5f;
            }
        }

        float mouthWidth;
        float mouthHeight;
        if (flashing && !flashIsError)
        {
            mouthWidth = 22f;
            mouthHeight = 10f;
        }
        else if (flashing)
        {
            mouthWidth = 10f;
            mouthHeight = 3f;
        }
        else if (activity == CharacterActivity.Thinking)
        {
            // A small round, static "pondering" mouth - the thought bubble above now carries
            // the animated part of this expression.
            mouthWidth = 8f;
            mouthHeight = 8f;
        }
        else
        {
            // Calm and static for every other state (including busy) - a continuously
            // oscillating mouth read as fish-like flapping (user report, 2026-07-16); glasses/
            // the thought bubble/orbit dots carry "is working" now instead.
            mouthWidth = 12f;
            mouthHeight = 2.5f;
        }

        mouth.style.width = mouthWidth;
        mouth.style.height = mouthHeight;
        mouth.style.left = center.x - mouthWidth / 2f + shakeX;
        mouth.style.top = center.y + bobY + MouthYOffset - mouthHeight / 2f;

        stateLabel.text = label;
    }

    private static float RingWave(float phase)
    {
        return 0.5f + 0.5f * Mathf.Sin(phase);
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
