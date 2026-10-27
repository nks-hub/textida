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

Version 3 is current. It has the record layout of version 2; what is new is payload layout 6 (large edits, below), which a reader
of version 2 would not understand, so it must refuse the file. Versions 1 and 2 are only read: `JournalReader` accepts them, and recovery copies
the records into a version 3 journal when it adopts the file (tested migration). Version 1 has no timestamp field. A newer version or a damaged header makes the file
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

Payloads start with a layout byte followed by the fields below (`BinaryWriter` encoding, GUIDs as 16 bytes). Layout 6 is current (layout 5 plus a reference to the inserted bytes of an edit); layout 5 added the hex bookmarks of a session tab; layout 4 added a repeat length per edit of `DocEdit`. Layout 1 stored the encoding as kind, BOM length and
code page and had no codec field in `DocEdit`, `DocSave` and `DocSaveIntent`; it is still read (the codec is derived from the old fields, or the
opening codec is kept). The codec is always stored by its stable `TextCodec.Name` and resolved with `TextCodec.ByName`; an unknown name fails
that document, not the journal.

| Type | Content |
| --- | --- |
| 1 SessionState | active group, groups: active document, tabs (document id, title, revision, selections as byte ranges, bookmarks as byte offsets of line starts; layout 3; and from layout 5 the hex bookmarks: name, colour, comment and type as optional strings, start and end byte offset of the region, which a recovery maps through the changes since the session was written) |
| 2 DocOpen | document id, path and fingerprint (named) or none, codec name, backend name, base revision, seed length |
| 3 DocSeed | document id, offset, bytes: initial content of an unnamed document, in chunks |
| 4 DocEdit | document id, revision, kind, source, "starts a new undo step", edits (start, deleted length, inserted bytes, and from layout 4 a repeat length: when it is not 0 the inserted bytes are a pattern and the edit inserts that many bytes of it repeated, a fill; and from layout 6 an external flag: when it is set the inserted bytes are empty and a payload reference follows, see Large edits) in the coordinates of the previous revision, codec name after the change (a different codec means a conversion, replayed as one undo step) |
| 5 DocUndo, 6 DocRedo | document id, revision; replayed by calling `Undo()`/`Redo()` |
| 7 DocSave | document id, revision, path, new fingerprint, codec name |
| 8 DocClose | document id |
| 9 DocSaveIntent | document id, revision (the one the file holds, which can be older than the document, see Recovery), path, size and SHA-256 of the file about to be moved into place, codec name |
| 10 DocCodec | document id, revision, codec name: "re-read as", replayed with `ReinterpretAs` |
| 17 DocMove | document id, new path: the file was renamed or moved on disk, the unsaved work follows it; recovery opens the new path |
| 11-16 Batch* | Replace in Files batch, see below; these types only appear in `batch-<id>.njl`, never in a session journal |

Named documents are recorded as path plus fingerprint (size and SHA-256), never as content, so a paged 100 MiB file costs a few
hundred bytes. Unnamed documents carry their initial bytes as seeds.

## Large edits

An edit whose inserted bytes exceed `JournalPlaceholders.MaxInlineEditBytes` (1 MiB, a placeholder: the owner has not decided the limit) is not copied into the
record. The record carries a payload reference instead: file name, offset, length, and CRC-32 of the first and of the last 4 KiB. The file lives in the
`payload` directory next to the journals and is named `<session>-<document>-<kind>.payload`.

- `add-N`: the spill file of the add buffer of a paged document. The document makes it a durable file (not delete-on-close) once the journal tracks it
  (`IByteBackend.UseDurableSpill`); the block is already there, so nothing is copied. A document whose add buffer was on disk before tracking has a scratch file
  that dies with the process, so its large edits take the next route.
- `blob-N`: an append-only file of the journal for blocks that no backend keeps on disk itself; the journal copies the block there.

Before the journal writes a batch of records (`JournalWriter.BeforeWrite`), every payload file referred to by it is fsynced, so a record never reaches the disk
before its bytes do. A kill at any moment leaves either the record with its bytes, or no record.

Recovery reads the block, checks length, file size and both checksums, and replays the edit like an inline one. A missing file, a short file or a block that no longer
matches its checksums makes the document `Failed` with the reason; nothing is applied, the user file is not touched, and the records stay in the new journal. The
checksums do not cover the middle of a block: a bit flip there is not detected.

Payload files are removed when the document is closed (`Untrack`), after a save for everything that no newer record needs (the file the backend writes to is kept),
at compaction, and at start for documents nobody knows, except files of a session whose journal still exists.

## Recovery

1. Closed documents are dropped. For each document everything up to its last save is replaced by a new `DocOpen` that points at
   the saved file (base revision = revision of the save, codec = codec at the save). A save that streams a new file while the user keeps editing
   records its edits with revisions above the saved one before the save record (and the intent); those edits are not in the saved file and are kept. A `DocSaveIntent` after the last save counts as a save when the file on
   disk has the intended size and hash (the process died after the swap, before the save record; the size is compared first, so a file of tens of GiB is not hashed for nothing).
   A temporary file of a save that died, and a target parked by an interrupted swap, are cleaned up by `FileSwap.RecoverAbandoned` when the document is opened.
2. A `DocMove` after the last save changes the path of the start record to the new one. A named document is opened from its file. If the file no longer matches the fingerprint and there are edits, the result is
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

## Replace batch journal

A batch of file replacements (`Changes/ChangeApplier.cs`) has its own journal `batch-<id>.njl` with a lock file, in the same format.
Document targets are not part of it: their edits are recorded by the session journal like any other edit.

| Type | Content |
| --- | --- |
| 11 BatchBegin | batch id, title, origin, creation time, number of files |
| 12 BatchFilePlan | batch id, file index, path, codec name, size and SHA-256 of the content the plan is based on, hunks (start, old bytes, new bytes) |
| 13 BatchFileIntent | batch id, file index, size and SHA-256 of the new content, name of the temporary file; written and fsynced before the new file is moved into place |
| 14 BatchFileDone | batch id, file index (not fsynced on its own) |
| 15 BatchFileSkipped | batch id, file index, reason (the file no longer matched its base) |
| 16 BatchEnd | batch id; the journal is deleted right after it |

`BatchBegin` and all plans are fsynced once before the first file is touched, so a plan is either whole or no file was changed.
After a crash the state of every file comes from its hash, not from `BatchFileDone`: the base hash means untouched, the intended hash
(from the intent) means applied, anything else means someone else changed it and the file is left alone. `BatchRecovery` offers to
finish (apply the plans of untouched files) or to roll back (reverse the hunks of applied files, checked against the base hash);
both are safe to repeat, because a crash during recovery leaves files in the same hash-defined states.
