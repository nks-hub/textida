using System.Text;

namespace NksHub.Textida.Core.Data;

/// <summary>What a Lorem ipsum text is counted in.</summary>
public enum LoremUnit { Paragraphs, Sentences, Words }

/// <summary>
/// Placeholder text in the manner of Lorem ipsum. The text is always the same for the same request (no randomness), so a test or a diff of a generated file is stable: it begins with the classic opening, goes on through
/// a fixed list of words in a fixed pattern of sentence lengths, and a paragraph holds four to six sentences.
/// </summary>
public static class LoremIpsum
{
    public const int MaxCount = 10_000;

    private static readonly string[] Words =
    [
        "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", "enim", "ad", "minim", "veniam", "quis",
        "nostrud", "exercitation", "ullamco", "laboris", "nisi", "aliquip", "ex", "ea", "commodo", "consequat", "duis", "aute", "irure", "in", "reprehenderit", "voluptate", "velit", "esse", "cillum", "eu", "fugiat", "nulla",
        "pariatur", "excepteur", "sint", "occaecat", "cupidatat", "non", "proident", "sunt", "culpa", "qui", "officia", "deserunt", "mollit", "anim", "id", "est", "laborum", "praesent", "libero", "vestibulum", "ante", "primis",
        "faucibus", "orci", "luctus", "ultrices", "posuere", "cubilia", "curae", "integer", "nec", "odio", "donec", "sollicitudin", "molestie", "malesuada", "vivamus", "magnam", "pellentesque", "habitant", "morbi", "tristique",
    ];

    private static readonly int[] SentenceLengths = [8, 12, 6, 15, 9, 11, 7, 14, 10];
    private static readonly int[] ParagraphSentences = [5, 4, 6, 4, 5];

    /// <summary>
    /// <paramref name="count"/> paragraphs (separated by a blank line), sentences (separated by a space) or words (separated by a space) from <paramref name="newLine"/>; a count below 1 is 1 and above <see cref="MaxCount"/> is
    /// <see cref="MaxCount"/>. Words are lower case without punctuation; sentences start with a capital and end with a period.
    /// </summary>
    public static string Generate(LoremUnit unit, int count, string newLine = "\n")
    {
        count = Math.Clamp(count, 1, MaxCount);
        var cursor = new Cursor();
        var sb = new StringBuilder();
        switch (unit)
        {
            case LoremUnit.Words:
                for (int i = 0; i < count; i++) sb.Append(i > 0 ? " " : "").Append(cursor.NextWord());
                break;
            case LoremUnit.Sentences:
                for (int i = 0; i < count; i++) sb.Append(i > 0 ? " " : "").Append(cursor.NextSentence());
                break;
            default:
                for (int p = 0; p < count; p++)
                {
                    if (p > 0) sb.Append(newLine).Append(newLine);
                    int sentences = ParagraphSentences[p % ParagraphSentences.Length];
                    for (int i = 0; i < sentences; i++) sb.Append(i > 0 ? " " : "").Append(cursor.NextSentence());
                }
                break;
        }
        return sb.ToString();
    }

    private sealed class Cursor
    {
        private int _word, _sentence;

        public string NextWord() => Words[_word++ % Words.Length];

        public string NextSentence()
        {
            int length = SentenceLengths[_sentence++ % SentenceLengths.Length];
            var sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                string word = NextWord();
                if (i == 0) word = char.ToUpperInvariant(word[0]) + word[1..];
                sb.Append(i > 0 ? (i == length / 2 && length > 8 ? ", " : " ") : "").Append(word);
            }
            return sb.Append('.').ToString();
        }
    }
}
