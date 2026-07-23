using UnityEngine;

// One AI provider's visual identity for the character stage - palette per activity state plus
// the status-label copy. Plain data (not a ScriptableObject) so it needs no asset file to exist
// for the tool to work; step 2 of the multi-provider plan (2026-07-23) - only the built-in
// Claude concept exists so far, wired through CharacterStageElement/CompanionSession so future
// GPT/Codex/Cursor/Gemini concepts (see [[project_claude_companion_multi_provider]] once written)
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
}
