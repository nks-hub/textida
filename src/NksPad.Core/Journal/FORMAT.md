# Journal format

One journal file per running editor (`session-<id>.njl`, lock file `session-<id>.lock`). It holds the unsaved work of all
open documents and the tab layout, so a crash or a hot exit loses at most one flush interval (default 500 ms, at most 1 s;
the target is 2 s). The journal never writes a user file; backups and autosave (`Backups.cs`) are separate.

## File

All integers are little endian.

| Part | Layout |
| --- | --- |
| File header, 16 bytes | magic `NKSJRNL\0`, u16 version, u16 flags (0), u32 CRC-32 of the first 12 bytes |
| Record | u32 payload length, u32 CRC-32, u8 type, u64 sequence, i64 Unix time in ms (version 2), payload |

The CRC covers every record byte except the CRC field. Sequence numbers start at 1 and rise by 1.

Version 2 is current. Version 1 has no timestamp field and is only read: `JournalReader` accepts it, and recovery copies
the records into a version 2 journal when it adopts the file (tested migration). A newer version or a damaged header makes the file
unreadable; it is reported and left untouched.

## Torn writes

The reader stops at the first record that is cut short, fails its CRC, breaks the sequence or has an unknown type. Everything
before it is used, everything from it on is ignored (`JournalTail.Truncated` or `Corrupt`). The writer appends only whole batches and
stops for good after a failed write (`Fault`), so a torn record can only be the last one.

## Durability

`FsyncPolicy.Interval` (default) collects records in memory and writes them with one `fsync` per flush interval, or sooner when a
megabyte is waiting. `EveryRecord` writes and syncs each record. `Manual` writes only on `Flush`/`Dispose` (tests). A save writes
a `DocSaveIntent` and syncs at once, before the new file replaces the old one.

## Records

Payloads start with a layout byte (1) followed by the fields below (`BinaryWriter` encoding, GUIDs as 16 bytes).

| Type | Content |
| --- | --- |
| 1 SessionState | active group, groups: active document, tabs (document id, title, revision, selections as byte ranges) |
| 2 DocOpen | document id, path and fingerprint (named) or none, encoding kind/BOM/code page, backend name, base revision, seed length |
| 3 DocSeed | document id, offset, bytes: initial content of an unnamed document, in chunks |
| 4 DocEdit | document id, revision, kind, source, "starts a new undo step", edits (start, deleted length, inserted bytes) in the coordinates of the previous revision |
| 5 DocUndo, 6 DocRedo | document id, revision; replayed by calling `Undo()`/`Redo()` |
| 7 DocSave | document id, revision, path, new fingerprint |
| 8 DocClose | document id |
| 9 DocSaveIntent | document id, revision, path, size and SHA-256 of the file about to be moved into place |

Named documents are recorded as path plus fingerprint (size and SHA-256), never as content, so a paged 100 MiB file costs a few
hundred bytes. Unnamed documents carry their initial bytes as seeds.

## Recovery

1. Closed documents are dropped. For each document everything up to its last save is replaced by a new `DocOpen` that points at
   the saved file (base revision = revision of the save). A `DocSaveIntent` after the last save counts as a save when the file on
   disk has the intended hash (the process died after the swap, before the save record).
2. A named document is opened from its file. If the file no longer matches the fingerprint and there are edits, the result is
   `Conflict` (or `Missing`): nothing is applied and the records are kept. Otherwise the edits are replayed through
   `Document.Apply`/`Undo`/`Redo`, and every revision is checked against the record.
3. The rebuilt document has the same bytes, revision (base + revision), undo steps and unsaved state as before the crash.
   Undo history from before a save is not kept. Undo/redo that go past a save point are recorded as plain edits, so the bytes are
   exact while the undo steps may differ.
4. `JournalSession.Start` adopts journals whose lock file is free: it copies the surviving records into the new journal, syncs it,
   and only then deletes the old file. Selections of the session are mapped to the recovered revision through the change log; a
   selection that cannot be mapped is dropped.

## Compaction

`Compact()` (automatic past `CompactThresholdBytes`) applies the reduction of step 1 and keeps the newest session state. It
writes `<journal>.tmp`, syncs it and moves it over the journal, so a crash leaves either the old or the new file.
