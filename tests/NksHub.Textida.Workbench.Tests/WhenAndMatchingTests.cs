using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.Workbench.Tests;

public class WhenExpressionTests
{
    private static bool Eval(string expr, WhenContext ctx) => WhenExpression.Parse(expr).Evaluate(ctx);

    [Fact]
    public void Each_core_context_key_is_evaluated()
    {
        var on = new WhenContext();
        var off = new WhenContext();
        foreach (var key in new[] { ContextKeys.EditorFocus, ContextKeys.HasDocument, ContextKeys.HasSelection, ContextKeys.ReadOnly, ContextKeys.LargeFileMode, ContextKeys.IsDirty })
        {
            on.Set(key, true);
            off.Set(key, false);
            Assert.True(Eval(key, on), key);
            Assert.False(Eval(key, off), key);
            Assert.False(Eval(key, new WhenContext()), key + " missing");
            Assert.True(Eval("!" + key, off), key);
        }

        var cs = new WhenContext().Set(ContextKeys.Language, "csharp").Set(ContextKeys.Platform, "macos");
        Assert.True(Eval("language == 'csharp'", cs));
        Assert.True(Eval("language == csharp && platform == macos", cs));
        Assert.False(Eval("language != 'csharp'", cs));
        Assert.True(Eval("platform != \"windows\"", cs));
        Assert.False(Eval("language == 'json'", cs));
        Assert.True(Eval("language != 'json'", new WhenContext()));
    }

    [Theory]
    [InlineData("a || b && c", true, false, true, true)]
    [InlineData("(a || b) && c", true, false, true, true)]
    [InlineData("(a || b) && c", true, false, false, false)]
    [InlineData("!a || b", true, false, false, false)]
    [InlineData("!(a && b)", true, true, false, false)]
    [InlineData("a && !b", true, false, false, true)]
    [InlineData("true && !false", false, false, false, true)]
    [InlineData("false || a", true, false, false, true)]
    public void Operators_and_precedence(string expr, bool a, bool b, bool c, bool expected)
    {
        var ctx = new WhenContext().Set("a", a).Set("b", b).Set("c", c);
        Assert.Equal(expected, Eval(expr, ctx));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a &&")]
    [InlineData("&& a")]
    [InlineData("(a")]
    [InlineData("a)")]
    [InlineData("a == ")]
    [InlineData("a = b")]
    [InlineData("a & b")]
    [InlineData("a == 'x")]
    [InlineData("a $ b")]
    [InlineData("1abc")]
    [InlineData("a b")]
    public void Malformed_expressions_are_rejected_with_a_message(string expr)
    {
        Assert.False(WhenExpression.TryParse(expr, out var parsed, out var error));
        Assert.Null(parsed);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Throws<FormatException>(() => WhenExpression.Parse(expr));
    }

    [Fact]
    public void Length_and_depth_are_limited()
    {
        Assert.False(WhenExpression.TryParse(new string('a', WhenExpression.MaxLength + 1), out _, out _));
        Assert.False(WhenExpression.TryParse(new string('(', 100) + "a" + new string(')', 100), out _, out _));
        Assert.False(WhenExpression.TryParse(new string('!', 100) + "a", out _, out _));
        Assert.True(WhenExpression.TryParse(new string('(', 10) + "a" + new string(')', 10), out _, out _));
    }

    [Fact]
    public void Canonical_text_parses_back_to_the_same_expression()
    {
        foreach (var text in new[] { "a || b && !c", "!(a || b)", "language == 'c\\'s' && (x || y)" })
        {
            var canonical = WhenExpression.Parse(text).ToString();
            Assert.Equal(canonical, WhenExpression.Parse(canonical).ToString());
        }
    }

    [Fact]
    public void Keys_lists_what_the_expression_reads()
    {
        Assert.Equal(["a", "b", "language"], WhenExpression.Parse("a && !b || language == 'x'").Keys.Order());
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("a", null, true)]
    [InlineData("a", "!a", false)]
    [InlineData("a && b", "!b", false)]
    [InlineData("a || b", "!a", true)]
    [InlineData("language == 'c'", "language == 'd'", false)]
    [InlineData("language == 'c'", "language != 'c'", false)]
    [InlineData("language == 'c'", "language != 'd'", true)]
    [InlineData("language != 'c'", "language != 'd'", true)]
    [InlineData("editorFocus", "editorFocus && !readOnly", true)]
    [InlineData("false", null, false)]
    public void Overlap_is_found_by_search_over_assignments(string? left, string? right, bool expected)
    {
        var a = left is null ? null : WhenExpression.Parse(left);
        var b = right is null ? null : WhenExpression.Parse(right);
        Assert.Equal(expected, WhenExpression.CanBothBeTrue(a, b));
        Assert.Equal(expected, WhenExpression.CanBothBeTrue(b, a));
    }

    [Fact]
    public void Overlap_is_assumed_when_the_search_space_is_too_large()
    {
        var wide = string.Join(" && ", Enumerable.Range(0, 20).Select(i => $"k{i} == 'x'"));
        Assert.True(WhenExpression.CanBothBeTrue(WhenExpression.Parse(wide), WhenExpression.Parse("k0 == 'y'")));
    }
}

public class TextMatchingTests
{
    [Theory]
    [InlineData("Kódování souboru", "kodovani souboru")]
    [InlineData("ŘEŽ Ťuk", "rez tuk")]
    [InlineData("Łódź", "lodz")]
    [InlineData("", "")]
    public void Fold_removes_case_and_diacritics(string input, string expected) => Assert.Equal(expected, TextMatching.Fold(input));

    [Fact]
    public void Fuzzy_requires_a_subsequence()
    {
        Assert.Null(TextMatching.FuzzyScore("xyz", "File: Save"));
        Assert.Null(TextMatching.FuzzyScore("saves", "Save"));
        Assert.NotNull(TextMatching.FuzzyScore("fsa", "File: Save"));
        Assert.Equal(0, TextMatching.FuzzyScore("", "anything"));
    }

    [Fact]
    public void Fuzzy_prefers_consecutive_and_word_start_matches()
    {
        var exact = TextMatching.FuzzyScore("save", "Save All")!.Value;
        var scattered = TextMatching.FuzzyScore("save", "Select All Vertical Edit")!.Value;
        var wordStarts = TextMatching.FuzzyScore("sa", "Save As")!.Value;
        var inside = TextMatching.FuzzyScore("sa", "Transaction")!.Value;
        Assert.True(exact > scattered);
        Assert.True(wordStarts > inside);
    }

    [Fact]
    public void Fuzzy_ignores_diacritics_and_case()
    {
        Assert.NotNull(TextMatching.FuzzyScore("PREJIT", "Přejít na řádek"));
        Assert.NotNull(TextMatching.FuzzyScore("přejít", "prejit"));
    }
}
