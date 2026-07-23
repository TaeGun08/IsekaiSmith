using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Procedural vector "character" for the Companion window's status stage, built from plain
// VisualElements (position: absolute + border-radius: 50% via the "stage-circle" USS class)
// instead of a hand-tinted Texture2D circle. Nothing here calls MarkDirtyRepaint - the host
// window drives Tick() on its own schedule (see AiCompanionWindow.OnTick), and UI Toolkit
// only actually repaints a panel when a style/layout value changes, so this stays cheap while
// idle instead of forcing a full-window redraw every frame the way the old IMGUI version did.
public class CharacterStageElement : VisualElement
{
    // Activity-state palette + labels now live in AiCharacterConcept (see SetConcept below) so
    // a different AI provider can look/read differently - this class only knows how to draw
    // whichever concept it's currently given, defaulting to the original Claude palette.
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

    // "Personal space" set dressing (2026-07-16 request) - a light flat-vector desk/monitor/
    // plant instead of the reference photo's full pixel-art room (the stage is a ~130px-tall
    // strip, nowhere near enough room for that level of detail, and a static illustration
    // would've meant an imported texture asset + import pipeline for what amounts to a strip
    // of background dressing). Same VisualElement/border-radius approach as the character
    // itself, so it costs nothing extra beyond a few more style writes per tick.
    private const float DeskHeight = 7f;
    private const float DeskWidthRatio = 0.92f;
    private const float MonitorOffsetX = 74f;
    // Bumped up from 30x24 - at the old size the monitor read as an unrecognizable dark blob
    // rather than a PC (user report, 2026-07-23: "PC 모양이 너무 작아서 PC인지 티도 안나"). A
    // stand+base under it (below) is what actually sells "screen sitting on the desk" though -
    // the old version had the monitor's bottom edge flush with the desk with nothing connecting
    // them.
    private const float MonitorBodyWidth = 42f;
    private const float MonitorBodyHeight = 32f;
    private const float MonitorScreenInset = 4f;
    private const float MonitorStandWidth = 6f;
    private const float MonitorStandHeight = 6f;
    private const float MonitorBaseWidth = 18f;
    private const float MonitorBaseHeight = 3f;
    private const float KeyboardWidth = 26f;
    private const float KeyboardHeight = 5f;
    private const float MugSize = 8f;
    private const float PlantOffsetX = 68f;
    private static readonly Color DeskColor = new Color(0.30f, 0.20f, 0.14f);
    private static readonly Color MonitorBodyColor = new Color(0.16f, 0.15f, 0.15f);
    private static readonly Color MonitorStandColor = new Color(0.12f, 0.11f, 0.11f);
    private static readonly Color KeyboardColor = new Color(0.20f, 0.19f, 0.18f);
    private static readonly Color MugColor = new Color(0.75f, 0.42f, 0.30f);
    private static readonly Color PlantPotColor = new Color(0.55f, 0.30f, 0.20f);
    private static readonly Color PlantLeafColor = new Color(0.30f, 0.56f, 0.32f);

    // In-place "what am I actually doing" acting (option A) - the 2026-07-23 mockup's C+A
    // walking hybrid read as more confusing/ambiguous in practice than the original stationary
    // character (user report: "캐릭터 애니메이션이 더 애매해졌어"), so locomotion was pulled back
    // out entirely; the character stays put and acts out each activity in place instead. This is
    // deliberately the *only* motion system now, layered on the existing bob/color/ring system.
    private const float HandSize = 6f;
    private const float BookWidth = 14f;
    private const float BookHeight = 10f;
    private const float LoaderSize = 14f;
    private const int CodeLineCount = 3;
    private const int SparkleCount = 3;
    private const int SweatDropCount = 2;
    // Gentle "alive while standing still" sway - gives Idle/Thinking some life without the
    // spatial repositioning that made the walk version feel busy/unclear.
    private const float SwayAmplitudeDegrees = 2.5f;
    private const float SwaySpeed = 0.8f;

    // Character sits a fixed distance above the desk line instead of at the stage's vertical
    // center - so expanding the stage (see Expanded below) only adds empty room *above* the
    // character's head for the backdrop image, instead of also shifting the character/desk
    // downward. Chosen to reproduce the exact old (collapsed) look: at the original 132px
    // stage height this offset lands the character exactly where the old height/2 formula did.
    private const float CharacterGroundOffset = 50f;

    private const string RoomBackdropAssetPath = "Assets/03. Art/Sprites/CompanionRoomBackdrop.png";
    private const float ExpandedStageHeight = 240f;

    private static Texture2D cachedRoomBackdrop;

    private readonly VisualElement desk;
    private readonly VisualElement monitorBody;
    private readonly VisualElement monitorScreen;
    private readonly VisualElement monitorStand;
    private readonly VisualElement monitorBase;
    private readonly VisualElement keyboard;
    private readonly VisualElement mug;
    private readonly VisualElement plantPot;
    private readonly VisualElement[] plantLeaves;
    private readonly VisualElement roomBackdrop;
    private readonly VisualElement groundShadow;
    private readonly VisualElement bodyShine;
    private readonly Button expandButton;
    private bool isExpanded;

    // In-place acting state (2026-07-23, option A).
    private readonly VisualElement handLeft;
    private readonly VisualElement handRight;
    private readonly VisualElement book;
    private readonly VisualElement loaderRing;
    private readonly VisualElement[] codeLines;
    private readonly VisualElement[] sparkles;
    private readonly VisualElement[] sweatDrops;

    private readonly VisualElement haloOuter;
    private readonly VisualElement haloInner;
    private readonly VisualElement body;
    private readonly VisualElement ring;
    private readonly VisualElement eyeLeft;
    private readonly VisualElement eyeRight;
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

    // Thinking gaps between tool calls during a real turn are often sub-second (round-trip to
    // the next tool_use), so showing the thought bubble only exactly while
    // activity==Thinking made it flicker in and out too fast to actually see (user report,
    // 2026-07-16). This "lingers" it for a beat after the most recent Thinking tick instead.
    private double lastThinkingTime = double.NegativeInfinity;
    private const double ThoughtLingerSeconds = 0.8;

    private AiCharacterConcept concept = AiCharacterConcept.Claude;

    // Called by the host window once per session bind (RebuildMainColumn) - swaps which AI's
    // palette/labels this stage draws without recreating the element itself.
    public void SetConcept(AiCharacterConcept newConcept)
    {
        concept = newConcept ?? AiCharacterConcept.Claude;
    }

    public CharacterStageElement()
    {
        AddToClassList("character-stage");

        // Backdrop paints behind absolutely everything - only visible/sized when Expanded (see
        // below). A real (cropped, character-free) strip from the user's reference room image
        // rather than a generated one - actual pixel art instead of an approximation of it.
        roomBackdrop = new VisualElement();
        roomBackdrop.AddToClassList("stage-room-backdrop");
        roomBackdrop.style.display = DisplayStyle.None;
        roomBackdrop.pickingMode = PickingMode.Ignore;
        Texture2D backdropTexture = GetRoomBackdropTexture();
        if (backdropTexture != null)
        {
            roomBackdrop.style.backgroundImage = new StyleBackground(backdropTexture);
        }
        Add(roomBackdrop);

        // Desk/monitor/plant are added first so the character (added further below) always
        // paints on top of them, sitting "in front of" its little desk setup.
        desk = new VisualElement();
        desk.AddToClassList("stage-desk");
        desk.style.backgroundColor = DeskColor;
        desk.pickingMode = PickingMode.Ignore;
        Add(desk);

        // Stand + base under the monitor - without these the old monitor just floated flush
        // against the desk with nothing visually connecting them, part of why it didn't read as
        // a PC (2026-07-23 request).
        monitorBase = new VisualElement();
        monitorBase.AddToClassList("stage-monitor-base");
        monitorBase.style.backgroundColor = MonitorStandColor;
        monitorBase.pickingMode = PickingMode.Ignore;
        Add(monitorBase);

        monitorStand = new VisualElement();
        monitorStand.AddToClassList("stage-monitor-stand");
        monitorStand.style.backgroundColor = MonitorStandColor;
        monitorStand.pickingMode = PickingMode.Ignore;
        Add(monitorStand);

        monitorBody = new VisualElement();
        monitorBody.AddToClassList("stage-monitor-body");
        monitorBody.style.backgroundColor = MonitorBodyColor;
        monitorBody.pickingMode = PickingMode.Ignore;
        Add(monitorBody);

        monitorScreen = new VisualElement();
        monitorScreen.AddToClassList("stage-monitor-screen");
        monitorScreen.pickingMode = PickingMode.Ignore;
        Add(monitorScreen);

        // Code lines only shown while Editing - see Tick.
        codeLines = new VisualElement[CodeLineCount];
        for (int i = 0; i < codeLines.Length; i++)
        {
            VisualElement line = new VisualElement();
            line.AddToClassList("stage-code-line");
            line.style.display = DisplayStyle.None;
            codeLines[i] = line;
            Add(line);
        }

        // Spinning loader ring on the screen only while Running - see Tick.
        loaderRing = new VisualElement();
        loaderRing.AddToClassList("stage-loader-ring");
        loaderRing.style.display = DisplayStyle.None;
        loaderRing.pickingMode = PickingMode.Ignore;
        Add(loaderRing);

        keyboard = new VisualElement();
        keyboard.AddToClassList("stage-keyboard");
        keyboard.style.backgroundColor = KeyboardColor;
        keyboard.pickingMode = PickingMode.Ignore;
        Add(keyboard);

        mug = new VisualElement();
        mug.AddToClassList("stage-mug");
        mug.style.backgroundColor = MugColor;
        mug.pickingMode = PickingMode.Ignore;
        Add(mug);

        plantPot = new VisualElement();
        plantPot.AddToClassList("stage-plant-pot");
        plantPot.style.backgroundColor = PlantPotColor;
        plantPot.pickingMode = PickingMode.Ignore;
        Add(plantPot);

        plantLeaves = new VisualElement[3];
        for (int i = 0; i < plantLeaves.Length; i++)
        {
            VisualElement leaf = MakeCircle(9f);
            leaf.RemoveFromClassList("stage-circle");
            leaf.AddToClassList("stage-plant-leaf");
            leaf.style.backgroundColor = PlantLeafColor;
            plantLeaves[i] = leaf;
            Add(leaf);
        }

        // A flattened, fixed-to-the-ground ellipse under the character - a classic 2D
        // platformer trick to fake "the character is a solid 3D-ish object above the floor"
        // instead of a flat circle floating on a flat background (user report, 2026-07-16).
        // Shrinks slightly when the character bobs up (see Tick), like the shadow reacting to
        // increasing "height".
        groundShadow = new VisualElement();
        groundShadow.AddToClassList("stage-ground-shadow");
        groundShadow.pickingMode = PickingMode.Ignore;
        Add(groundShadow);

        // Halo layers are added first so they paint behind the body (VisualElement children
        // paint in the order they were added, like a painter's algorithm, same as the desk/
        // monitor/plant above painting behind everything else) - two overlapping, low-alpha
        // circles of decreasing opacity fake a soft radial glow, since USS has no
        // radial-gradient support to do this with a single element.
        haloOuter = MakeCircle(HaloOuterSize);
        Add(haloOuter);

        haloInner = MakeCircle(HaloInnerSize);
        Add(haloInner);

        body = MakeCircle(BodySize);
        Add(body);

        // A small glossy highlight on the body's upper-left, offset from center - the classic
        // "cute mascot" trick for making a flat-shaded circle read as a rounded 3D object
        // instead of a flat disc (user report, 2026-07-16: "too flat a character").
        bodyShine = MakeCircle(16f);
        bodyShine.RemoveFromClassList("stage-circle");
        bodyShine.AddToClassList("stage-body-shine");
        Add(bodyShine);

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

        // In-place acting props (option A of the 2026-07-23 mockup) - hands bounce while
        // Editing (typing), a book appears while Reading. All hidden by default, shown/moved in
        // Tick only for their matching activity.
        handLeft = MakeCircle(HandSize);
        handLeft.style.display = DisplayStyle.None;
        Add(handLeft);

        handRight = MakeCircle(HandSize);
        handRight.style.display = DisplayStyle.None;
        Add(handRight);

        book = new VisualElement();
        book.AddToClassList("stage-book");
        book.style.display = DisplayStyle.None;
        book.pickingMode = PickingMode.Ignore;
        Add(book);

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

        // Success sparkles - a few "✦" glyphs that pop briefly around the head on FlashSuccess
        // (see Tick), on top of the existing scale-pop reaction.
        sparkles = new VisualElement[SparkleCount];
        for (int i = 0; i < sparkles.Length; i++)
        {
            Label sparkle = new Label("✦");
            sparkle.AddToClassList("stage-sparkle");
            sparkle.style.display = DisplayStyle.None;
            sparkle.pickingMode = PickingMode.Ignore;
            sparkles[i] = sparkle;
            Add(sparkle);
        }

        // Error sweat drops - a couple of small droplets that appear and fall briefly on
        // FlashError, next to the existing shake reaction.
        sweatDrops = new VisualElement[SweatDropCount];
        for (int i = 0; i < sweatDrops.Length; i++)
        {
            VisualElement drop = MakeCircle(5f);
            drop.RemoveFromClassList("stage-circle");
            drop.AddToClassList("stage-sweat-drop");
            drop.style.display = DisplayStyle.None;
            sweatDrops[i] = drop;
            Add(drop);
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

        // Overlay toggle, top-right corner - collapsed (default) keeps the compact bar so the
        // room doesn't eat into chat space; expanded shows the backdrop + more breathing room
        // above the character. Added last so it's always clickable on top of everything else.
        expandButton = new Button(() => Expanded = !isExpanded) { text = "⤢ 펼치기" };
        expandButton.AddToClassList("stage-expand-button");
        Add(expandButton);
    }

    // The window owns persistence (a plain [SerializeField] bool, same pattern as
    // turnStepperCollapsed) - this just applies the visual side and reports back via the event
    // so the window can save it.
    public event Action<bool> ExpandedChanged;

    public bool Expanded
    {
        get => isExpanded;
        set
        {
            isExpanded = value;
            EnableInClassList("character-stage--expanded", isExpanded);
            roomBackdrop.style.display = isExpanded ? DisplayStyle.Flex : DisplayStyle.None;
            expandButton.text = isExpanded ? "⤡ 접기" : "⤢ 펼치기";
            ExpandedChanged?.Invoke(isExpanded);
        }
    }

    private static Texture2D GetRoomBackdropTexture()
    {
        if (cachedRoomBackdrop == null)
        {
            cachedRoomBackdrop = AssetDatabase.LoadAssetAtPath<Texture2D>(RoomBackdropAssetPath);
        }
        return cachedRoomBackdrop;
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
        float deskTop = characterAreaHeight - DeskHeight;
        // Anchored a fixed distance above the desk rather than the stage's vertical center, so
        // Expanded (see below) only adds empty room above the character's head instead of also
        // dragging the character/desk down the middle of a much taller stage.
        Vector2 center = new Vector2(width / 2f, deskTop - CharacterGroundOffset);

        // Desk/monitor/plant - purely static geometry (no bob/color dependency yet), so this
        // only actually changes when the stage is resized, but recomputing it every tick is
        // cheap enough not to bother caching.
        float deskWidth = width * DeskWidthRatio;
        desk.style.width = deskWidth;
        desk.style.left = width / 2f - deskWidth / 2f;
        desk.style.top = deskTop;

        if (isExpanded)
        {
            roomBackdrop.style.width = width;
            roomBackdrop.style.height = deskTop;
        }

        // Stand + base sit between the monitor and the desk surface so it visibly reads as
        // "a screen propped up on the desk" instead of a dark rectangle flush against it
        // (2026-07-23 request). monitorLeft/monitorTop etc. are cached here (room-fixed, not
        // affected by the character's own walk offset below) for the code-line/loader-ring
        // acting props further down.
        float monitorCenterX = center.x - MonitorOffsetX;
        float monitorBaseTop = deskTop - MonitorBaseHeight;
        float monitorStandTop = monitorBaseTop - MonitorStandHeight;
        float monitorTop = monitorStandTop - MonitorBodyHeight;
        float monitorLeft = monitorCenterX - MonitorBodyWidth / 2f;
        float screenLeft = monitorLeft + MonitorScreenInset;
        float screenTop = monitorTop + MonitorScreenInset;
        float screenWidth = MonitorBodyWidth - MonitorScreenInset * 2f;
        float screenHeight = MonitorBodyHeight - MonitorScreenInset * 2f;

        monitorBase.style.left = monitorCenterX - MonitorBaseWidth / 2f;
        monitorBase.style.top = monitorBaseTop;
        monitorBase.style.width = MonitorBaseWidth;
        monitorBase.style.height = MonitorBaseHeight;

        monitorStand.style.left = monitorCenterX - MonitorStandWidth / 2f;
        monitorStand.style.top = monitorStandTop;
        monitorStand.style.width = MonitorStandWidth;
        monitorStand.style.height = MonitorStandHeight;

        monitorBody.style.left = monitorLeft;
        monitorBody.style.top = monitorTop;
        monitorScreen.style.left = screenLeft;
        monitorScreen.style.top = screenTop;

        keyboard.style.left = monitorCenterX - KeyboardWidth / 2f;
        keyboard.style.top = deskTop - KeyboardHeight;

        mug.style.left = monitorLeft + MonitorBodyWidth + 8f;
        mug.style.top = deskTop - MugSize;

        float potHeight = 10f;
        float potWidth = 16f;
        plantPot.style.left = center.x + PlantOffsetX - potWidth / 2f;
        plantPot.style.top = deskTop - potHeight;
        float leafBaseX = center.x + PlantOffsetX;
        float leafBaseY = deskTop - potHeight;
        Vector2[] leafOffsets = { new Vector2(-5f, -10f), new Vector2(5f, -10f), new Vector2(0f, -16f) };
        for (int i = 0; i < plantLeaves.Length; i++)
        {
            plantLeaves[i].style.left = leafBaseX + leafOffsets[i].x - 4.5f;
            plantLeaves[i].style.top = leafBaseY + leafOffsets[i].y;
        }

        float bobAmplitude = busy ? 7f : 3f;
        float bobSpeed = busy ? 6f : 2f;
        float bobY = Mathf.Sin((float)t * bobSpeed) * bobAmplitude;

        GetActivityStyle(concept, activity, out Color colorA, out Color colorB, out string label);
        if (flashing)
        {
            colorA = colorB = flashIsError ? concept.ErrorColor : concept.SuccessColor;
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

        // Success sparkles - a few "✦" glyphs popping around the head, on top of the existing
        // scale-pop reaction (reuses the same flash progress).
        bool showSparkles = flashing && !flashIsError;
        for (int i = 0; i < sparkles.Length; i++)
        {
            VisualElement sparkle = sparkles[i];
            sparkle.style.display = showSparkles ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showSparkles)
            {
                continue;
            }
            float sparkleProgress = Mathf.Clamp01((float)(t - flashStart) / (float)SuccessFlashSeconds);
            float sparkleAngle = (i / (float)sparkles.Length) * Mathf.PI * 2f - Mathf.PI / 2f;
            float sparkleRadius = BodySize / 2f + 6f + sparkleProgress * 14f;
            sparkle.style.left = center.x + Mathf.Cos(sparkleAngle) * sparkleRadius - 6f;
            sparkle.style.top = center.y + bobY + Mathf.Sin(sparkleAngle) * sparkleRadius * 0.7f - 6f;
            sparkle.style.opacity = 1f - sparkleProgress;
        }

        // Error sweat drops - a couple of droplets that appear near the head and fall slightly
        // over the error flash, next to the existing shake reaction.
        bool showSweat = flashing && flashIsError;
        for (int i = 0; i < sweatDrops.Length; i++)
        {
            VisualElement drop = sweatDrops[i];
            drop.style.display = showSweat ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showSweat)
            {
                continue;
            }
            float dropProgress = Mathf.Clamp01((float)(t - flashStart) / (float)ErrorFlashSeconds);
            float dropSide = i == 0 ? -1f : 1f;
            drop.style.left = center.x + dropSide * (BodySize / 2f - 4f) - 2.5f;
            drop.style.top = center.y + bobY - BodySize / 2f + 2f + dropProgress * 16f;
            drop.style.opacity = 1f - dropProgress;
        }

        // The monitor's own "screen" glows with whatever color the character currently is -
        // ties the little desk setup into the same state signal instead of being pure static
        // dressing, at no extra cost (reusing bodyColor).
        monitorScreen.style.backgroundColor = bodyColor;

        // Editing: a few "code line" ticks scroll on the screen instead of a flat color fill -
        // in-place acting (option A of the 2026-07-23 mockup), room-fixed like the monitor
        // itself (not the character's walk offset).
        bool showCodeLines = !flashing && activity == CharacterActivity.Editing;
        for (int i = 0; i < codeLines.Length; i++)
        {
            VisualElement line = codeLines[i];
            line.style.display = showCodeLines ? DisplayStyle.Flex : DisplayStyle.None;
            if (!showCodeLines)
            {
                continue;
            }
            float linePhase = (float)(t * 3.0) + i * 0.7f;
            line.style.width = screenWidth * (0.35f + 0.35f * (0.5f + 0.5f * Mathf.Sin(linePhase)));
            line.style.left = screenLeft + 1.5f;
            line.style.top = screenTop + 2f + i * (screenHeight / (codeLines.Length + 1));
        }

        // Running: a spinning loader ring on the screen instead of a flat color fill.
        bool showLoader = !flashing && activity == CharacterActivity.Running;
        loaderRing.style.display = showLoader ? DisplayStyle.Flex : DisplayStyle.None;
        if (showLoader)
        {
            loaderRing.style.left = monitorCenterX - LoaderSize / 2f;
            loaderRing.style.top = screenTop + screenHeight / 2f - LoaderSize / 2f;
            loaderRing.style.rotate = new Rotate(new Angle((float)t * 260f, AngleUnit.Degree));
        }

        // Squash & stretch tied to the bob itself (classic animation principle) instead of a
        // separate timer, so it can't drift out of sync: stretched (taller/narrower) near the
        // top of the bob, squashed (shorter/wider) near the bottom.
        float normalizedBob = bobAmplitude > 0f ? bobY / bobAmplitude : 0f;
        float squash = busy ? normalizedBob * 0.07f : normalizedBob * 0.03f;
        body.style.scale = new Scale(new Vector3(bodyScale * (1f - squash), bodyScale * (1f + squash), 1f));

        // Gentle standing-still sway (Idle/Thinking only - Editing/Running/Reading already have
        // their own acting cues below, and flashing has its own shake/pop) so the character
        // still reads as "alive" without needing to actually move around the stage (the walking
        // version tried for this and read as more confusing than lively - user report,
        // 2026-07-23: "캐릭터 애니메이션이 더 애매해졌어").
        bool showSway = !flashing && (activity == CharacterActivity.Idle || activity == CharacterActivity.Thinking);
        float swayDegrees = showSway ? Mathf.Sin((float)t * SwaySpeed) * SwayAmplitudeDegrees : 0f;
        body.style.rotate = new Rotate(new Angle(swayDegrees, AngleUnit.Degree));

        // Glossy highlight, fixed offset from the body's own top-left regardless of bob (moves
        // with the body, doesn't independently animate).
        const float shineSize = 16f;
        bodyShine.style.left = center.x - BodySize / 2f + 8f + shakeX;
        bodyShine.style.top = center.y - BodySize / 2f + 6f + bobY;
        bodyShine.style.opacity = 0.30f;

        // Shadow stays on the ground line (doesn't bob with the body) and shrinks slightly
        // when the character is higher up, like it's reacting to the character's "height".
        const float shadowWidth = 46f;
        const float shadowHeight = 11f;
        float shadowShrink = 1f - Mathf.Max(0f, -normalizedBob) * 0.25f;
        groundShadow.style.width = shadowWidth * shadowShrink;
        groundShadow.style.height = shadowHeight;
        groundShadow.style.left = center.x - (shadowWidth * shadowShrink) / 2f;
        groundShadow.style.top = deskTop - shadowHeight / 2f - 2f;

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
        // Nominal (non-blink) eye center - used to place the glasses so they don't jitter with
        // the blink cycle.
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

        // In-place acting props (option A): hands bounce like typing while Editing, a small
        // book appears while Reading - both hang just below the body, independent of the
        // glasses costume above.
        bool showHands = !flashing && activity == CharacterActivity.Editing;
        handLeft.style.display = showHands ? DisplayStyle.Flex : DisplayStyle.None;
        handRight.style.display = showHands ? DisplayStyle.Flex : DisplayStyle.None;
        if (showHands)
        {
            handLeft.style.backgroundColor = concept.EditingColorB;
            handRight.style.backgroundColor = concept.EditingColorB;
            float handBobLeft = Mathf.Sin((float)t * 14f) * 2.5f;
            float handBobRight = Mathf.Sin((float)t * 14f + Mathf.PI) * 2.5f;
            float handBaseY = center.y + bobY + MouthYOffset + 8f;
            handLeft.style.left = center.x - 9f - HandSize / 2f + shakeX;
            handLeft.style.top = handBaseY + handBobLeft;
            handRight.style.left = center.x + 9f - HandSize / 2f + shakeX;
            handRight.style.top = handBaseY + handBobRight;
        }

        bool showBook = !flashing && activity == CharacterActivity.Reading;
        book.style.display = showBook ? DisplayStyle.Flex : DisplayStyle.None;
        if (showBook)
        {
            // A slow width pulse reads as a page being turned every couple of seconds, instead
            // of a static prop.
            float pageFlip = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin((float)t * 1.4f));
            float bookWidth = BookWidth * pageFlip;
            book.style.width = bookWidth;
            book.style.left = center.x - bookWidth / 2f + shakeX;
            book.style.top = center.y + bobY + MouthYOffset + 4f;
        }

        // Thought bubble - shown only while genuinely Thinking (not while a concrete tool is
        // running, which already has its own orbit-dot/glasses tells).
        if (activity == CharacterActivity.Thinking)
        {
            lastThinkingTime = t;
        }
        bool showThought = !flashing && (t - lastThinkingTime) < ThoughtLingerSeconds;
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
            // Clamped so an upward bob doesn't push the bubble's top edge past the stage's own
            // top boundary (the stage clips overflow - user report, 2026-07-23: "생각할 때
            // 말풍선 모양이 잘리다 말았어").
            float bubbleTop = Mathf.Max(2f, headTopY - ThoughtBubbleHeight - 15f);
            // width/height were never set here before (only left/top), so the bubble actually
            // rendered at 0x0 - just the tail dots and "..." floated with no visible container
            // around them, reading as "unfinished" rather than a thought bubble (user report,
            // 2026-07-23: "말풍선인지도 모르겠어. 만들다 만 것 같아").
            thoughtBubble.style.width = ThoughtBubbleWidth;
            thoughtBubble.style.height = ThoughtBubbleHeight;
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

        // No mouth element at all - a static line, then a Thinking-only pondering shape, then a
        // success/error pop were each tried in turn and every one of them still read as
        // "flapping" once the character sat through a real multi-turn conversation (user
        // reports 2026-07-16 and 2026-07-23, most recently "뻐끔뻐끔하는데") - so the mouth is
        // gone; the eyes (eyeOpen below) and thought bubble carry the expression instead.
        stateLabel.text = label;
    }

    private static float RingWave(float phase)
    {
        return 0.5f + 0.5f * Mathf.Sin(phase);
    }

    private static void GetActivityStyle(AiCharacterConcept concept, CharacterActivity activity, out Color colorA, out Color colorB, out string label)
    {
        switch (activity)
        {
            case CharacterActivity.Thinking:
                colorA = concept.ThinkingColorA;
                colorB = concept.ThinkingColorB;
                label = concept.ThinkingLabel;
                break;
            case CharacterActivity.Reading:
                colorA = concept.ReadingColorA;
                colorB = concept.ReadingColorB;
                label = concept.ReadingLabel;
                break;
            case CharacterActivity.Editing:
                colorA = concept.EditingColorA;
                colorB = concept.EditingColorB;
                label = concept.EditingLabel;
                break;
            case CharacterActivity.Running:
                colorA = concept.RunningColorA;
                colorB = concept.RunningColorB;
                label = concept.RunningLabel;
                break;
            default:
                colorA = colorB = concept.IdleBodyColor;
                label = concept.IdleLabel;
                break;
        }
    }

    // Shared with the sidebar's per-session busy dots (see AiCompanionWindow.OnAnimationTick)
    // so a background tab's dot hints at what it's doing without needing to switch to it.
    // concept defaults to Claude for call sites that don't have a specific session's concept
    // handy (e.g. a raw CharacterActivity with no session context).
    public static Color GetIndicatorColor(CharacterActivity activity, AiCharacterConcept concept = null)
    {
        concept = concept ?? AiCharacterConcept.Claude;
        GetActivityStyle(concept, activity, out Color colorA, out Color colorB, out _);
        return activity == CharacterActivity.Idle ? colorA : colorB;
    }

    private void UpdateBlink(double t)
    {
        if (nextBlinkTime <= 0)
        {
            nextBlinkTime = t + UnityEngine.Random.Range(2f, 5f);
        }

        if (!isBlinking && t >= nextBlinkTime)
        {
            isBlinking = true;
            blinkEndTime = t + 0.12;
        }
        else if (isBlinking && t >= blinkEndTime)
        {
            isBlinking = false;
            nextBlinkTime = t + UnityEngine.Random.Range(2f, 5f);
        }
    }
}
