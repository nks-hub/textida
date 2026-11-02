using System.Diagnostics;
using NksHub.Textida.Core.Agents;
using Xunit.Abstractions;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P8b.3.2: the exact offline count of the OpenAI preset with the bundled o200k_base vocabulary. The expected numbers come from the reference tokenizer, Python tiktoken 0.14.0,
/// len(tiktoken.get_encoding("o200k_base").encode(text)) for the same strings.
/// </summary>
public class TokenCountTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, int> Pinned => new()
    {
        { "empty", "", 0 },
        { "english", "The quick brown fox jumps over the lazy dog. Tokenizers split text into pieces that a model reads.", 21 },
        { "czech", "Příliš žluťoučký kůň úpěl ďábelské ódy. Řeřicha, ještě, čtvrť, ďolíček.", 40 },
        { "code", "public static int Add(int a, int b)\n{\n    return a + b; // sum\n}\n", 21 },
        { "emoji", "Hello \U0001F44B\U0001F3FD world \U0001F30D! \U0001F1E8\U0001F1FF \U0001F468‍\U0001F469‍\U0001F467‍\U0001F466", 24 },
        { "crlf", "line one\r\nline two\r\n\tindented\r\n", 10 },
    };

    [Theory]
    [MemberData(nameof(Pinned))]
    public void The_openai_preset_counts_exactly_as_tiktoken_o200k_base(string name, string text, int expected)
    {
        var count = TokenEstimator.Count(text, TokenEstimator.OpenAi);
        Assert.True(count.Exact, name);
        Assert.Equal(expected, count.Tokens);
    }

    [Fact]
    public void The_pieces_of_a_multiple_selection_are_counted_one_by_one()
    {
        var count = TokenEstimator.Count(["The quick brown fox jumps over the lazy dog. Tokenizers split text into pieces that a model reads.", "line one\r\nline two\r\n\tindented\r\n"], TokenEstimator.OpenAi);
        Assert.Equal(new TokenCount(31, true), count);
    }

    [Fact]
    public void The_other_presets_stay_estimates()
    {
        const string text = "Příliš žluťoučký kůň úpěl ďábelské ódy. Řeřicha, ještě, čtvrť, ďolíček.";
        foreach (var preset in TokenEstimator.Presets.Where(p => p != TokenEstimator.OpenAi))
        {
            Assert.Null(preset.Encoding);
            Assert.Equal(new TokenCount(TokenEstimator.Estimate(text.Length, preset), false), TokenEstimator.Count(text, preset));
        }
        Assert.Equal(TokenEstimator.O200kBase, TokenEstimator.OpenAi.Encoding);
    }

    [Fact]
    public void A_text_over_the_cap_is_estimated_and_one_at_the_cap_is_counted()
    {
        string atCap = string.Concat(Enumerable.Repeat("The quick brown fox jumps over the lazy dog. ", TokenEstimator.ExactMaxCharacters / 45 + 1))[..TokenEstimator.ExactMaxCharacters];
        var watch = Stopwatch.StartNew();
        var exact = TokenEstimator.Count(atCap, TokenEstimator.OpenAi);
        output.WriteLine($"o200k_base: {atCap.Length} chars, {exact.Tokens} tokens, {watch.ElapsedMilliseconds} ms");
        Assert.True(exact.Exact);

        var over = TokenEstimator.Count(atCap + "x", TokenEstimator.OpenAi);
        Assert.Equal(new TokenCount(TokenEstimator.Estimate(atCap.Length + 1, TokenEstimator.OpenAi), false), over);
        Assert.False(TokenEstimator.Count([atCap, "x"], TokenEstimator.OpenAi).Exact);
    }
}
