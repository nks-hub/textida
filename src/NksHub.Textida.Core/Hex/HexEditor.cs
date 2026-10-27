using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Hex;

public enum HexColumn { Hex, Text }

public enum HexIssue
{
    None,
    /// <summary>Overwriting does not make the file longer; switch to insert mode to add bytes at the end.</summary>
    EndOfFile,
    /// <summary>The typed character has no byte form in the charset of the character column.</summary>
    NotEncodable,
    /// <summary>A save is running on a snapshot; the document takes no change until it ends.</summary>
    Busy,
    /// <summary>The document refused the edit.</summary>
    Rejected,
    /// <summary>Overwrite mode: the caller must say what the key does (zero or remove, extend or truncate).</summary>
    NeedsChoice,
    /// <summary>The operation holds the bytes in memory and the selection is more than an array can hold (2 GiB).</summary>
    TooLarge,
}

public readonly record struct HexResult(bool Applied, HexIssue Issue = HexIssue.None)
{
    public static HexResult Done => new(true);
    public static HexResult Refused(HexIssue issue) => new(false, issue);
}

public enum DeleteMode { Ask, Zero, Remove }

public enum PasteLonger { Ask, Extend, Truncate }

/// <summary>
/// The caret, the selection and the edits of one hex view over a <see cref="Document"/>. Edits are byte-level and go through the
/// document, so undo, revisions and the journal are those of the text view. Overwrite is the default; every edit that changes the
/// length is explicit (insert mode, or a choice the caller asks the user for).
/// </summary>
public sealed class HexEditor(Document doc, HexCharset charset)
{
    private int _run;
    private string? _key;

    public Document Document => doc;
    public HexCharset Charset { get; set; } = charset;
    /// <summary>Byte the caret is on, 0 to the length of the document (the end is where a byte can be appended in insert mode).</summary>
    public long Caret { get; private set; }
    public long Anchor { get; private set; }
    /// <summary>Which half of the byte the next typed digit writes: 0 high, 1 low.</summary>
    public int Nibble { get; private set; }
    public bool Insert { get; set; }
    public HexColumn Column { get; set; }

    public bool HasSelection => Anchor != Caret;
    public ByteRange Selection => new(Math.Min(Anchor, Caret), Math.Max(Anchor, Caret));

    // ---- moving ----

    public void MoveTo(long caret, bool extend = false)
    {
        caret = Math.Clamp(caret, 0, doc.ByteLength);
        if (caret != Caret) Nibble = 0;
        Caret = caret;
        if (!extend) Anchor = caret;
        BreakRun();
    }

    public void Select(long anchor, long caret)
    {
        Anchor = Math.Clamp(anchor, 0, doc.ByteLength);
        Caret = Math.Clamp(caret, 0, doc.ByteLength);
        Nibble = 0;
        BreakRun();
    }

    public void SelectAll() => Select(0, doc.ByteLength);

    /// <summary>Starts a new undo step for the next typed digit or character.</summary>
    public void BreakRun() => _key = null;

    /// <summary>Keeps the caret and the selection inside the document after a change that came from elsewhere.</summary>
    public void Clamp()
    {
        long length = doc.ByteLength;
        Caret = Math.Min(Caret, length);
        Anchor = Math.Min(Anchor, length);
    }

    // ---- edits ----

    private HexResult Do(long start, long end, byte[] bytes, long caretAfter, int nibbleAfter, bool typing)
    {
        if (typing) _key ??= "hex#" + ++_run; else _key = null;
        var options = new EditOptions
        {
            RawBytes = true,
            Source = "hex",
            CoalesceKey = typing ? _key : null,
            SelectionsBefore = [new ByteRange(Caret, Caret)],
            SelectionsAfter = [new ByteRange(caretAfter, caretAfter)],
        };
        var result = doc.Apply([new ByteEdit(new ByteRange(start, end), bytes)], options);
        if (!result.Applied) return HexResult.Refused(result.Conflict == ConflictReason.Saving ? HexIssue.Busy : HexIssue.Rejected);
        Caret = Anchor = caretAfter;
        Nibble = nibbleAfter;
        return HexResult.Done;
    }

    private byte ByteAt(long offset)
    {
        Span<byte> one = stackalloc byte[1];
        return doc.Read(offset, one) == 1 ? one[0] : (byte)0;
    }

    /// <summary>One hex digit (0 to 15) into the byte under the caret; in insert mode the first digit inserts a byte.</summary>
    public HexResult TypeNibble(int digit)
    {
        if (HasSelection)
        {
            var sel = Selection;
            if (Insert) return Do(sel.Start, sel.End, [(byte)(digit << 4)], sel.Start, 1, typing: false);
            Caret = Anchor = sel.Start;
            Nibble = 0;
        }
        long length = doc.ByteLength;
        if (!Insert)
        {
            if (Caret >= length) return HexResult.Refused(HexIssue.EndOfFile);
            byte old = ByteAt(Caret);
            byte value = Nibble == 0 ? (byte)(digit << 4 | old & 0x0F) : (byte)(old & 0xF0 | digit);
            return Do(Caret, Caret + 1, [value], Nibble == 0 ? Caret : Caret + 1, 1 - Nibble, typing: true);
        }
        if (Nibble == 0 || Caret >= length) return Do(Caret, Caret, [(byte)(digit << 4)], Caret, 1, typing: true);
        return Do(Caret, Caret + 1, [(byte)(ByteAt(Caret) & 0xF0 | digit)], Caret + 1, 0, typing: true);
    }

    /// <summary>Text typed in the character column, as bytes of the charset: overwrite takes as many bytes as the text needs.</summary>
    public HexResult TypeText(string text)
    {
        if (text.Length == 0) return HexResult.Done;
        byte[] bytes;
        try { bytes = Charset.Codec.Encode(text); }
        catch (UnrepresentableCharactersException) { return HexResult.Refused(HexIssue.NotEncodable); }
        if (bytes.Length == 0) return HexResult.Done;
        long start = HasSelection ? Selection.Start : Caret;
        if (HasSelection && Insert) return Do(start, Selection.End, bytes, start + bytes.Length, 0, typing: false);
        if (Insert) return Do(start, start, bytes, start + bytes.Length, 0, typing: true);
        if (start + bytes.Length > doc.ByteLength) return HexResult.Refused(HexIssue.EndOfFile);
        return Do(start, start + bytes.Length, bytes, start + bytes.Length, 0, typing: !HasSelection);
    }

    /// <summary>The Delete key. In overwrite mode it never removes bytes unasked: with <see cref="DeleteMode.Ask"/> it returns <see cref="HexIssue.NeedsChoice"/>.</summary>
    public HexResult Delete(DeleteMode mode)
    {
        long length = doc.ByteLength;
        var range = HasSelection ? Selection : new ByteRange(Caret, Math.Min(length, Caret + 1));
        if (range.IsEmpty) return HexResult.Done;
        if (mode == DeleteMode.Ask) mode = Insert ? DeleteMode.Remove : DeleteMode.Ask;
        return mode switch
        {
            DeleteMode.Ask => HexResult.Refused(HexIssue.NeedsChoice),
            DeleteMode.Zero => Do(range.Start, range.End, new byte[range.Length], range.Start, 0, typing: false),
            _ => Do(range.Start, range.End, [], range.Start, 0, typing: false),
        };
    }

    /// <summary>Backspace removes the byte before the caret in insert mode; in overwrite mode it only moves back (or removes a selection after the same question as Delete).</summary>
    public HexResult Backspace(DeleteMode mode)
    {
        if (HasSelection) return Delete(mode);
        if (Caret == 0) return HexResult.Done;
        if (!Insert)
        {
            MoveTo(Caret - 1);
            return HexResult.Done;
        }
        return Do(Caret - 1, Caret, [], Caret - 1, 0, typing: false);
    }

    /// <summary>Bytes from the clipboard. Overwrite writes them over the following bytes; a block longer than the rest of the file needs a <paramref name="longer"/> answer.</summary>
    public HexResult Paste(byte[] data, PasteLonger longer = PasteLonger.Ask)
    {
        if (data.Length == 0) return HexResult.Done;
        long length = doc.ByteLength;
        if (Insert)
        {
            var range = HasSelection ? Selection : new ByteRange(Caret, Caret);
            return Do(range.Start, range.End, data, range.Start + data.Length, 0, typing: false);
        }
        long start = HasSelection ? Selection.Start : Caret;
        long rest = length - start;
        if (data.Length > rest)
        {
            if (longer == PasteLonger.Ask) return HexResult.Refused(HexIssue.NeedsChoice);
            if (longer == PasteLonger.Extend) return Do(start, length, data, start + data.Length, 0, typing: false);
            if (rest <= 0) return HexResult.Refused(HexIssue.EndOfFile);
            data = data[..(int)rest];
        }
        return Do(start, start + data.Length, data, start + data.Length, 0, typing: false);
    }

    /// <summary>Replaces <paramref name="range"/> by <paramref name="bytes"/> as one undo step (the data inspector); the caret stays.</summary>
    public HexResult Write(ByteRange range, byte[] bytes)
    {
        long caret = Caret, anchor = Anchor;
        var result = Do(range.Start, range.End, bytes, range.Start, 0, typing: false);
        if (result.Applied) (Caret, Anchor) = (Math.Min(caret, doc.ByteLength), Math.Min(anchor, doc.ByteLength));
        return result;
    }

    // ---- fill and operations over the selection ----

    /// <summary>Where a fill goes: the selection, or without one <paramref name="count"/> bytes at the caret (new bytes in insert mode, the following bytes otherwise).</summary>
    private HexResult? FillTarget(long count, out ByteRange range)
    {
        range = default;
        if (HasSelection) range = Selection;
        else if (count <= 0) return HexResult.Done;
        else if (Insert) range = new ByteRange(Caret, Caret);
        else if (Caret + count > doc.ByteLength) return HexResult.Refused(HexIssue.EndOfFile);
        else range = new ByteRange(Caret, Caret + count);
        return null;
    }

    private HexResult Finish(EditResult result, long start, long length)
    {
        if (!result.Applied) return HexResult.Refused(result.Conflict == ConflictReason.Saving ? HexIssue.Busy : HexIssue.Rejected);
        Select(start, start + length);
        return HexResult.Done;
    }

    private EditOptions FillOptions(long start, long length) => new()
    {
        RawBytes = true,
        Source = "hex",
        SelectionsBefore = [new ByteRange(Caret, Caret)],
        SelectionsAfter = [new ByteRange(start, start + length)],
    };

    /// <summary>
    /// Fills the selection with <paramref name="pattern"/> repeated from its first byte, or without a selection <paramref name="count"/> bytes at the caret.
    /// One undo step; a fill larger than a patch costs no memory (<see cref="Document.Fill"/>). Afterwards the filled bytes are selected.
    /// </summary>
    public HexResult Fill(byte[] pattern, long count = 0)
    {
        if (pattern.Length == 0) return HexResult.Done;
        if (FillTarget(count, out var range) is { } stop) return stop;
        long length = HasSelection ? range.Length : count;
        _key = null;
        try { return Finish(doc.Fill(range, pattern, length, FillOptions(range.Start, length)), range.Start, length); }
        catch (NotSupportedException) { return HexResult.Refused(HexIssue.TooLarge); }
    }

    /// <summary>Puts <paramref name="copies"/> more copies of the selected bytes right behind the selection and selects the whole run.</summary>
    public HexResult RepeatSelection(int copies)
    {
        if (!HasSelection || copies < 1) return HexResult.Done;
        var sel = Selection;
        if (sel.Length > Array.MaxLength) return HexResult.Refused(HexIssue.TooLarge);
        _key = null;
        try
        {
            var result = doc.Fill(new ByteRange(sel.End, sel.End), doc.ReadBytes(sel), sel.Length * copies, FillOptions(sel.Start, sel.Length * (copies + 1)));
            return Finish(result, sel.Start, sel.Length * (copies + 1));
        }
        catch (NotSupportedException) { return HexResult.Refused(HexIssue.TooLarge); }
    }

    /// <summary>Replaces the target of a fill by bytes made by <paramref name="make"/> from the buffer it must fill (random bytes, a number sequence).</summary>
    private HexResult FillWith(long count, Action<Span<byte>> make)
    {
        if (FillTarget(count, out var range) is { } stop) return stop;
        long length = HasSelection ? range.Length : count;
        if (length > Array.MaxLength) return HexResult.Refused(HexIssue.TooLarge);
        var bytes = new byte[length];
        make(bytes);
        _key = null;
        return Finish(doc.Apply([new ByteEdit(range, bytes)], FillOptions(range.Start, length)), range.Start, length);
    }

    /// <summary>Fills the selection (or <paramref name="count"/> bytes at the caret) with random bytes from the system generator.</summary>
    public HexResult FillRandom(long count = 0) => FillWith(count, System.Security.Cryptography.RandomNumberGenerator.Fill);

    /// <summary>Fills with the numbers <paramref name="start"/>, <paramref name="start"/> + <paramref name="step"/>, ... as words of <paramref name="width"/> bytes.</summary>
    public HexResult FillSequence(long start, long step, int width, bool bigEndian, long count = 0) =>
        FillWith(count, data => HexOperations.Sequence(data, start, step, width, bigEndian));

    /// <summary>Applies <paramref name="operation"/> to the selected bytes as one undo step (see <see cref="HexOperations.Apply"/>); the selection stays.</summary>
    public HexResult Operate(HexOperation operation, byte[]? operand = null, int amount = 0)
    {
        if (!HasSelection) return HexResult.Done;
        var range = Selection;
        if (range.Length > Array.MaxLength) return HexResult.Refused(HexIssue.TooLarge);
        var bytes = doc.ReadBytes(range);
        HexOperations.Apply(operation, bytes, operand, amount);
        _key = null;
        return Finish(doc.Apply([new ByteEdit(range, bytes)], FillOptions(range.Start, range.Length)), range.Start, range.Length);
    }

    // ---- undo ----

    public bool Undo() => Restore(doc.Undo());

    public bool Redo() => Restore(doc.Redo());

    private bool Restore(UndoResult? result)
    {
        if (result is null) return false;
        long target = result.Selections.Count > 0 ? result.Selections[0].Start : Caret;
        Caret = Anchor = Math.Clamp(target, 0, doc.ByteLength);
        Nibble = 0;
        BreakRun();
        return true;
    }

    // ---- clipboard forms ----

    /// <summary>The selected bytes; empty without a selection.</summary>
    public byte[] SelectedBytes() => HasSelection ? doc.ReadBytes(Selection) : [];

    public static string ToHexText(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 3);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(bytes[i].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>Reads text such as <c>4E 4B 03</c>, <c>4e4b03</c> or <c>0x4E, 0x4B</c> as bytes; false for anything else, so ordinary text is not taken for hex.</summary>
    public static bool TryParseHexText(string text, out byte[] bytes)
    {
        var digits = new List<int>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c) || c == ',') continue;
            if (c == '0' && i + 1 < text.Length && text[i + 1] is 'x' or 'X') { i++; continue; }
            int v = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
            if (v < 0) { bytes = []; return false; }
            digits.Add(v);
        }
        if (digits.Count == 0 || digits.Count % 2 != 0) { bytes = []; return false; }
        bytes = new byte[digits.Count / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(digits[2 * i] << 4 | digits[2 * i + 1]);
        return true;
    }
}
