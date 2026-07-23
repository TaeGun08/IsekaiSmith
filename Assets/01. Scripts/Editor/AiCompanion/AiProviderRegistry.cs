using System;
using System.Collections.Generic;

// Which AI backend a session talks to. Claude is first (and must stay first) so an old
// sessions.json written before this enum existed still deserializes to Claude (JsonUtility
// leaves a missing field at its C# default, which is the enum's 0 value).
public enum AiProviderId
{
    Claude = 0,
    // 1 (formerly Gpt) intentionally retired 2026-07-23 - Codex CLI already covers OpenAI/GPT,
    // a separate "GPT" slot had no distinct backend to point to. Not reused, to keep any
    // already-serialized sessions.json entries from that brief window from silently remapping.
    Codex = 2,
    Cursor = 3,
    Antigravity = 4,
}

// One entry in the provider picker (step 3 of the multi-provider plan, 2026-07-23) - pairs a
// provider with its character concept and a factory for the runner that actually talks to it.
// Only Claude is wired to a real backend so far; the others exist so the picker UI and the rest
// of the app (concept theming, session records) already have a slot for them, instead of this
// again being a bigger refactor once Codex/Cursor/Antigravity support actually lands.
public sealed class AiProviderDefinition
{
    public AiProviderId Id;
    public string DisplayName;
    public bool IsImplemented;
    public AiCharacterConcept Concept;
    public Func<string, IAiSessionRunner> CreateRunner;

    // Install-prompt support (2026-07-23 request: offer to install a missing CLI instead of
    // just failing silently). Null IsInstalled means "no installer wired up for this provider
    // yet" - true for every not-yet-implemented provider today, since NotImplementedSessionRunner
    // doesn't launch anything a PATH check could even verify.
    public Func<bool> IsInstalled;
    public string InstallPackage;
}

public static class AiProviderRegistry
{
    public static readonly IReadOnlyList<AiProviderDefinition> All = new List<AiProviderDefinition>
    {
        new AiProviderDefinition
        {
            Id = AiProviderId.Claude,
            DisplayName = "Claude",
            IsImplemented = true,
            Concept = AiCharacterConcept.Claude,
            CreateRunner = workingDirectory => new ClaudeSessionRunner(workingDirectory),
            IsInstalled = ClaudeSessionRunner.IsInstalled,
            InstallPackage = ClaudeSessionRunner.NpmPackage,
        },
        new AiProviderDefinition
        {
            Id = AiProviderId.Codex,
            DisplayName = "Codex",
            IsImplemented = true,
            Concept = AiCharacterConcept.Codex,
            CreateRunner = workingDirectory => new CodexSessionRunner(workingDirectory),
            IsInstalled = CodexSessionRunner.IsInstalled,
            InstallPackage = CodexSessionRunner.NpmPackage,
        },
        new AiProviderDefinition
        {
            Id = AiProviderId.Cursor,
            DisplayName = "Cursor",
            IsImplemented = true,
            Concept = AiCharacterConcept.Cursor,
            CreateRunner = workingDirectory => new CursorSessionRunner(workingDirectory),
            IsInstalled = CursorSessionRunner.IsInstalled,
            // No InstallPackage: Cursor's official CLI install is a curl|bash script, not an
            // npm package, so OfferInstall falls back to a manual-install dialog for this one.
        },
        new AiProviderDefinition
        {
            // Backend deliberately left unimplemented (2026-07-23): antigravity-cli's headless
            // "-p" mode has open upstream bugs that hit exactly this app's invocation pattern -
            // no conversation id surfaced for resume (issue #7), and stdout silently dropped
            // (or hangs) when run as a non-TTY subprocess (issues #76, #318) - implementing now
            // would likely just produce empty responses. Revisit once those are confirmed fixed.
            Id = AiProviderId.Antigravity,
            DisplayName = "Antigravity",
            IsImplemented = false,
            Concept = AiCharacterConcept.Antigravity,
            CreateRunner = workingDirectory => new NotImplementedSessionRunner("Antigravity"),
        },
    };

    public static AiProviderDefinition Get(AiProviderId id)
    {
        foreach (AiProviderDefinition definition in All)
        {
            if (definition.Id == id)
            {
                return definition;
            }
        }
        return All[0];
    }
}
