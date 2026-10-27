using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Fragments;

/// <summary>The outcome of <see cref="FragmentLink.Open"/>: the link and the text to edit, or the reason there is none.</summary>
public sealed record FragmentOpened(FragmentLink? Link, string Text, FragmentProblem Problem);

/// <summary>
/// A string literal of a parent document edited as text of its own (JSON, SQL, HTML). The content of the literal is held by two anchors, so edits of the parent
/// elsewhere move it. <see cref="WriteBack"/> escapes the text for the kind of literal and replaces the content, unless the parent changed inside the string
/// since the link was made or last written: that is a conflict and nothing is written. Not thread safe, like <see cref="Document"/>.
/// </summary>
public sealed class FragmentLink
{
    private const string Source = "fragment";

    private readonly Anchor _start, _end;
    private readonly StringLiteral _literal;
    private long _baseRevision;

    private FragmentLink(Document parent, StringLiteral literal, Anchor start, Anchor end)
    {
        Parent = parent;
        _literal = literal;
        (_start, _end) = (start, end);
        _baseRevision = parent.Revision;
    }

    public Document Parent { get; }
    public LiteralKind Kind => _literal.Kind;
    /// <summary>True after <see cref="Detach"/>: the fragment is a text of its own and writes nothing back.</summary>
    public bool IsDetached { get; private set; }
    /// <summary>The content of the string in the parent now; empty when the anchors were lost.</summary>
    public ByteRange Range => _start.IsValid && _end.IsValid ? new(_start.Offset, Math.Max(_start.Offset, _end.Offset)) : default;

    /// <summary>Cuts the link, for example when the parent is closed. The anchors stay with the parent and are collected with it.</summary>
    public void Detach() => IsDetached = true;

    /// <summary>
    /// Finds the string literal at <paramref name="caret"/> (a byte offset of <paramref name="parent"/>) and opens its text. <paramref name="php"/> selects the
    /// escape rules of PHP, otherwise those of JavaScript.
    /// </summary>
    public static FragmentOpened Open(Document parent, Tokenizer tokenizer, bool php, long caret)
    {
        long bom = parent.BomLength;
        string all = parent.ReadText(new ByteRange(bom, parent.ByteLength));
        int charCaret = parent.ReadText(new ByteRange(bom, Math.Max(bom, caret))).Length;
        if (StringLiterals.Find(all, tokenizer, php, charCaret) is not { } literal) return new(null, "", FragmentProblem.NoLiteral);
        if (!StringLiterals.TryUnescape(literal, all[literal.ContentStart..literal.ContentEnd], out var text, out var problem)) return new(null, "", problem);
        long start = bom + parent.Codec.Encode(all[..literal.ContentStart]).Length;
        long end = start + parent.Codec.Encode(all[literal.ContentStart..literal.ContentEnd]).Length;
        return new(new FragmentLink(parent, literal, parent.CreateAnchor(start, AnchorGravity.Left), parent.CreateAnchor(end, AnchorGravity.Right)), text, FragmentProblem.None);
    }

    /// <summary>True when the parent changed in or next to the string since the last write, or when the change log cannot say it did not.</summary>
    public bool ParentChangedInside()
    {
        if (!_start.IsValid || !_end.IsValid) return true;
        if (Parent.ChangedRangesSince(_baseRevision) is not { } changed) return true;
        return changed.Any(r => r.Start <= _end.Offset && r.End >= _start.Offset);
    }

    /// <summary>
    /// Writes <paramref name="text"/> into the string. With <paramref name="overwrite"/> a change of the parent inside the string is replaced too;
    /// a string whose ends are gone is never written. <see cref="FragmentProblem.None"/> when the parent took the edit.
    /// </summary>
    public FragmentProblem WriteBack(string text, bool overwrite = false)
    {
        if (IsDetached) return FragmentProblem.Detached;
        if (!_start.IsValid || !_end.IsValid) return FragmentProblem.RangeLost;
        if (!overwrite && ParentChangedInside()) return FragmentProblem.Conflict;
        if (!StringLiterals.TryEscape(_literal, text, out var content, out var problem)) return problem;
        var range = new ByteRange(_start.Offset, Math.Max(_start.Offset, _end.Offset));
        if (_literal.IsDocument && content.Length > 0 && !BreaksLine(range.End)) content += LineBreakAfterOpening();
        byte[] bytes;
        try { bytes = Parent.Codec.Encode(content); }
        catch (UnrepresentableCharactersException) { return FragmentProblem.NotEncodable; }
        var result = Parent.Replace(range, bytes, new EditOptions { Source = Source });
        if (!result.Applied) return FragmentProblem.ParentRejected;
        _baseRevision = Parent.Revision;
        return FragmentProblem.None;
    }

    /// <summary>A heredoc without body has its closing marker right behind the line break of the opening; text written there needs a line break of its own.</summary>
    private bool BreaksLine(long offset)
    {
        var tail = Parent.Codec.GetString(Parent.ReadBytes(new ByteRange(offset, Math.Min(Parent.ByteLength, offset + 8))));
        return tail.Length > 0 && tail[0] is '\r' or '\n';
    }

    private string LineBreakAfterOpening()
    {
        var before = Parent.Codec.GetString(Parent.ReadBytes(new ByteRange(Math.Max(Parent.BomLength, _start.Offset - 8), _start.Offset)));
        return before.EndsWith("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }
}
