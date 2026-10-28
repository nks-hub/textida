namespace NksHub.Textida.Core.Agents;

/// <summary>
/// A way to estimate tokens from characters. <see cref="CharsPerToken"/> and <see cref="Factor"/> are the rule of thumb a vendor publishes for its models; <see cref="Source"/> names the public page.
/// None of them is the tokenizer of the model: the result is an estimate, for text in English, and it is always shown as one.
/// </summary>
public sealed record TokenPreset(string Id, double CharsPerToken, double Factor, string Source);

/// <summary>
/// Estimates the size of a text in tokens on this computer; the text goes nowhere and nothing is looked up. Characters are counted as UTF-16 code units, the length of a .NET string.
/// The presets are the published rules of thumb: OpenAI "approximately 4 characters or 0.75 words for English text" (developers.openai.com/api/docs/concepts); Gemini "about 4 characters"
/// (ai.google.dev/gemini-api/docs/tokens); Claude "approximately 3.5 English characters" on the models before Claude Opus 4.7, and "approximately 30 percent more tokens for the same text" on the newer
/// tokenizer (platform.claude.com glossary and token counting pages). Any other model has no preset: it gets the generic estimate of 4 characters per token, which is the number OpenAI and Google publish.
/// </summary>
public static class TokenEstimator
{
    public static readonly TokenPreset Generic = new("generic", 4, 1, "characters / 4");
    public static readonly TokenPreset OpenAi = new("openai", 4, 1, "developers.openai.com/api/docs/concepts");
    public static readonly TokenPreset Gemini = new("gemini", 4, 1, "ai.google.dev/gemini-api/docs/tokens");
    public static readonly TokenPreset Claude = new("claude", 3.5, 1, "platform.claude.com/docs/en/about-claude/glossary");
    public static readonly TokenPreset ClaudeNewTokenizer = new("claude47", 3.5, 1.3, "platform.claude.com/docs/en/about-claude/glossary");

    public static IReadOnlyList<TokenPreset> Presets { get; } = [Generic, OpenAi, Gemini, Claude, ClaudeNewTokenizer];

    public static TokenPreset ById(string? id) => Presets.FirstOrDefault(p => p.Id == id) ?? Generic;

    /// <summary>The estimate for a text of <paramref name="characters"/> characters, rounded up; 0 for an empty text.</summary>
    public static long Estimate(long characters, TokenPreset preset) =>
        characters <= 0 ? 0 : (long)Math.Ceiling(characters * preset.Factor / preset.CharsPerToken);

    public static long Estimate(ReadOnlySpan<char> text, TokenPreset preset) => Estimate(text.Length, preset);
}
