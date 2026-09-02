// Cross-cutting Companion settings read from outside AiCompanionWindow (namely
// CompanionSession, which builds the text actually sent to the CLI) without threading a
// window reference through every session instance. AiCompanionWindow's own Language
// property (backed by a [SerializeField] int, persisted like SoundEnabled/SoundVariant)
// writes here on every change and on OnEnable, so a domain reload doesn't lose it even though
// this static field itself does.
public static class CompanionPreferences
{
    public enum Language
    {
        Korean,
        English,
    }

    public static Language ResponseLanguage = Language.Korean;
}
