using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8.6.3: the words that changed inside a changed line, for the peek at a change in the editor.</summary>
public class WordDiffTests
{
    private static string[] Changed(string text, IReadOnlyList<WordSpan> spans) => [.. spans.Select(s => text.Substring(s.Start, s.Length))];

    [Fact]
    public void A_changed_value_in_a_line_marks_that_word_on_both_sides()
    {
        var diff = WordDiff.Compute("var count = 10;", "var count = 25;");
        Assert.Equal(["10"], Changed("var count = 10;", diff.Old));
        Assert.Equal(["25"], Changed("var count = 25;", diff.New));
    }

    [Fact]
    public void An_inserted_word_is_marked_on_the_new_side_only()
    {
        var diff = WordDiff.Compute("if (ready) run();", "if (ready && armed) run();");
        Assert.Empty(diff.Old);
        Assert.Equal("&&armed", string.Concat(Changed("if (ready && armed) run();", diff.New)).Replace(" ", ""));
    }

    [Fact]
    public void A_removed_word_is_marked_on_the_old_side_only()
    {
        var diff = WordDiff.Compute("alpha beta gamma", "alpha gamma");
        Assert.Empty(diff.New);
        Assert.Equal("beta", string.Concat(Changed("alpha beta gamma", diff.Old)).Trim());
    }

    [Fact]
    public void Equal_lines_have_no_changed_words_and_unrelated_lines_are_changed_whole()
    {
        var same = WordDiff.Compute("same text", "same text");
        Assert.Empty(same.Old);
        Assert.Empty(same.New);
        var other = WordDiff.Compute("abc", "xyz");
        Assert.Equal([new WordSpan(0, 3)], other.Old);
        Assert.Equal([new WordSpan(0, 3)], other.New);
        var empty = WordDiff.Compute("", "new");
        Assert.Empty(empty.Old);
        Assert.Equal([new WordSpan(0, 3)], empty.New);
    }

    [Fact]
    public void Changes_apart_stay_apart_and_neighbouring_changes_join()
    {
        var diff = WordDiff.Compute("a1 b2 c3", "a9 b2 c8");
        Assert.Equal(["a1", "c3"], Changed("a1 b2 c3", diff.Old));
        Assert.Equal(["a9", "c8"], Changed("a9 b2 c8", diff.New));
        var joined = WordDiff.Compute("x.y.z", "x.q.z");
        Assert.Equal(["y"], Changed("x.y.z", joined.Old));
    }

    [Fact]
    public void A_very_long_line_is_marked_from_the_first_to_the_last_different_piece_without_a_quadratic_table()
    {
        string left = string.Join(' ', Enumerable.Range(0, 3000).Select(i => "w" + i));
        string right = left.Replace("w1500 ", "changed ", StringComparison.Ordinal).Replace("w2500 ", "other ", StringComparison.Ordinal);
        var diff = WordDiff.Compute(left, right);
        Assert.Single(diff.Old);
        Assert.Single(diff.New);
        Assert.StartsWith("w1500", left[diff.Old[0].Start..]);
        Assert.True(diff.Old[0].Start + diff.Old[0].Length <= left.Length);
        Assert.StartsWith("changed", right[diff.New[0].Start..]);
    }

    [Fact]
    public void Surrogate_pairs_are_never_cut()
    {
        string left = "a \U0001F600 b", right = "a \U0001F601 b";
        var diff = WordDiff.Compute(left, right);
        Assert.Equal(["\U0001F600"], Changed(left, diff.Old));
        Assert.Equal(["\U0001F601"], Changed(right, diff.New));
    }
}
