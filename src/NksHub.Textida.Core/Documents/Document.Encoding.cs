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

    // ---- convert a large file on save ----

    private TextCodec? _pendingTranscode;
    private string? _transcodeReplacement;

    /// <summary>The encoding the file is rewritten in by the next save (<see cref="StageTranscode"/>); null when the save keeps the encoding of the bytes.</summary>
    public TextCodec? PendingTranscode => _pendingTranscode;

    /// <summary>
    /// Counts, block by block, the characters of the document that <paramref name="target"/> cannot write (and the undecodable bytes, which cannot be carried across), without changing or writing anything.
    /// Block after block is read from a snapshot and cut between characters, so a file of any size costs no more memory than one block. Run it on a worker thread.
    /// </summary>
    public LossReport PreviewTranscode(TextCodec target, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        return Transcode(GetSnapshot(), _codec, target, null, ct, null, null, 0).Loss;
    }

    /// <summary>
    /// "Convert to" for a document that is not decoded as a whole (the large file view): the conversion is a pending step of the document and the next save streams the file into the new
    /// encoding, block by block (<see cref="Transcode"/>), and swaps it in only when it is complete. One undo step: undoing it before the save cancels it; undoing it after the save makes the next
    /// save convert back. <paramref name="replacement"/> is the user's confirmed text for each character the target cannot hold (null: the save reports the loss and writes nothing).
    /// </summary>
    public EditResult StageTranscode(TextCodec target, string? replacement = null)
    {
        ThrowIfDisposed();
        if (_saving) return EditResult.Fail(ConflictReason.Saving, _revision);
        RequireSupported(target);
        var after = target.SameBytesAs(_codec) ? null : target;
        if (after == _pendingTranscode) return new(true, _revision, ConflictReason.None);
        var before = _pendingTranscode;
        _pendingTranscode = after;
        _transcodeReplacement = replacement;
        Commit(ChangeKind.View, "transcode", new List<Pending>(), new Step([], null, null, IsTranscode: true, PendingBefore: before, PendingAfter: after), null);
        return new(true, _revision, ConflictReason.None);
    }

    /// <summary>
    /// Reads <paramref name="snapshot"/> in blocks of 1 MiB cut at character boundaries of <paramref name="from"/>, plans each block in <paramref name="to"/> and writes it to <paramref name="sink"/> (null: only counts).
    /// The byte order mark of the target is written once at the start; a mark in the middle of the source is a character. The loss is added up over the blocks; <c>Blocked</c> is true when a character
    /// was lost and there is no <paramref name="replacement"/>, in which case nothing may be written.
    /// </summary>
    private static (LossReport Loss, bool Blocked) Transcode(IByteSnapshot snapshot, TextCodec from, TextCodec to, string? replacement, CancellationToken ct, Stream? sink, IProgress<SaveProgress>? progress, int intervalMs)
    {
        const int Block = 1 << 20;
        var counts = new Dictionary<int, int>();
        int total = 0;
        bool blocked = false, first = true;
        var target = to.WithBom(false);
        if (sink is not null && to.Bom.Length > 0) sink.Write(to.Bom);
        var buffer = new byte[Block + 16];
        int have = 0;
        long at = 0, length = snapshot.ByteLength, written = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long nextReport = intervalMs;
        while (at < length || have > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (at < length)
            {
                int n = snapshot.Read(at, buffer.AsSpan(have, (int)Math.Min(Block, length - at)));
                if (n == 0) throw new EndOfStreamException("The snapshot ended before its declared length.");
                at += n;
                have += n;
            }
            bool end = at >= length;
            int cut = end ? have : from.CompleteLength(buffer.AsSpan(0, have));
            if (cut == 0) cut = have; // a block that is one unfinished character cannot happen at this size; take it whole rather than loop
            var decoded = (first ? from : from.WithBom(false)).Decode(buffer.AsMemory(0, cut).ToArray());
            var plan = ByteFidelity.PlanConvert(decoded, target, replacement);
            if (plan.Loss.HasLoss)
            {
                total += plan.Loss.Total;
                foreach (var g in plan.Loss.Groups) counts[g.CodePoint] = counts.GetValueOrDefault(g.CodePoint) + g.Count;
            }
            blocked |= plan.IsBlocked;
            if (sink is not null && !blocked)
            {
                var data = plan.ToBytes();
                sink.Write(data);
                written += data.Length;
            }
            first = false;
            Buffer.BlockCopy(buffer, cut, buffer, 0, have - cut);
            have -= cut;
            if (progress is not null && (at >= length || clock.ElapsedMilliseconds >= nextReport))
            {
                progress.Report(new(at - have, length));
                nextReport = clock.ElapsedMilliseconds + intervalMs;
            }
        }
        return (LossReport.FromCounts(counts, total), blocked);
    }

    /// <summary>After a save wrote the document in <paramref name="to"/>: the history that described the old bytes is gone, the encoding is the new one, and one step remembers the old one so undo converts back.</summary>
    private void ResetAfterTranscode(TextCodec from, TextCodec to, long oldLength)
    {
        _undo.Clear();
        _redo.Clear();
        _undoTrimmed = 0;
        _saved = null;
        _savedUnreachable = false;
        _pendingTranscode = null;
        SetCodec(to);
        Commit(ChangeKind.Backend, "transcode", [new ChangeSet.Span(0, oldLength, ByteLength)], null, null);
        PushUndo(new Step([], null, null, IsTranscode: true, PendingBefore: from, PendingAfter: null), null);
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
