using UnityEngine;

// One AI provider's visual identity for the character stage - palette per activity state plus
// the status-label copy. Plain data (not a ScriptableObject) so it needs no asset file to exist
// for the tool to work; step 2 of the multi-provider plan (2026-07-23) - only the built-in
// Claude concept exists so far, wired through CharacterStageElement/CompanionSession so future
// Codex/Cursor/Antigravity concepts (see [[project_claude_companion_multi_provider]] once written)
// only need to add another instance here, not touch the drawing code.
public sealed class AiCharacterConcept
{
    public string DisplayName;

    public Color IdleBodyColor;
    public Color ThinkingColorA;
    public Color ThinkingColorB;
    public Color ReadingColorA;
    public Color ReadingColorB;
    public Color EditingColorA;
    public Color EditingColorB;
    public Color RunningColorA;
    public Color RunningColorB;
    public Color SuccessColor;
    public Color ErrorColor;

    public string IdleLabel = "대기 중";
    public string ThinkingLabel = "생각하는 중...";
    public string ReadingLabel = "파일 읽는 중...";
    public string EditingLabel = "코드 수정 중...";
    public string RunningLabel = "명령 실행 중...";

    // The exact palette CharacterStageElement used before this concept system existed - default
    // for every session until provider selection (step 3) can assign a different one.
    public static readonly AiCharacterConcept Claude = new AiCharacterConcept
    {
        DisplayName = "Claude",
        IdleBodyColor = new Color(0.55f, 0.62f, 0.72f),
        ThinkingColorA = new Color(0.58f, 0.44f, 0.88f),
        ThinkingColorB = new Color(0.72f, 0.62f, 0.96f),
        ReadingColorA = new Color(0.22f, 0.68f, 0.64f),
        ReadingColorB = new Color(0.38f, 0.85f, 0.80f),
        EditingColorA = new Color(1f, 0.62f, 0.25f),
        EditingColorB = new Color(1f, 0.85f, 0.4f),
        RunningColorA = new Color(0.95f, 0.36f, 0.20f),
        RunningColorB = new Color(1f, 0.54f, 0.30f),
        SuccessColor = new Color(0.28f, 0.80f, 0.46f),
        ErrorColor = new Color(0.92f, 0.26f, 0.26f),
    };

    // Placeholder concepts for providers whose backend isn't wired up yet (see
    // AiProviderRegistry) - distinct palettes from the earlier planning table so picking one in
    // the step-3 UI already looks/reads differently even before the AI behind it actually works.
    public static readonly AiCharacterConcept Codex = new AiCharacterConcept
    {
        DisplayName = "Codex",
        IdleBodyColor = new Color(0.40f, 0.52f, 0.48f),
        ThinkingColorA = new Color(0.10f, 0.58f, 0.50f),
        ThinkingColorB = new Color(0.30f, 0.80f, 0.62f),
        ReadingColorA = new Color(0.15f, 0.55f, 0.60f),
        ReadingColorB = new Color(0.30f, 0.78f, 0.75f),
        EditingColorA = new Color(0.80f, 0.70f, 0.18f),
        EditingColorB = new Color(0.95f, 0.85f, 0.32f),
        RunningColorA = new Color(0.08f, 0.68f, 0.50f),
        RunningColorB = new Color(0.22f, 0.88f, 0.60f),
        SuccessColor = new Color(0.15f, 0.72f, 0.52f),
        ErrorColor = new Color(0.92f, 0.26f, 0.26f),
        ThinkingLabel = "코드 분석 중...",
    };

    public static readonly AiCharacterConcept Cursor = new AiCharacterConcept
    {
        DisplayName = "Cursor",
        IdleBodyColor = new Color(0.45f, 0.48f, 0.68f),
        ThinkingColorA = new Color(0.35f, 0.35f, 0.85f),
        ThinkingColorB = new Color(0.55f, 0.55f, 0.98f),
        ReadingColorA = new Color(0.30f, 0.40f, 0.78f),
        ReadingColorB = new Color(0.48f, 0.58f, 0.95f),
        EditingColorA = new Color(0.65f, 0.35f, 0.85f),
        EditingColorB = new Color(0.80f, 0.55f, 1f),
        RunningColorA = new Color(0.30f, 0.35f, 0.80f),
        RunningColorB = new Color(0.45f, 0.50f, 0.98f),
        SuccessColor = new Color(0.35f, 0.65f, 0.90f),
        ErrorColor = new Color(0.92f, 0.26f, 0.26f),
        ThinkingLabel = "탐색하는 중...",
    };

    // Replaces the old Gemini slot (2026-07-23): Google retired the standalone Gemini CLI for
    // individual/free users on 2026-06-18, consolidating dev tooling under the Antigravity brand
    // (announced at Google I/O 2026-05-19) - Antigravity CLI is the actual successor, not just a
    // naming choice. Kept the same blue/cyan palette since it's the same lineage. See
    // AiProviderRegistry for why the backend is still NotImplementedSessionRunner (unresolved
    // headless-subprocess reliability issues upstream, independent of the deprecation).
    public static readonly AiCharacterConcept Antigravity = new AiCharacterConcept
    {
        DisplayName = "Antigravity",
        IdleBodyColor = new Color(0.40f, 0.55f, 0.70f),
        ThinkingColorA = new Color(0.20f, 0.50f, 0.90f),
        ThinkingColorB = new Color(0.45f, 0.75f, 1f),
        ReadingColorA = new Color(0.25f, 0.55f, 0.85f),
        ReadingColorB = new Color(0.45f, 0.78f, 0.98f),
        EditingColorA = new Color(0.55f, 0.40f, 0.90f),
        EditingColorB = new Color(0.75f, 0.60f, 1f),
        RunningColorA = new Color(0.15f, 0.55f, 0.85f),
        RunningColorB = new Color(0.35f, 0.78f, 1f),
        SuccessColor = new Color(0.30f, 0.70f, 0.95f),
        ErrorColor = new Color(0.92f, 0.26f, 0.26f),
        ThinkingLabel = "탐색하는 중...",
    };
}
