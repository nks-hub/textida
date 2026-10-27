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

    private static List<string> Scan(IReadOnlyList<CompletionSource> sources, string prefix, int max)
    {
        var counts = new Dictionary<string, (int Total, bool First)>(StringComparer.Ordinal);
        for (int k = 0; k < sources.Count; k++)
        {
            string text = sources[k].Text;
            for (int i = 0; i < text.Length;)
            {
                if (!WordCompletion.IsWordChar(text[i])) { i++; continue; }
                int start = i;
                while (i < text.Length && WordCompletion.IsWordChar(text[i])) i++;
                if (i - start <= prefix.Length || (sources[k].Caret > start && sources[k].Caret <= i)) continue;
                string word = text[start..i];
                if (!word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                counts.TryGetValue(word, out var c);
                counts[word] = (c.Total + 1, c.First || k == 0);
            }
        }
        return counts.OrderByDescending(p => p.Value.First).ThenByDescending(p => p.Value.Total)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Key, StringComparer.Ordinal).Take(max).Select(p => p.Key).ToList();
    }

    private static string RandomText(Random rng, int words)
    {
        string[] pool = ["Foo", "foo", "FOO", "foobar", "fo", "f", "Bar_1", "bar", "é", "Éa", "éa", "x9", "xx", "X"];
        var sb = new StringBuilder();
        for (int i = 0; i < words; i++) sb.Append(pool[rng.Next(pool.Length)]).Append(rng.Next(4) == 0 ? "\n" : rng.Next(5) == 0 ? ".-" : " ");
        return sb.ToString();
    }

    [Fact]
    public void The_index_offers_what_the_scan_offers_on_random_text_and_after_edits()
    {
        var rng = new Random(11);
        string[] prefixes = ["", "f", "F", "fo", "FOO", "b", "é", "É", "x", "zz", "foobar"];
        for (int round = 0; round < 60; round++)
        {
            string text = RandomText(rng, 80);
            string other = RandomText(rng, 30);
            for (int edit = 0; edit < 4; edit++)
            {
                int caret = rng.Next(3) == 0 ? -1 : rng.Next(text.Length + 1);
                var indexed = new[] { new CompletionSource(text, caret, WordIndex.Build(text)), new CompletionSource(other, -1, WordIndex.Build(other)) };
                var plain = new[] { new CompletionSource(text, caret), new CompletionSource(other) };
                foreach (var prefix in prefixes)
                    foreach (int max in new[] { 3, 100 })
                    {
                        var expected = Scan(plain, prefix, max);
                        Assert.Equal(expected, WordCompletion.Words(indexed, prefix, max));
                        Assert.Equal(expected, WordCompletion.Words(plain, prefix, max));
                    }
                int at = rng.Next(text.Length + 1);
                text = rng.Next(2) == 0 ? text.Insert(at, RandomText(rng, 2)) : text.Remove(at, Math.Min(rng.Next(12), text.Length - at));
            }
        }
    }

    private static List<(string Word, int Total)> Entries(WordIndex index)
    {
        var all = new List<(string, int)>();
        index.ForEachStartingWith("", (w, n) => all.Add((w, n)));
        return all;
    }

    [Fact]
    public void An_index_changed_by_the_lines_of_random_edits_equals_one_built_from_the_text()
    {
        var rng = new Random(5);
        for (int round = 0; round < 80; round++)
        {
            string text = RandomText(rng, 60);
            var index = WordIndex.Build(text);
            for (int edit = 0; edit < 25; edit++)
            {
                int at = rng.Next(text.Length + 1);
                int removal = rng.Next(3) == 0 ? 0 : Math.Min(rng.Next(15), text.Length - at);
                string insert = rng.Next(3) == 0 ? "" : rng.Next(2) == 0 ? "ab\n" : RandomText(rng, 2)[..^1];
                int from = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
                int toOld = text.IndexOf('\n', at + removal) is var t and >= 0 ? t : text.Length;
                string before = text[from..toOld];
                text = text.Remove(at, removal).Insert(at, insert);
                int toNew = text.IndexOf('\n', at + insert.Length) is var u and >= 0 ? u : text.Length;
                index.Change(before, text[from..toNew]);
                Assert.Equal(Entries(WordIndex.Build(text)), Entries(index));
            }
        }
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void A_megabyte_of_text_is_searched_for_candidates_in_33_ms()
    {
        var rng = new Random(7);
        var sb = new StringBuilder(1 << 20);
        while (sb.Length < 1 << 20) sb.Append("word").Append(rng.Next(20_000)).Append(rng.Next(3) == 0 ? "\n" : " ");
        string text = sb.ToString();
        var source = new CompletionSource(text, 5, WordIndex.Build(text)); // the index is built once per revision, not per offer
        WordCompletion.Words([source], "wor", 200); // warm up the JIT
        var runs = new long[20];
        List<string> words = [];
        for (int i = 0; i < runs.Length; i++)
        {
            var sw = Stopwatch.StartNew();
            words = WordCompletion.Words([source], "word1", 200);
            runs[i] = sw.ElapsedTicks;
        }
        Assert.NotEmpty(words);
        long p95 = runs.Order().ElementAt(runs.Length * 95 / 100 - 1) * 1000 / Stopwatch.Frequency;
        Assert.True(p95 <= 33, $"p95 {p95} ms");
    }
}
