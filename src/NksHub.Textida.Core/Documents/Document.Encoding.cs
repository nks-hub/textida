using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Documents;

/// <summary>What converting the document to another encoding would cost; nothing has been changed.</summary>
/// <param name="Loss">Characters the target cannot hold (and bytes of invalid sequences, which cannot be carried across).</param>
public sealed record ConversionPreview(long Revision, TextCodec Target, LossReport Loss, long ByteLength);

/// <summary>
/// Edits of an explicit action (unify line endings, normalise Unicode) computed against <see cref="Revision"/>; applying them
/// maps them through the change log, so a preview shown to the user stays valid while they type elsewhere.
/// </summary>
/// <param name="Edits">Byte ranges of the changed text only; everything else stays as it is.</param>
/// <param name="Loss">Characters the document encoding cannot hold after the change (NFD in a code page).</param>
/// <param name="IsBlocked">Characters would be lost and no replacement was confirmed.</param>
public sealed record ChangePreview(long Revision, string Source, IReadOnlyList<ByteEdit> Edits, LossReport Loss, bool IsBlocked)
{
    public int Count => Edits.Count;
}

// shortcut: whole-document actions decode the document at once (limit 2 GiB, memory about 6x the size);
// upgrade to block-wise planning when the paged backend needs them on files of several GiB.
public sealed partial class Document
{
    private static int BomOf(IByteBackend backend, TextCodec codec)
    {
        Span<byte> head = stackalloc byte[4];
        return codec.BomLengthIn(head[..backend.Read(0, head)]);
    }

    private void SetCodec(TextCodec codec)
    {
        _backend.Reinterpret(codec);
        _codec = codec;
        _bomLength = BomOf(_backend, codec);
    }

    private void RequireSupported(TextCodec codec)
    {
        if (!_backend.Supports(codec))
            throw new NotSupportedException($"The {_backend.Capabilities.Name} backend cannot hold {codec.Name}.");
    }

    private DecodedText DecodeAll()
    {
        if (ByteLength > Array.MaxLength) throw new NotSupportedException("A document over 2 GiB cannot be decoded in one piece.");
        return _codec.Decode(ReadBytes(new ByteRange(0, ByteLength)));
    }

    // ---- re-read as ----

    /// <summary>
    /// "Re-read as": the same bytes shown through another encoding. No byte changes and nothing is undoable. The document
    /// revision grows by one view change (<see cref="ChangeKind.View"/>, positions map unchanged) so views refresh; the file
    /// on disk, its <see cref="Fingerprint"/> and <see cref="IsModified"/> are not touched.
    /// </summary>
    public void ReinterpretAs(TextCodec codec)
    {
        ThrowIfDisposed();
        if (_saving) throw new InvalidOperationException("The document cannot change its encoding while it is being saved.");
        if (codec == _codec) return;
        RequireSupported(codec);
        SetCodec(codec);
        Commit(ChangeKind.View, "reinterpret", Array.Empty<ChangeSet.Span>(), null, null);
    }

    // ---- convert to ----

    /// <summary>Loss report of <see cref="ConvertTo"/> without changing anything.</summary>
    public ConversionPreview PreviewConvert(TextCodec target)
    {
        ThrowIfDisposed();
        RequireSupported(target);
        var plan = ByteFidelity.PlanConvert(DecodeAll(), target);
        return new(_revision, target, plan.Loss, plan.Length);
    }

    /// <summary>
    /// "Convert to": rewrites the bytes in <paramref name="target"/> as one undoable transaction; undo restores bytes and encoding.
    /// Blocked (<see cref="ConflictReason.LossNotConfirmed"/>) while characters would be lost and <paramref name="replacement"/>
    /// is null; a replacement is the user's confirmed text for each lost character.
    /// </summary>
    public EditResult ConvertTo(TextCodec target, string? replacement = null)
    {
        ThrowIfDisposed();
        if (_saving) return EditResult.Fail(ConflictReason.Saving, _revision);
        if (target == _codec) return new(true, _revision, ConflictReason.None);
        RequireSupported(target);
        var plan = ByteFidelity.PlanConvert(DecodeAll(), target, replacement);
        if (plan.IsBlocked) return EditResult.Fail(ConflictReason.LossNotConfirmed, _revision);

        var work = new List<Pending>();
        if (_codec.SameBytesAs(target)) work.Add(new(0, _bomLength, target.Bom)); // only the BOM differs
        else work.Add(new(0, ByteLength, plan.ToBytes()));
        work.RemoveAll(w => w.Deleted == 0 && w.Insert.IsEmpty);

        var before = _codec;
        var removed = Execute(work, validate: false, captureRemoved: true, out _);
        SetCodec(target);
        var step = new Step([.. work.Select((w, k) => Applied(w.Start, removed![k], w.Insert.ToArray()))], null, null, before, target);
        Commit(ChangeKind.Edit, "convert", work, step, null);
        return new(true, _revision, ConflictReason.None);
    }

    /// <summary>
    /// "Save as" with a target encoding. The loss check runs first: without a confirmed <paramref name="replacement"/>
    /// nothing is converted or written and the result carries the <see cref="SaveResult.Loss"/>. A failed save (external change)
    /// takes the conversion back.
    /// </summary>
    public SaveResult SaveAs(string path, TextCodec target, string? replacement = null, bool overwriteExternalChanges = false)
    {
        ThrowIfDisposed();
        if (target == _codec && !_backend.Capabilities.MaterializesText) return Save(path, overwriteExternalChanges); // a paged document is never decoded whole
        var preview = PreviewConvert(target);
        if (preview.Loss.HasLoss && replacement is null) return new(false, null, preview.Loss);

        long before = _revision;
        if (!ConvertTo(target, replacement).Applied) return new(false, null, preview.Loss);
        bool converted = _revision != before;
        var result = Save(path, overwriteExternalChanges);
        if (!result.Saved && converted)
        {
            Undo();
            _redo.RemoveAt(_redo.Count - 1);
        }
        return result;
    }

    /// <summary>Applies a conversion that is already known (journal replay): the edits and the codec the document has afterwards, as one undo step.</summary>
    internal EditResult ApplyConversion(IReadOnlyList<ByteEdit> edits, TextCodec target, string? source)
    {
        ThrowIfDisposed();
        RequireSupported(target);
        var work = edits.Select(e => new Pending(e.Range.Start, e.Range.Length, e.Insert)).OrderBy(w => w.Start).ToList();
        var before = _codec;
        var removed = Execute(work, validate: false, captureRemoved: true, out _);
        SetCodec(target);
        var step = new Step([.. work.Select((w, k) => Applied(w.Start, removed![k], w.Insert.ToArray()))], null, null, before, target);
        Commit(ChangeKind.Edit, source, work, step, null);
        return new(true, _revision, ConflictReason.None);
    }

    // ---- line endings and normalisation ----

    /// <summary>Which line endings would change to <paramref name="target"/>; apply with <see cref="ApplyPreview"/>.</summary>
    public ChangePreview PreviewUnifyEol(EolStyle target, string? replacement = null)
    {
        ThrowIfDisposed();
        var text = DecodeAll();
        return PlanEdits(text, Eol.PreviewNormalize(text, target).Edits, replacement, "unify-eol");
    }

    /// <summary>Which lines would change under the Unicode normalisation <paramref name="form"/>; invalid bytes are never touched.</summary>
    public ChangePreview PreviewNormalize(NormalizationForm form, string? replacement = null)
    {
        ThrowIfDisposed();
        var text = DecodeAll();
        return PlanEdits(text, UnicodeNormalizer.Preview(text, form), replacement, "normalize-" + form);
    }

    /// <summary>
    /// Applies a preview as one undoable transaction. Blocked previews (<see cref="ConflictReason.LossNotConfirmed"/>) change nothing;
    /// a range a later change touched is a conflict like any other edit.
    /// </summary>
    public EditResult ApplyPreview(ChangePreview preview)
    {
        ThrowIfDisposed();
        if (preview.IsBlocked) return EditResult.Fail(ConflictReason.LossNotConfirmed, _revision);
        return Apply(preview.Edits, new EditOptions { BaseRevision = preview.Revision, Source = preview.Source });
    }

    public EditResult UnifyEol(EolStyle target, string? replacement = null) => ApplyPreview(PreviewUnifyEol(target, replacement));

    public EditResult Normalize(NormalizationForm form, string? replacement = null) => ApplyPreview(PreviewNormalize(form, replacement));

    private ChangePreview PlanEdits(DecodedText text, IReadOnlyList<TextEdit> edits, string? replacement, string source)
    {
        var plan = ByteFidelity.PlanSave(text, edits, replacement);
        var written = plan.Segments.Where(s => !s.IsVerbatim).ToList(); // one per edit, in order
        var bytes = new List<ByteEdit>(edits.Count);
        for (int i = 0; i < edits.Count; i++)
            bytes.Add(new(new ByteRange(text.ByteOffsetOf(edits[i].Start), text.ByteOffsetOf(edits[i].End)), written[i].Data!));
        return new(_revision, source, bytes, plan.Loss, plan.IsBlocked);
    }
}
