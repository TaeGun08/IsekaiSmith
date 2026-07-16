// What the character stage should visually represent right now - derived from tool_use
// activity in CompanionSession (see CompanionSession.CurrentActivity), independent of any UI
// framework. Kept coarse-grained (grouped by "kind of work", not exact tool name) so the
// visual language stays simple and legible instead of needing a distinct look per tool.
public enum CharacterActivity
{
    Idle,
    Thinking,
    Reading,
    Editing,
    Running,
}
