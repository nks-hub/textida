using System.Text;
using NksPad.Core.Documents;

namespace NksPad.Core.Journal;

public readonly record struct DocFormat(EncodingKind Kind, int BomLength, int CodePage)
{
    public static DocFormat Of(DetectedEncoding e) => new(e.Kind, e.BomLength, e.Encoding.CodePage);

    public DetectedEncoding ToEncoding() => new(Kind switch
    {
        EncodingKind.Ascii or EncodingKind.Utf8 => new UTF8Encoding(BomLength > 0),
        EncodingKind.Utf16LE => new UnicodeEncoding(false, BomLength > 0),
        EncodingKind.Utf16BE => new UnicodeEncoding(true, BomLength > 0),
        EncodingKind.Utf32LE => new UTF32Encoding(false, BomLength > 0),
        EncodingKind.Utf32BE => new UTF32Encoding(true, BomLength > 0),
        _ => Encoding.GetEncoding(CodePage),
    }, Kind, BomLength);
}

/// <summary>Start of a document in the journal: either a file and its fingerprint, or an unnamed document whose bytes follow as seeds.</summary>
public sealed record OpenRecord(Guid DocId, string? Path, FileFingerprint? Fingerprint, DocFormat Format, string Backend, long BaseRevision, long SeedLength);

public sealed record SeedRecord(Guid DocId, long Offset, byte[] Bytes);

public sealed record ChangeEdit(long Start, long Deleted, byte[] Inserted);

/// <summary>A change in the coordinates of the previous revision. <see cref="NewUndoEntry"/> is false when the edit joined the previous undo step.</summary>
public sealed record EditRecord(Guid DocId, long Revision, ChangeKind Kind, string? Source, bool NewUndoEntry, IReadOnlyList<ChangeEdit> Edits);

/// <summary>Undo or redo; replayed by calling <c>Undo()</c>/<c>Redo()</c>, so one step of the user is one entry.</summary>
public sealed record UndoRedoRecord(Guid DocId, long Revision);

public sealed record SaveRecord(Guid DocId, long Revision, string Path, FileFingerprint Fingerprint);

/// <summary>Written and fsynced before a save moves the new file into place; if the process dies in the swap, recovery recognises the file by its hash.</summary>
public sealed record SaveIntentRecord(Guid DocId, long Revision, string Path, long Size, string Sha256);

/// <summary>Tabs of the session. <see cref="Revision"/> is the revision of the document the selections refer to.</summary>
public sealed record SessionTab(Guid DocId, string? Title, long Revision, IReadOnlyList<ByteRange> Selections)
{
    /// <summary>Builds a tab from anchors (start, end per selection); selections with an invalid anchor are left out, never guessed.</summary>
    public static SessionTab Capture(Guid docId, Document doc, string? title, IEnumerable<(Anchor Start, Anchor End)> selections) =>
        new(docId, title, doc.Revision, [.. selections.Where(s => s.Start.IsValid && s.End.IsValid && s.Start.Offset <= s.End.Offset).Select(s => new ByteRange(s.Start.Offset, s.End.Offset))]);
}

public sealed record SessionGroup(IReadOnlyList<SessionTab> Tabs, Guid? ActiveDocId);

public sealed record SessionModel(IReadOnlyList<SessionGroup> Groups, int ActiveGroup)
{
    public IEnumerable<SessionTab> AllTabs => Groups.SelectMany(g => g.Tabs);
}

internal static class JournalRecords
{
    private const int FormatVersionOfPayloads = 1;

    // ---- primitives ----

    private static void PutString(BinaryWriter w, string? s)
    {
        w.Write(s is not null);
        if (s is not null) w.Write(s);
    }

    private static string? GetString(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;
    private static void PutGuid(BinaryWriter w, Guid g) => w.Write(g.ToByteArray());
    private static Guid GetGuid(BinaryReader r) => new(r.ReadBytes(16));

    private static void PutPrint(BinaryWriter w, FileFingerprint? p)
    {
        w.Write(p is not null);
        if (p is null) return;
        w.Write(p.Size);
        w.Write(p.LastWriteUtc.Ticks);
        w.Write(p.Sha256);
    }

    private static FileFingerprint? GetPrint(BinaryReader r) =>
        r.ReadBoolean() ? new(r.ReadInt64(), new DateTime(r.ReadInt64(), DateTimeKind.Utc), r.ReadString()) : null;

    private static byte[] Build(Action<BinaryWriter> fill)
    {
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)FormatVersionOfPayloads);
            fill(w);
        }
        return ms.ToArray();
    }

    private static T Parse<T>(byte[] payload, Func<BinaryReader, T> read)
    {
        using var r = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
        if (r.ReadByte() != FormatVersionOfPayloads) throw new JournalFormatException("Unknown record layout.");
        return read(r);
    }

    // ---- records ----

    public static byte[] Encode(OpenRecord o) => Build(w =>
    {
        PutGuid(w, o.DocId);
        PutString(w, o.Path);
        PutPrint(w, o.Fingerprint);
        w.Write((byte)o.Format.Kind);
        w.Write(o.Format.BomLength);
        w.Write(o.Format.CodePage);
        w.Write(o.Backend);
        w.Write(o.BaseRevision);
        w.Write(o.SeedLength);
    });

    public static OpenRecord DecodeOpen(byte[] p) => Parse(p, r => new OpenRecord(GetGuid(r), GetString(r), GetPrint(r),
        new DocFormat((EncodingKind)r.ReadByte(), r.ReadInt32(), r.ReadInt32()), r.ReadString(), r.ReadInt64(), r.ReadInt64()));

    public static byte[] Encode(SeedRecord s) => Build(w =>
    {
        PutGuid(w, s.DocId);
        w.Write(s.Offset);
        w.Write(s.Bytes.Length);
        w.Write(s.Bytes);
    });

    public static SeedRecord DecodeSeed(byte[] p) => Parse(p, r =>
    {
        var id = GetGuid(r);
        long offset = r.ReadInt64();
        return new SeedRecord(id, offset, r.ReadBytes(r.ReadInt32()));
    });

    public static byte[] Encode(EditRecord e) => Build(w =>
    {
        PutGuid(w, e.DocId);
        w.Write(e.Revision);
        w.Write((byte)e.Kind);
        PutString(w, e.Source);
        w.Write(e.NewUndoEntry);
        w.Write(e.Edits.Count);
        foreach (var x in e.Edits)
        {
            w.Write(x.Start);
            w.Write(x.Deleted);
            w.Write(x.Inserted.Length);
            w.Write(x.Inserted);
        }
    });

    public static EditRecord DecodeEdit(byte[] p) => Parse(p, r =>
    {
        var id = GetGuid(r);
        long rev = r.ReadInt64();
        var kind = (ChangeKind)r.ReadByte();
        var source = GetString(r);
        bool isNew = r.ReadBoolean();
        var edits = new ChangeEdit[r.ReadInt32()];
        for (int i = 0; i < edits.Length; i++)
        {
            long start = r.ReadInt64(), deleted = r.ReadInt64();
            edits[i] = new(start, deleted, r.ReadBytes(r.ReadInt32()));
        }
        return new EditRecord(id, rev, kind, source, isNew, edits);
    });

    public static byte[] Encode(UndoRedoRecord u) => Build(w =>
    {
        PutGuid(w, u.DocId);
        w.Write(u.Revision);
    });

    public static UndoRedoRecord DecodeUndoRedo(byte[] p) => Parse(p, r => new UndoRedoRecord(GetGuid(r), r.ReadInt64()));

    public static byte[] Encode(SaveRecord s) => Build(w =>
    {
        PutGuid(w, s.DocId);
        w.Write(s.Revision);
        w.Write(s.Path);
        PutPrint(w, s.Fingerprint);
    });

    public static SaveRecord DecodeSave(byte[] p) => Parse(p, r => new SaveRecord(GetGuid(r), r.ReadInt64(), r.ReadString(), GetPrint(r)!));

    public static byte[] Encode(SaveIntentRecord s) => Build(w =>
    {
        PutGuid(w, s.DocId);
        w.Write(s.Revision);
        w.Write(s.Path);
        w.Write(s.Size);
        w.Write(s.Sha256);
    });

    public static SaveIntentRecord DecodeSaveIntent(byte[] p) => Parse(p, r => new SaveIntentRecord(GetGuid(r), r.ReadInt64(), r.ReadString(), r.ReadInt64(), r.ReadString()));

    public static byte[] EncodeClose(Guid docId) => Build(w => PutGuid(w, docId));
    public static Guid DecodeClose(byte[] p) => Parse(p, GetGuid);

    public static byte[] Encode(SessionModel m) => Build(w =>
    {
        w.Write(m.ActiveGroup);
        w.Write(m.Groups.Count);
        foreach (var g in m.Groups)
        {
            w.Write(g.ActiveDocId is not null);
            if (g.ActiveDocId is { } a) PutGuid(w, a);
            w.Write(g.Tabs.Count);
            foreach (var t in g.Tabs)
            {
                PutGuid(w, t.DocId);
                PutString(w, t.Title);
                w.Write(t.Revision);
                w.Write(t.Selections.Count);
                foreach (var s in t.Selections)
                {
                    w.Write(s.Start);
                    w.Write(s.End);
                }
            }
        }
    });

    public static SessionModel DecodeSession(byte[] p) => Parse(p, r =>
    {
        int active = r.ReadInt32();
        var groups = new SessionGroup[r.ReadInt32()];
        for (int gi = 0; gi < groups.Length; gi++)
        {
            Guid? activeDoc = r.ReadBoolean() ? GetGuid(r) : null;
            var tabs = new SessionTab[r.ReadInt32()];
            for (int ti = 0; ti < tabs.Length; ti++)
            {
                var id = GetGuid(r);
                var title = GetString(r);
                long rev = r.ReadInt64();
                var sel = new ByteRange[r.ReadInt32()];
                for (int si = 0; si < sel.Length; si++) sel[si] = new ByteRange(r.ReadInt64(), r.ReadInt64());
                tabs[ti] = new(id, title, rev, sel);
            }
            groups[gi] = new(tabs, activeDoc);
        }
        return new SessionModel(groups, active);
    });

    /// <summary>Document a record belongs to; null for the session state.</summary>
    public static Guid? DocOf(JournalRecord rec)
    {
        if (rec.Type == RecordType.SessionState) return null;
        using var r = new BinaryReader(new MemoryStream(rec.Payload));
        r.ReadByte();
        return GetGuid(r);
    }
}
