using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>What an inspected action does to the bytes.</summary>
public enum InspectedAction
{
    /// <summary>The whole file is rewritten in another encoding (Convert to, Save As with an encoding, the charset of EditorConfig).</summary>
    Convert,
    UnifyEol,
    Normalize,
    /// <summary>Explicit edits proposed from outside the editor (a plugin, a recipe); the ranges are the edits themselves.</summary>
    Edits,
    /// <summary>Not an action: the ranges the next save writes, for the hex view.</summary>
    Save,
}

/// <summary>
/// One changed range. <see cref="Start"/> is an offset in the revision that was inspected; the heads are the first bytes of the old and the new content.
/// </summary>
/// <param name="OldLength">Bytes replaced; null when the old content is not known (a save that writes a new file).</param>
public readonly record struct InspectedRange(long Start, long? OldLength, long NewLength, byte[]? OldHead, byte[] NewHead);

/// <summary>
/// The report of F169 for every action that changes bytes outside the ranges the user edited: the ranges that change, the sizes, the encoding and the
/// line endings before and after, and the characters the target cannot hold. It changes nothing; <see cref="Document.Apply(ByteInspection)"/>
/// applies it only after <see cref="Confirm"/>, and only to the revision that was inspected.
/// </summary>
public sealed class ByteInspection
{
    /// <summary>Ranges listed in <see cref="Ranges"/>; <see cref="RangeCount"/> counts all of them.</summary>
    public const int MaxListed = 1000;
    public const int HeadBytes = 16;

    internal ByteInspection(InspectedAction action, string source, long revision)
    {
        Action = action;
        Source = source;
        Revision = revision;
    }

    public InspectedAction Action { get; }
    public string Source { get; }
    public long Revision { get; }
    public TextCodec CodecBefore { get; internal init; } = TextCodec.Utf8;
    public TextCodec CodecAfter { get; internal init; } = TextCodec.Utf8;
    public long SizeBefore { get; internal init; }
    public long SizeAfter { get; internal init; }
    public EolStats EolBefore { get; internal init; }
    public EolStats EolAfter { get; internal init; }
    public LossReport Loss { get; internal init; } = LossReport.None;
    public IReadOnlyList<InspectedRange> Ranges { get; internal init; } = [];
    public int RangeCount { get; internal init; }
    /// <summary>Bytes of the new content inside the changed ranges.</summary>
    public long ChangedBytes { get; internal init; }
    public bool IsConfirmed { get; private set; }
    /// <summary>The text written for each character the target cannot hold; null while none was chosen.</summary>
    public string? Replacement { get; private set; }
    public bool HasLoss => Loss.HasLoss;
    /// <summary>True when characters would be lost and no replacement was chosen.</summary>
    public bool IsBlocked => Loss.HasLoss && Replacement is null;

    internal TextCodec? Target { get; init; }
    internal IReadOnlyList<ByteEdit> Edits { get; init; } = [];

    /// <summary>The user has seen the report and agrees. A loss needs <paramref name="replacement"/> as well (the text written for each lost character).</summary>
    public void Confirm(string? replacement = null)
    {
        if (Loss.HasLoss && replacement is null) throw new InvalidOperationException("The action loses characters; confirm it with the replacement that is written for them.");
        Replacement = Loss.HasLoss ? replacement : null;
        IsConfirmed = true;
    }
}

public sealed partial class Document
{
    /// <summary>The report of "Convert to" <paramref name="target"/>; nothing is changed.</summary>
    public ByteInspection InspectConvert(TextCodec target, string source = "convert")
    {
        ThrowIfDisposed();
        RequireSupported(target);
        var text = DecodeAll();
        var plan = ByteFidelity.PlanConvert(text, target, "?");
        var after = plan.Assemble();
        var diff = ByteDiff.Compute(text.Bytes.Span, after);
        InspectedRange[] ranges = diff.IsEmpty ? [] : [new(diff.FirstDifference, diff.OldChanged, diff.NewChanged,
            Head(text.Bytes.Span.Slice((int)diff.FirstDifference, (int)diff.OldChanged)), Head(after.AsSpan((int)diff.FirstDifference, (int)diff.NewChanged)))];
        return new(InspectedAction.Convert, source, _revision)
        {
            CodecBefore = _codec, CodecAfter = target, SizeBefore = ByteLength, SizeAfter = after.Length,
            EolBefore = Eol.Analyze(text.Text), EolAfter = Eol.Analyze(text.Text),
            Loss = plan.Loss, Ranges = ranges, RangeCount = ranges.Length, ChangedBytes = diff.NewChanged, Target = target,
        };
    }

    /// <summary>The report of unifying the line endings to <paramref name="target"/>.</summary>
    public ByteInspection InspectUnifyEol(EolStyle target)
    {
        ThrowIfDisposed();
        var text = DecodeAll();
        var edits = Eol.PreviewNormalize(text, target).Edits;
        return InspectEdits(InspectedAction.UnifyEol, "unify-eol", text, edits);
    }

    /// <summary>The report of a Unicode normalisation to <paramref name="form"/>.</summary>
    public ByteInspection InspectNormalize(NormalizationForm form)
    {
        ThrowIfDisposed();
        var text = DecodeAll();
        return InspectEdits(InspectedAction.Normalize, "normalize-" + form, text, UnicodeNormalizer.Preview(text, form));
    }

    /// <summary>
    /// The report of explicit edits made against <paramref name="baseRevision"/>; they are mapped to the current revision first, so the ranges are those that
    /// would change now. Null when a later change touched one of them (the proposal is stale).
    /// </summary>
    public ByteInspection? InspectEdits(IReadOnlyList<ByteEdit> edits, string source, long baseRevision)
    {
        ThrowIfDisposed();
        var mapped = new List<ByteEdit>(edits.Count);
        foreach (var edit in edits)
        {
            var map = MapRange(edit.Range, baseRevision);
            if (!map.Ok || map.Range.End > ByteLength) return null;
            mapped.Add(edit with { Range = map.Range });
        }
        mapped.Sort((a, b) => a.Range.Start.CompareTo(b.Range.Start));
        var ranges = new List<InspectedRange>();
        long changed = 0, delta = 0;
        foreach (var edit in mapped)
        {
            changed += edit.Insert.Length;
            delta += edit.Insert.Length - edit.Range.Length;
            if (ranges.Count < ByteInspection.MaxListed)
                ranges.Add(new(edit.Range.Start, edit.Range.Length, edit.Insert.Length,
                    Head(ReadBytes(new ByteRange(edit.Range.Start, Math.Min(edit.Range.End, edit.Range.Start + ByteInspection.HeadBytes)))), Head(edit.Insert.Span)));
        }
        return new(InspectedAction.Edits, source, _revision)
        {
            CodecBefore = _codec, CodecAfter = _codec, SizeBefore = ByteLength, SizeAfter = ByteLength + delta,
            Ranges = ranges, RangeCount = mapped.Count, ChangedBytes = changed, Edits = mapped,
        };
    }

    private ByteInspection InspectEdits(InspectedAction action, string source, DecodedText text, IReadOnlyList<TextEdit> textEdits)
    {
        var preview = PlanEdits(text, textEdits, "?", source);
        var ranges = new List<InspectedRange>();
        long changed = 0, delta = 0;
        foreach (var edit in preview.Edits)
        {
            changed += edit.Insert.Length;
            delta += edit.Insert.Length - edit.Range.Length;
            if (ranges.Count < ByteInspection.MaxListed)
                ranges.Add(new(edit.Range.Start, edit.Range.Length, edit.Insert.Length,
                    Head(ReadBytes(new ByteRange(edit.Range.Start, Math.Min(edit.Range.End, edit.Range.Start + ByteInspection.HeadBytes)))), Head(edit.Insert.Span)));
        }
        return new(action, source, _revision)
        {
            CodecBefore = _codec, CodecAfter = _codec, SizeBefore = ByteLength, SizeAfter = ByteLength + delta,
            EolBefore = Eol.Analyze(text.Text), EolAfter = Eol.Analyze(TextEdit.Apply(text.Text, textEdits)),
            Loss = preview.Loss, Ranges = ranges, RangeCount = preview.Edits.Count, ChangedBytes = changed, Edits = preview.Edits,
        };
    }

    /// <summary>
    /// The ranges the next save writes (everything changed since the last save), with the old bytes from the file when a save patches it in place.
    /// Not an action, so <see cref="Apply(ByteInspection)"/> refuses it. Null ranges when the change log no longer reaches back to the last save.
    /// </summary>
    public ByteInspection InspectUnsaved()
    {
        ThrowIfDisposed();
        var plan = PlanSave();
        var changed = ChangedRangesSince(SavedRevision) ?? [];
        var ranges = new List<InspectedRange>();
        long bytes = 0;
        FileStream? disk = null;
        try
        {
            if (plan.Mode == SaveMode.InPlace && FilePath is { } path && File.Exists(path))
                disk = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            foreach (var r in changed)
            {
                bytes += r.Length;
                if (ranges.Count >= ByteInspection.MaxListed) continue;
                var now = ReadBytes(new ByteRange(r.Start, Math.Min(r.End, r.Start + ByteInspection.HeadBytes)));
                byte[]? old = null;
                if (disk is not null)
                {
                    old = new byte[now.Length];
                    disk.Position = r.Start;
                    old = old.AsSpan(0, disk.Read(old)).ToArray();
                }
                ranges.Add(new(r.Start, disk is null ? null : r.Length, r.Length, old, now));
            }
        }
        finally
        {
            disk?.Dispose();
        }
        return new(InspectedAction.Save, "save", _revision)
        {
            CodecBefore = _codec, CodecAfter = _codec, SizeBefore = Fingerprint?.Size ?? ByteLength, SizeAfter = ByteLength,
            Ranges = ranges, RangeCount = changed.Count, ChangedBytes = bytes,
        };
    }

    /// <summary>
    /// Applies a confirmed inspection as one undo step. Fails with <see cref="ConflictReason.NotConfirmed"/> before <see cref="ByteInspection.Confirm"/>,
    /// with <see cref="ConflictReason.LossNotConfirmed"/> while characters would be lost without a replacement and with <see cref="ConflictReason.Overlap"/>
    /// when the document changed since it was inspected: the user agreed to exactly the ranges that were shown.
    /// </summary>
    public EditResult Apply(ByteInspection inspection)
    {
        ThrowIfDisposed();
        if (inspection.Action == InspectedAction.Save) return EditResult.Fail(ConflictReason.InvalidRange, _revision);
        if (!inspection.IsConfirmed) return EditResult.Fail(ConflictReason.NotConfirmed, _revision);
        if (inspection.IsBlocked) return EditResult.Fail(ConflictReason.LossNotConfirmed, _revision);
        if (inspection.Revision != _revision) return EditResult.Fail(ConflictReason.Overlap, _revision);
        if (inspection.Action == InspectedAction.Convert) return ConvertTo(inspection.Target!, inspection.Replacement);
        if (inspection.Edits.Count == 0) return new(true, _revision, ConflictReason.None);
        return Apply(inspection.Edits, new EditOptions { BaseRevision = inspection.Revision, Source = inspection.Source });
    }

    private static byte[] Head(ReadOnlySpan<byte> bytes) => bytes[..Math.Min(bytes.Length, ByteInspection.HeadBytes)].ToArray();
}
