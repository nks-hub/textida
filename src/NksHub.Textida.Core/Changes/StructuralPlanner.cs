using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Changes;

/// <summary>
/// Turns the matches of ast-grep with a rewrite into the one change model that the preview, the revision check and the apply path share (PZ.6.5). A file is based on the fingerprint of its content as it was read,
/// an open document on the revision it was sent at, so a change made since is a conflict and the file is left alone. The offsets of ast-grep are UTF-8 bytes, so only UTF-8 content (with or without a byte order mark) is planned.
/// </summary>
public static class StructuralPlanner
{
    public static bool IsUtf8(TextCodec codec) => codec.Kind == CodecKind.Utf8;

    private static List<TextReplacement> Edits(IEnumerable<AstGrepMatch> matches) =>
        [.. matches.Where(m => m.Replacement is not null).Select(m => new TextReplacement(new ByteRange(m.Start, m.End), m.Replacement!))];

    /// <summary>The change for a file on disk; null when no match has a replacement. Throws <see cref="InvalidOperationException"/> for a file that is not UTF-8 or looks binary, and <see cref="ArgumentException"/> for matches that overlap or do not fall on character boundaries.</summary>
    public static FileChange? ForFile(string path, IEnumerable<AstGrepMatch> matches)
    {
        var edits = Edits(matches);
        if (edits.Count == 0) return null;
        var (bytes, _) = ChangeBuilder.ReadFile(Path.GetFullPath(path));
        var codec = ContentScanner.Detect(bytes, bytes.Length);
        if (codec is null || !IsUtf8(codec)) throw new InvalidOperationException("The file is not UTF-8 text.");
        return ChangeBuilder.ForFile(path, edits, codec);
    }

    /// <summary>The change for an open document, based on its current revision; the matches must come from the bytes of that revision.</summary>
    public static FileChange? ForDocument(Document doc, IEnumerable<AstGrepMatch> matches)
    {
        var edits = Edits(matches);
        return edits.Count == 0 ? null : ChangeBuilder.ForDocument(doc, edits);
    }
}
