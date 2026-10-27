using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.Core.Journal;

/// <summary>Start of a document in the journal: either a file and its fingerprint, or an unnamed document whose bytes follow as seeds.</summary>
public sealed record OpenRecord(Guid DocId, string? Path, FileFingerprint? Fingerprint, string Codec, string Backend, long BaseRevision, long SeedLength);

public sealed record SeedRecord(Guid DocId, long Offset, byte[] Bytes);

/// <param name="Repeat">When not 0, <paramref name="Inserted"/> is a pattern and the edit inserts this many bytes of it repeated (a fill).</param>
/// <param name="External">When set, the inserted bytes are not in the record: <paramref name="Inserted"/> is empty and they are read from a payload file.</param>
public sealed record ChangeEdit(long Start, long Deleted, byte[] Inserted, long Repeat = 0, PayloadRef? External = null);

/// <summary>A change in the coordinates of the previous revision. <see cref="NewUndoEntry"/> is false when the edit joined the previous undo step.</summary>
/// <param name="Codec">Name of the codec after the change; null in version 1 records. A different codec means the edit was a conversion.</param>
public sealed record EditRecord(Guid DocId, long Revision, ChangeKind Kind, string? Source, bool NewUndoEntry, IReadOnlyList<ChangeEdit> Edits, string? Codec = null);

/// <summary>"Re-read as": the document shows the same bytes through another codec.</summary>
public sealed record CodecRecord(Guid DocId, long Revision, string Codec);

/// <summary>Undo or redo; replayed by calling <c>Undo()</c>/<c>Redo()</c>, so one step of the user is one entry.</summary>
public sealed record UndoRedoRecord(Guid DocId, long Revision);

/// <param name="Codec">Codec of the document at the save (null in version 1 records); recovery reopens the saved file with it.</param>
public sealed record SaveRecord(Guid DocId, long Revision, string Path, FileFingerprint Fingerprint, string? Codec = null);

/// <summary>Written and fsynced before a save moves the new file into place; if the process dies in the swap, recovery recognises the file by its hash.</summary>
/// <summary>The file of a document was renamed or moved on disk; its unsaved work now belongs to <paramref name="Path"/>.</summary>
public sealed record MoveRecord(Guid DocId, string Path);

public sealed record SaveIntentRecord(Guid DocId, long Revision, string Path, long Size, string Sha256, string? Codec = null);

/// <summary>Tabs of the session. <see cref="Revision"/> is the revision of the document the selections refer to. <see cref="Bookmarks"/> are byte offsets of line starts at the same revision, <see cref="HexBookmarks"/> the regions marked in the hex view.</summary>
public sealed record SessionTab(Guid DocId, string? Title, long Revision, IReadOnlyList<ByteRange> Selections, IReadOnlyList<long>? Bookmarks = null, IReadOnlyList<StoredHexBookmark>? HexBookmarks = null)
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
    /// <summary>Payload layout: 1 stored the encoding as kind, BOM length and code page; 2 stores the codec name, 3 adds the bookmarks of a session tab, 4 adds the repeat length of an edit, 5 adds the hex bookmarks of a session tab, 6 lets an edit refer to its inserted bytes in a payload file.</summary>
    private const int PayloadLayout = 6;

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
            w.Write((byte)PayloadLayout);
            fill(w);
        }
        return ms.ToArray();
    }

    private static T Parse<T>(byte[] payload, Func<BinaryReader, int, T> read)
    {
        using var r = new BinaryReader(new MemoryStream(payload), Encoding.UTF8);
        int layout = r.ReadByte();
        if (layout is < 1 or > PayloadLayout) throw new JournalFormatException("Unknown record layout.");
        return read(r, layout);
    }

    private static T Parse<T>(byte[] payload, Func<BinaryReader, T> read) => Parse(payload, (r, _) => read(r));

    /// <summary>Codec of a layout 1 record (old encoding kinds: 0 ASCII, 1 UTF-8, 2/3 UTF-16 LE/BE, 4/5 UTF-32 LE/BE, 6 single byte).</summary>
    private static string LegacyCodec(byte kind, int bomLength, int codePage) => (kind switch
    {
        0 or 1 => TextCodec.Utf8.WithBom(bomLength > 0),
        2 => TextCodec.Utf16LE.WithBom(bomLength > 0),
        3 => TextCodec.Utf16BE.WithBom(bomLength > 0),
        4 => TextCodec.Utf32LE.WithBom(bomLength > 0),
        5 => TextCodec.Utf32BE.WithBom(bomLength > 0),
        _ => codePage switch { 1252 => TextCodec.Cp1252, 28592 => TextCodec.Latin2, _ => TextCodec.Cp1250 },
    }).Name;

    // ---- records ----

    public static byte[] Encode(OpenRecord o) => Build(w =>
    {
        PutGuid(w, o.DocId);
        PutString(w, o.Path);
        PutPrint(w, o.Fingerprint);
        w.Write(o.Codec);
        w.Write(o.Backend);
        w.Write(o.BaseRevision);
        w.Write(o.SeedLength);
    });

    public static OpenRecord DecodeOpen(byte[] p) => Parse(p, (r, layout) =>
    {
        var id = GetGuid(r);
        var path = GetString(r);
        var print = GetPrint(r);
        string codec = layout == 1 ? LegacyCodec(r.ReadByte(), r.ReadInt32(), r.ReadInt32()) : r.ReadString();
        return new OpenRecord(id, path, print, codec, r.ReadString(), r.ReadInt64(), r.ReadInt64());
    });

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
            w.Write(x.Repeat);
            w.Write(x.External is not null);
            if (x.External is { } p)
            {
                w.Write(p.File);
                w.Write(p.Offset);
                w.Write(p.Length);
                w.Write(p.HeadCrc);
                w.Write(p.TailCrc);
            }
        }
        PutString(w, e.Codec);
    });

    public static EditRecord DecodeEdit(byte[] p) => Parse(p, (r, layout) =>
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
            var inserted = r.ReadBytes(r.ReadInt32());
            long repeat = layout >= 4 ? r.ReadInt64() : 0;
            PayloadRef? external = layout >= 6 && r.ReadBoolean() ? new(r.ReadString(), r.ReadInt64(), r.ReadInt64(), r.ReadUInt32(), r.ReadUInt32()) : null;
            edits[i] = new(start, deleted, inserted, repeat, external);
        }
        return new EditRecord(id, rev, kind, source, isNew, edits, layout >= 2 ? GetString(r) : null);
    });

    public static byte[] Encode(CodecRecord c) => Build(w =>
    {
        PutGuid(w, c.DocId);
        w.Write(c.Revision);
        w.Write(c.Codec);
    });

    public static CodecRecord DecodeCodec(byte[] p) => Parse(p, r => new CodecRecord(GetGuid(r), r.ReadInt64(), r.ReadString()));

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
        PutString(w, s.Codec);
    });

    public static SaveRecord DecodeSave(byte[] p) => Parse(p, (r, layout) => new SaveRecord(GetGuid(r), r.ReadInt64(), r.ReadString(), GetPrint(r)!, layout >= 2 ? GetString(r) : null));

    public static byte[] Encode(SaveIntentRecord s) => Build(w =>
    {
        PutGuid(w, s.DocId);
        w.Write(s.Revision);
        w.Write(s.Path);
        w.Write(s.Size);
        w.Write(s.Sha256);
        PutString(w, s.Codec);
    });

    public static SaveIntentRecord DecodeSaveIntent(byte[] p) => Parse(p, (r, layout) => new SaveIntentRecord(GetGuid(r), r.ReadInt64(), r.ReadString(), r.ReadInt64(), r.ReadString(), layout >= 2 ? GetString(r) : null));

    public static byte[] Encode(MoveRecord m) => Build(w =>
    {
        PutGuid(w, m.DocId);
        w.Write(m.Path);
    });

    public static MoveRecord DecodeMove(byte[] p) => Parse(p, r => new MoveRecord(GetGuid(r), r.ReadString()));

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
                w.Write(t.Bookmarks?.Count ?? 0);
                foreach (var b in t.Bookmarks ?? []) w.Write(b);
                w.Write(t.HexBookmarks?.Count ?? 0);
                foreach (var h in t.HexBookmarks ?? [])
                {
                    w.Write(h.Name);
                    PutString(w, h.Color);
                    PutString(w, h.Comment);
                    PutString(w, h.Type);
                    w.Write(h.Start);
                    w.Write(h.End);
                }
            }
        }
    });

    public static SessionModel DecodeSession(byte[] p) => Parse(p, (r, layout) =>
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
                var marks = new long[layout >= 3 ? r.ReadInt32() : 0];
                for (int bi = 0; bi < marks.Length; bi++) marks[bi] = r.ReadInt64();
                var hexMarks = new StoredHexBookmark[layout >= 5 ? r.ReadInt32() : 0];
                for (int hi = 0; hi < hexMarks.Length; hi++) hexMarks[hi] = new(r.ReadString(), GetString(r), GetString(r), GetString(r), r.ReadInt64(), r.ReadInt64());
                tabs[ti] = new(id, title, rev, sel, marks, hexMarks);
            }
            groups[gi] = new(tabs, activeDoc);
        }
        return new SessionModel(groups, active);
    });

    /// <summary>Revision of an edit, undo, redo or codec record (the field right after the document id).</summary>
    public static long RevisionOf(JournalRecord rec) => System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(rec.Payload.AsSpan(17));

    /// <summary>Document a record belongs to; null for the session state.</summary>
    public static Guid? DocOf(JournalRecord rec)
    {
        if (rec.Type == RecordType.SessionState) return null;
        using var r = new BinaryReader(new MemoryStream(rec.Payload));
        r.ReadByte();
        return GetGuid(r);
    }
}
