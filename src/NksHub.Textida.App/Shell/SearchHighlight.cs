using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Marks where the words of a search stand in a text. The search ignores case and marks on letters, so the text is folded one text element at a time, which keeps track of where each folded
/// letter came from; "Kódování" is marked whole for "kodovani", and "Schließen" for "schliessen".
/// </summary>
internal static class SearchHighlight
{
    /// <summary>The words of a query, folded, shortest repeats dropped; the same words the settings search matches.</summary>
    public static IReadOnlyList<string> Words(string query) => [.. TextMatching.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct()];

    /// <summary>The ranges of <paramref name="text"/> (start and length in its own characters) that hold one of the folded words; overlapping and touching ranges are one.</summary>
    public static IReadOnlyList<(int Start, int Length)> Ranges(string text, IReadOnlyList<string> foldedWords)
    {
        if (text.Length == 0 || foldedWords.Count == 0) return [];
        var folded = new StringBuilder(text.Length);
        var origin = new List<(int Start, int Length)>(text.Length);
        for (var elements = StringInfo.GetTextElementEnumerator(text); elements.MoveNext();)
        {
            string element = elements.GetTextElement();
            foreach (char c in TextMatching.Fold(element))
            {
                folded.Append(c);
                origin.Add((elements.ElementIndex, element.Length));
            }
        }
        string hay = folded.ToString();
        var found = new List<(int Start, int End)>();
        foreach (var word in foldedWords)
        {
            if (word.Length == 0) continue;
            for (int at = hay.IndexOf(word, StringComparison.Ordinal); at >= 0; at = hay.IndexOf(word, at + word.Length, StringComparison.Ordinal))
            {
                var last = origin[at + word.Length - 1];
                found.Add((origin[at].Start, last.Start + last.Length));
            }
        }
        found.Sort();
        var merged = new List<(int Start, int Length)>();
        foreach (var (start, end) in found)
        {
            if (merged.Count > 0 && start <= merged[^1].Start + merged[^1].Length) merged[^1] = (merged[^1].Start, Math.Max(merged[^1].Start + merged[^1].Length, end) - merged[^1].Start);
            else merged.Add((start, end - start));
        }
        return merged;
    }

    /// <summary>Puts the text into the block with the matches bold on the mark; without a match the block just holds the text.</summary>
    public static void Fill(TextBlock block, string text, IReadOnlyList<string> foldedWords, IBrush? mark)
    {
        var ranges = Ranges(text, foldedWords);
        block.Inlines?.Clear();
        if (ranges.Count == 0)
        {
            block.Text = text;
            return;
        }
        block.Text = null;
        int at = 0;
        foreach (var (start, length) in ranges)
        {
            if (start > at) block.Inlines!.Add(new Run(text[at..start]));
            block.Inlines!.Add(new Run(text.Substring(start, length)) { FontWeight = FontWeight.Bold, Background = mark });
            at = start + length;
        }
        if (at < text.Length) block.Inlines!.Add(new Run(text[at..]));
    }

    /// <summary>The text of a block filled by <see cref="Fill"/>, whole.</summary>
    public static string TextOf(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : block.Text ?? "";

    /// <summary>The parts of the text of a block that are marked.</summary>
    public static IReadOnlyList<string> MarkedOf(TextBlock block) => block.Inlines is { Count: > 0 } inlines ? [.. inlines.OfType<Run>().Where(r => r.FontWeight == FontWeight.Bold).Select(r => r.Text ?? "")] : [];
}
