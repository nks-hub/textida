using Microsoft.ML.Tokenizers;

namespace NksHub.Textida.Core.Agents;

/// <summary>
/// A way to count tokens. <see cref="CharsPerToken"/> and <see cref="Factor"/> are the rule of thumb a vendor publishes for its models; <see cref="Source"/> names the public page.
/// <see cref="Encoding"/> names the tokenizer encoding bundled with the editor for an exact count, or is null when the preset only estimates; an estimate is always shown as one.
/// </summary>
public sealed record TokenPreset(string Id, double CharsPerToken, double Factor, string Source, string? Encoding = null);

/// <summary>A number of tokens; <see cref="Exact"/> is false for an estimate by characters.</summary>
public readonly record struct TokenCount(long Tokens, bool Exact);

/// <summary>
/// Counts or estimates the size of a text in tokens on this computer; the text goes nowhere and nothing is looked up. Decided: the editor counts tokens offline only and sends no text to a
/// service to count it. The OpenAI preset counts exactly with the o200k_base encoding (GPT-4o and later), whose vocabulary ships with the editor (Microsoft.ML.Tokenizers.Data.O200kBase);
/// every other preset estimates by a published rule of thumb and is always labelled as an estimate. Characters are counted as UTF-16 code units, the length of a .NET string.
/// The rules of thumb: OpenAI "approximately 4 characters or 0.75 words for English text" (developers.openai.com/api/docs/concepts), used by the OpenAI preset above <see cref="ExactMaxCharacters"/>;
/// Gemini "about 4 characters" (ai.google.dev/gemini-api/docs/tokens); Claude "approximately 3.5 English characters" on the models before Claude Opus 4.7, and "approximately 30 percent more
/// tokens for the same text" on the newer tokenizer (platform.claude.com glossary and token counting pages). Any other model has no preset: it gets the generic estimate of 4 characters per
/// token, which is the number OpenAI and Google publish.
/// </summary>
public static class TokenEstimator
{
    public const string O200kBase = "o200k_base";

    // 4 Mi characters of C# took 0.5 s on one core with o200k_base (vocabulary load included), so a count stays short on a background thread; a longer text is estimated.
    public const int ExactMaxCharacters = 4 << 20;

    public static readonly TokenPreset Generic = new("generic", 4, 1, "characters / 4");
    public static readonly TokenPreset OpenAi = new("openai", 4, 1, "github.com/openai/tiktoken", O200kBase);
    public static readonly TokenPreset Gemini = new("gemini", 4, 1, "ai.google.dev/gemini-api/docs/tokens");
    public static readonly TokenPreset Claude = new("claude", 3.5, 1, "platform.claude.com/docs/en/about-claude/glossary");
    public static readonly TokenPreset ClaudeNewTokenizer = new("claude47", 3.5, 1.3, "platform.claude.com/docs/en/about-claude/glossary");

    public static IReadOnlyList<TokenPreset> Presets { get; } = [Generic, OpenAi, Gemini, Claude, ClaudeNewTokenizer];

    private static readonly Lazy<Tokenizer> O200k = new(() => TiktokenTokenizer.CreateForEncoding(O200kBase));

    public static TokenPreset ById(string? id) => Presets.FirstOrDefault(p => p.Id == id) ?? Generic;

    /// <summary>The estimate for a text of <paramref name="characters"/> characters, rounded up; 0 for an empty text.</summary>
    public static long Estimate(long characters, TokenPreset preset) =>
        characters <= 0 ? 0 : (long)Math.Ceiling(characters * preset.Factor / preset.CharsPerToken);

    public static long Estimate(ReadOnlySpan<char> text, TokenPreset preset) => Estimate(text.Length, preset);

    /// <summary>True when <see cref="Count"/> gives an exact number for this many characters under this preset.</summary>
    public static bool CountsExactly(long characters, TokenPreset preset) => preset.Encoding == O200kBase && characters <= ExactMaxCharacters;

    /// <summary>
    /// The tokens of the texts (the pieces of a multiple selection are counted one by one and added): exact with the encoding of the preset when it has one and the texts together are not longer
    /// than <see cref="ExactMaxCharacters"/>, otherwise the estimate. The exact count takes time in proportion to the text, so a caller on the UI thread runs it on a background thread.
    /// </summary>
    public static TokenCount Count(IReadOnlyList<string> texts, TokenPreset preset)
    {
        long characters = texts.Sum(t => (long)t.Length);
        if (!CountsExactly(characters, preset)) return new(Estimate(characters, preset), false);
        return new(texts.Sum(t => (long)O200k.Value.CountTokens(t)), true);
    }

    public static TokenCount Count(string text, TokenPreset preset) => Count([text], preset);
}
