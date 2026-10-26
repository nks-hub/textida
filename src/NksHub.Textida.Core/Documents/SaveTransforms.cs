using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <param name="TrimTrailingWhitespace">Remove spaces and tabs at the end of the lines that were edited since the last save.</param>
/// <param name="InsertFinalNewline">True adds a line ending at the end of an edited document that lacks one, false removes it, null leaves it.</param>
/// <param name="NewLineStyle">Line ending to add when the document has none of its own; LF when null.</param>
public sealed record SaveTransformOptions(bool TrimTrailingWhitespace, bool? InsertFinalNewline = null, EolStyle? NewLineStyle = null);

/// <param name="Edits">Ascending, disjoint edits against the current revision; apply them with <see cref="Document.Apply"/> before the save.</param>
/// <param name="Note">Why nothing was planned for a part of the request (for the status bar); null when everything asked for was considered.</param>
public sealed record SaveTransformPlan(IReadOnlyList<ByteEdit> Edits, string? Note = null);

/// <summary>
/// The edits EditorConfig (or a setting) asks for at save time. Only lines that were edited since the last save are trimmed (so a save never
/// rewrites the whitespace of code nobody touched), line endings are never converted (an existing file keeps its CRLF or LF; the final
/// newline uses the document's own dominant ending), and the document's encoding is not involved. Without a change log back to the last save the
/// edited lines are not known: nothing is trimmed and the plan says so. A document that was never saved counts as edited everywhere.
/// </summary>
public static class SaveTransforms
{
    /// <summary>Documents over this size are not decoded as a whole for the plan.</summary>
    public const long MaxBytes = 64L << 20;

    private static readonly char[] Breaks = ['\n', '\r'];

    public static SaveTransformPlan Plan(Document doc, SaveTransformOptions options)
    {
        bool isNew = doc.Fingerprint is null;
        IReadOnlyList<ByteRange>? changed = isNew ? [new ByteRange(0, doc.ByteLength)] : doc.ChangedRangesSince(doc.SavedRevision);
        if (changed is null) return new SaveTransformPlan([], "The change history is too short to know which lines were edited, so nothing was trimmed.");
        if (changed.Count == 0) return new SaveTransformPlan([]);
        if (doc.ByteLength > MaxBytes) return new SaveTransformPlan([], "The document is too large for trimming on save.");

        var dt = doc.Codec.Decode(doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
        string text = dt.Text;
        var edits = new List<ByteEdit>();

        if (options.TrimTrailingWhitespace)
        {
            var starts = new SortedSet<int>();
            foreach (var range in changed)
            {
                int first = dt.CharIndexOf(range.Start), last = dt.CharIndexOf(range.End);
                for (int pos = LineStart(text, first); ;)
                {
                    starts.Add(pos);
                    int end = LineEnd(text, pos);
                    if (end >= last || end >= text.Length) break;
                    pos = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
                }
            }
            foreach (int start in starts)
            {
                int end = LineEnd(text, start), keep = end;
                while (keep > start && text[keep - 1] is ' ' or '\t') keep--;
                if (keep < end) edits.Add(ByteEdit.Delete(new ByteRange(dt.ByteOffsetOf(keep), dt.ByteOffsetOf(end))));
            }
        }

        if (options.InsertFinalNewline is { } wanted && text.Length > 0)
        {
            bool endsWithBreak = text[^1] is '\n' or '\r';
            if (wanted && !endsWithBreak)
            {
                string seq = Eol.ForNewLine(Eol.Analyze(text), null, options.NewLineStyle ?? EolStyle.Lf);
                edits.Add(ByteEdit.InsertAt(doc.ByteLength, doc.Codec.Encode(seq)));
            }
            else if (!wanted && endsWithBreak)
            {
                int len = text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : 1;
                edits.Add(ByteEdit.Delete(new ByteRange(dt.ByteOffsetOf(text.Length - len), doc.ByteLength)));
            }
        }
        return new SaveTransformPlan([.. edits.OrderBy(e => e.Range.Start).ThenBy(e => e.Range.Length)]);
    }

    private static int LineStart(string text, int index)
    {
        if (index <= 0 || text.Length == 0) return 0;
        int i = text.LastIndexOfAny(Breaks, Math.Min(index, text.Length) - 1);
        return i < 0 ? 0 : i + 1;
    }

    private static int LineEnd(string text, int start)
    {
        int i = text.AsSpan(start).IndexOfAny('\n', '\r');
        return i < 0 ? text.Length : start + i;
    }

    /// <summary>Plans and applies the transforms as one edit (one undo step); the result is null when there was nothing to do.</summary>
    public static (SaveTransformPlan Plan, EditResult? Result) Apply(Document doc, SaveTransformOptions options)
    {
        var plan = Plan(doc, options);
        return (plan, plan.Edits.Count == 0 ? null : doc.Apply(plan.Edits, new EditOptions { Source = "save-transform" }));
    }
}
