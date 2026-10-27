using AvaloniaEdit.Rendering;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// Draws <see cref="SecretValues.Placeholder"/> in place of every secret of the document (see <see cref="SecretValues.Find"/>). Only the picture changes:
/// the text, the offsets and the bytes stay, so saving, searching and editing behave as without it. A secret that spans lines is drawn as one placeholder per line.
/// </summary>
/// <param name="spans">The secrets in order of position, or null while nothing is hidden. Read for every visual line that is built.</param>
public sealed class SecretMaskGenerator(Func<IReadOnlyList<SecretSpan>?> spans) : VisualLineElementGenerator
{
    /// <summary>The secret that is shown in plain at the moment (the one the pointer rests on).</summary>
    public SecretSpan? Shown { get; set; }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        if (spans() is not { Count: > 0 } list) return -1;
        int lineEnd = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
        foreach (var span in list)
        {
            if (span.End <= startOffset || span == Shown) continue;
            int at = Math.Max(span.Start, startOffset);
            return at < lineEnd ? at : -1;
        }
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        if (spans() is not { } list || SecretValues.SpanAt(list, offset) is not { } span || span == Shown) return null;
        int length = Math.Min(span.End, CurrentContext.VisualLine.LastDocumentLine.EndOffset) - offset;
        return length > 0 ? new FormattedTextElement(SecretValues.Placeholder, length) : null;
    }
}
