using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Completion;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.5.5: the words offered from the text of open documents.</summary>
public class WordCompletionTests
{
    private static List<string> Words(string prefix, params CompletionSource[] sources) => WordCompletion.Words(sources, prefix, 100);

    [Theory]
    [InlineData("foo bar", 7, 4)]
    [InlineData("foo bar", 3, 0)]
    [InlineData("foo ", 4, 4)]
    [InlineData("a_b9 é", 6, 5)]
    [InlineData("", 0, 0)]
    public void The_prefix_starts_after_the_last_character_that_is_not_part_of_a_word(string text, int caret, int start) => Assert.Equal(start, WordCompletion.PrefixStart(text, caret));

    [Fact]
    public void Words_start_with_the_prefix_ignoring_case_and_are_longer_than_it()
    {
        Assert.Equal(["Config", "config_path", "configure"], Words("con", new CompletionSource("Config configure con config_path other")).Order(StringComparer.Ordinal));
        Assert.Empty(Words("zz", new CompletionSource("alpha beta")));
        Assert.Empty(Words("con", new CompletionSource("con con con")));
    }

    [Fact]
    public void The_word_being_typed_is_not_offered_but_the_same_word_elsewhere_is()
    {
        Assert.Equal(["total", "totals"], Words("tot", new CompletionSource("total totals tot", 16)));

        // the caret in the middle of a word: that whole word is the one being typed
        Assert.Empty(Words("tot", new CompletionSource("totals", 3)));
        Assert.Equal(["total"], Words("tot", new CompletionSource("total totals", 11)));
    }

    [Fact]
    public void Words_of_the_first_document_come_first_then_the_more_frequent_ones_then_alphabetical()
    {
        Assert.Equal(["pear", "plum", "pine"], Words("p", new CompletionSource("pear pear", 0), new CompletionSource("plum plum plum apple pine pine")));
        Assert.Equal(["plum", "pine", "pear"], Words("p", new CompletionSource("none"), new CompletionSource("pear plum plum pine pine plum")));
        Assert.Equal(["pine"], Words("pi", new CompletionSource("plum"), new CompletionSource("pine pine pine")));
        Assert.Equal(["Apple", "apple2", "banana"], Words("", new CompletionSource("banana Apple apple2")));
    }

    [Fact]
    public void Without_a_prefix_every_word_is_a_candidate_and_the_list_is_cut_to_the_best()
    {
        Assert.Equal(["three", "two", "one"], Words("", new CompletionSource("one two two three three three")));
        Assert.Equal(["three", "two"], WordCompletion.Words([new CompletionSource("one two two three three three")], "", 2));
        Assert.Empty(WordCompletion.Words([new CompletionSource("one two")], "", 0));
    }

    [Fact]
    public void Masked_text_offers_nothing_of_what_was_hidden()
    {
        // the caller masks the hidden values before it gives the text, so only what is left can be offered
        Assert.Equal(["API_KEY", "TOKEN"], Words("", new CompletionSource("API_KEY=•••• TOKEN=••••")).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void A_megabyte_of_text_is_searched_for_candidates_in_33_ms()
    {
        var rng = new Random(7);
        var sb = new StringBuilder(1 << 20);
        while (sb.Length < 1 << 20) sb.Append("word").Append(rng.Next(20_000)).Append(rng.Next(3) == 0 ? "\n" : " ");
        var source = new CompletionSource(sb.ToString(), 5);
        WordCompletion.Words([source], "wor", 200); // warm up the JIT
        var sw = Stopwatch.StartNew();
        var words = WordCompletion.Words([source], "word1", 200);
        sw.Stop();
        Assert.NotEmpty(words);
        Assert.True(sw.ElapsedMilliseconds <= 33, $"{sw.ElapsedMilliseconds} ms");
    }
}
