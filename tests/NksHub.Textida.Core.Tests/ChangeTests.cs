using System.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class ChangeTests
{
    private static readonly SearchSettings Settings = new() { ChunkBytes = 1 << 16, MaxResults = int.MaxValue, StopAtLimit = false, Walk = new FileWalkOptions { UseIgnoreFiles = false } };

    private sealed record Sample(string Name, byte[] Original, Encoding Enc, byte[] Bom);

    /// <summary>Files in several encodings, each with OLD tokens, CRLF and LF lines, and characters outside ASCII.</summary>
    private static List<Sample> MakeFiles(string dir)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text = string.Concat(Enumerable.Range(0, 400).Select(i => $"line {i} OLD value ě{i % 7} {(i % 3 == 0 ? "OLD OLD" : "keep")}{(i % 5 == 0 ? "\r\n" : "\n")}"));
        var samples = new List<Sample>
        {
            Make("utf8.txt", text, new UTF8Encoding(false), []),
            Make("utf8bom.txt", text, new UTF8Encoding(false), TextCodec.Utf8Bom.Bom),
            Make("utf16le.txt", text, new UnicodeEncoding(false, false), TextCodec.Utf16LEBom.Bom),
            Make("utf16be.txt", text, new UnicodeEncoding(true, false), TextCodec.Utf16BEBom.Bom),
            Make("cp1250.txt", text, Encoding.GetEncoding(1250), []),
        };
        foreach (var s in samples) File.WriteAllBytes(Path.Combine(dir, s.Name), s.Original);
        File.WriteAllText(Path.Combine(dir, "untouched.txt"), "nothing here\n");
        return samples;

        static Sample Make(string name, string text, Encoding enc, byte[] bom) => new(name, [.. bom, .. enc.GetBytes(text)], enc, bom);
    }

    private static byte[] Expected(Sample s, string find, string replace) =>
        [.. s.Bom, .. s.Enc.GetBytes(s.Enc.GetString(s.Original, s.Bom.Length, s.Original.Length - s.Bom.Length).Replace(find, replace, StringComparison.Ordinal))];

    private static Task<ReplacePlan> Plan(string dir, string find, string replace, bool regex = false, params LiveBuffer[] buffers) =>
        ReplacePlanner.PlanAsync(new SearchPattern(find, regex), replace, new SearchScope([dir], buffers), Settings);

    [Fact]
    public async Task ReplaceInFilesKeepsEveryOtherByteAndEncodingOfEachFile()
    {
        using var dir = new JournalTempDir();
        var samples = MakeFiles(dir.Path);
        var plan = await Plan(dir.Path, "OLD", "NEW-ě");
        Assert.Equal(5, plan.Proposal.Files.Count);
        Assert.Empty(plan.Skipped);

        var first = plan.Proposal.Files.Single(f => f.DisplayName.EndsWith("utf8.txt")).Hunks[0];
        Assert.Equal(("OLD", "NEW-ě", 1), (first.OldText, first.NewText, first.Line));
        Assert.Equal("line 0 ", first.LineBefore);
        Assert.StartsWith(" value", first.LineAfter);

        var report = new ChangeApplier(_ => null, dir.Combine("journal")).Apply(plan.Proposal);

        Assert.Equal(5, report.Applied);
        foreach (var s in samples)
            Assert.Equal(Expected(s, "OLD", "NEW-ě"), File.ReadAllBytes(Path.Combine(dir.Path, s.Name)));
        Assert.Equal("nothing here\n", File.ReadAllText(dir.Combine("untouched.txt")));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
        Assert.Empty(Directory.GetFiles(dir.Combine("journal"), "batch-*"));
        Assert.Contains("5 applied", report.Summary);
    }

    [Fact]
    public async Task InvalidBytesOutsideTheHunksSurviveVerbatim()
    {
        using var dir = new JournalTempDir();
        var bytes = new List<byte>();
        for (int i = 0; i < 80000; i++) bytes.AddRange(Encoding.UTF8.GetBytes($"prefix {i} OLD\n"));
        bytes.AddRange([0xFF, 0xFE, 0xC3, 0x0A]);
        bytes.AddRange("tail OLD\n"u8);
        string path = dir.Combine("mixed.txt");
        File.WriteAllBytes(path, [.. bytes]);

        var plan = await Plan(dir.Path, "OLD", "N");
        var file = Assert.Single(plan.Proposal.Files);
        Assert.Equal(TextCodec.Utf8, file.Codec);
        new ChangeApplier(_ => null).Apply(plan.Proposal);

        var expected = new List<byte>();
        var src = bytes.ToArray();
        for (int i = 0; i < src.Length;)
        {
            if (i + 2 < src.Length && src[i] == 'O' && src[i + 1] == 'L' && src[i + 2] == 'D') { expected.Add((byte)'N'); i += 3; }
            else expected.Add(src[i++]);
        }
        Assert.Equal(expected.ToArray(), File.ReadAllBytes(path));
    }

    [Fact]
    public async Task AFileChangedByAnotherProgramIsAConflictAndTheRestIsApplied()
    {
        using var dir = new JournalTempDir();
        var samples = MakeFiles(dir.Path);
        var plan = await Plan(dir.Path, "OLD", "NEW");
        string victim = dir.Combine("utf16le.txt");
        File.AppendAllText(victim, "x");  // size differs
        string quiet = dir.Combine("cp1250.txt");
        var original = File.ReadAllBytes(quiet);
        var same = original.ToArray();
        same[10] ^= 0x01;                 // same size, other content
        File.WriteAllBytes(quiet, same);

        var report = new ChangeApplier(_ => null, dir.Combine("journal")).Apply(plan.Proposal);

        Assert.Equal(3, report.Applied);
        Assert.Equal(2, report.Conflicts);
        Assert.All(report.Files.Where(f => f.Status == ChangeStatus.Conflict), f =>
        {
            Assert.Equal(ChangeConflict.ExternalChange, f.Conflict);
            Assert.True(f.NeedsRepreview);
        });
        Assert.Equal(same, File.ReadAllBytes(quiet));
        Assert.Equal(Expected(samples[0], "OLD", "NEW"), File.ReadAllBytes(dir.Combine("utf8.txt")));

        var again = await Plan(dir.Path, "OLD", "NEW");
        Assert.Equal(2, again.Proposal.Files.Count);
        Assert.Equal(2, new ChangeApplier(_ => null).Apply(again.Proposal).Applied);
    }

    [Fact]
    public async Task FilesAndSingleHunksCanBeRejected()
    {
        using var dir = new JournalTempDir();
        var samples = MakeFiles(dir.Path);
        var plan = await Plan(dir.Path, "OLD", "NEW");
        var utf8 = plan.Proposal.Files.Single(f => f.DisplayName.EndsWith("utf8.txt"));
        var cp = plan.Proposal.Files.Single(f => f.DisplayName.EndsWith("cp1250.txt"));
        var selection = new ChangeSelection().RejectFile(cp).RejectHunk(utf8, 0).RejectHunk(utf8, 2);

        var report = new ChangeApplier(_ => null).Apply(plan.Proposal, selection);

        Assert.Equal(1, report.Rejected);
        Assert.Equal(utf8.Hunks.Count - 2, report.Files.Single(f => f.Key == utf8.Key).HunksApplied);
        Assert.Equal(File.ReadAllBytes(dir.Combine("cp1250.txt")), samples[4].Original);
        string text = File.ReadAllText(dir.Combine("utf8.txt"));
        Assert.StartsWith("line 0 OLD value ě0 NEW OLD\r\n", text);       // hunks 0 and 2 kept out, hunk 1 applied
        Assert.Contains("NEW", text.Split('\n')[3]);
    }

    [Fact]
    public async Task ReplacementsTheEncodingCannotHoldAreShownButNeverApplied()
    {
        using var dir = new JournalTempDir();
        var samples = MakeFiles(dir.Path);
        var plan = await Plan(dir.Path, "OLD", "NEW😀");
        var cp = plan.Proposal.Files.Single(f => f.DisplayName.EndsWith("cp1250.txt"));
        Assert.All(cp.Hunks, h => Assert.NotNull(h.Problem));
        Assert.All(plan.Proposal.Files.Where(f => f != cp), f => Assert.All(f.Hunks, h => Assert.Null(h.Problem)));

        var report = new ChangeApplier(_ => null).Apply(plan.Proposal);
        Assert.Equal(1, report.Rejected);
        Assert.Equal(samples[4].Original, File.ReadAllBytes(dir.Combine("cp1250.txt")));
    }

    [Fact]
    public async Task RegexReplacementsExpandGroups()
    {
        using var dir = new JournalTempDir();
        File.WriteAllText(dir.Combine("a.txt"), "id=12 id=7 other\n");
        var plan = await Plan(dir.Path, @"id=(\d+)", "key:$1;", regex: true);
        new ChangeApplier(_ => null).Apply(plan.Proposal);
        Assert.Equal("key:12; key:7; other\n", File.ReadAllText(dir.Combine("a.txt")));
    }

    [Fact]
    public async Task AnOpenDocumentGetsOneUndoStepAndAStaleRevisionIsRefused()
    {
        using var dir = new JournalTempDir();
        string path = dir.Combine("open.txt");
        File.WriteAllText(path, "OLD one\nOLD two\n");
        using var doc = Doc.Open(path, ReferenceBackendFactory.Instance);
        doc.Insert(doc.ByteLength, "OLD unsaved\n"u8.ToArray());
        int undoDepth = doc.UndoDepth;

        var plan = await Plan(dir.Path, "OLD", "NEW", false, LiveBuffer.Of(doc));
        var change = Assert.Single(plan.Proposal.Files);
        Assert.Equal(doc.Id, change.DocumentId);
        Assert.Equal(3, change.Hunks.Count);

        // another edit after the preview makes the preview stale: nothing is applied
        doc.Insert(0, "x"u8.ToArray());
        var stale = new ChangeApplier(id => id == doc.Id ? doc : null).Apply(plan.Proposal);
        Assert.Equal(ChangeConflict.StaleRevision, stale.Files[0].Conflict);
        Assert.True(stale.Files[0].NeedsRepreview);
        Assert.Equal("xOLD one\nOLD two\nOLD unsaved\n", doc.ReadText(new ByteRange(0, doc.ByteLength)));

        var fresh = await Plan(dir.Path, "OLD", "NEW", false, LiveBuffer.Of(doc));
        int depth = doc.UndoDepth;
        long revision = doc.Revision;
        var report = new ChangeApplier(id => id == doc.Id ? doc : null).Apply(fresh.Proposal);
        Assert.Equal(1, report.Applied);
        Assert.Equal(depth + 1, doc.UndoDepth);
        Assert.Equal(revision + 1, doc.Revision);
        Assert.Equal("xNEW one\nNEW two\nNEW unsaved\n", doc.ReadText(new ByteRange(0, doc.ByteLength)));
        Assert.Equal("OLD one\nOLD two\n", File.ReadAllText(path));  // the file on disk is not touched
        doc.Undo();
        Assert.Equal("xOLD one\nOLD two\nOLD unsaved\n", doc.ReadText(new ByteRange(0, doc.ByteLength)));
        Assert.True(undoDepth < doc.UndoDepth);
    }

    [Fact]
    public async Task AClosedDocumentIsAConflict()
    {
        using var doc = Doc.FromBytes("OLD\n"u8.ToArray(), ReferenceBackendFactory.Instance);
        using var dir = new JournalTempDir();
        var plan = await Plan(dir.Path, "OLD", "NEW", false, LiveBuffer.Of(doc));
        var report = new ChangeApplier(_ => null).Apply(plan.Proposal);
        Assert.Equal(ChangeConflict.DocumentUnavailable, report.Files[0].Conflict);
    }

    [Fact]
    public async Task AReadOnlyFileFailsAloneAndLeavesNoTemporaryFile()
    {
        using var dir = new JournalTempDir();
        File.WriteAllText(dir.Combine("a.txt"), "OLD\n");
        File.WriteAllText(dir.Combine("b.txt"), "OLD\n");
        var plan = await Plan(dir.Path, "OLD", "NEW");
        File.SetAttributes(dir.Combine("a.txt"), FileAttributes.ReadOnly);
        try
        {
            var report = new ChangeApplier(_ => null, dir.Combine("j")).Apply(plan.Proposal);
            Assert.Equal(1, report.Failed);
            Assert.Equal(1, report.Applied);
            Assert.Equal("OLD\n", File.ReadAllText(dir.Combine("a.txt")));
            Assert.Equal("NEW\n", File.ReadAllText(dir.Combine("b.txt")));
            Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
        }
        finally
        {
            File.SetAttributes(dir.Combine("a.txt"), FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task ProposalsFromOtherProducersUseTheSamePath()
    {
        using var dir = new JournalTempDir();
        string path = dir.Combine("a.txt");
        File.WriteAllText(path, "hello world\n");
        var edit = ChangeBuilder.ForFile(path, [new TextReplacement(new ByteRange(6, 11), "there")]);
        Assert.Equal(("world", "there"), (edit.Hunks[0].OldText, edit.Hunks[0].NewText));
        Assert.Throws<ArgumentException>(() => ChangeBuilder.ForFile(path, [new TextReplacement(new ByteRange(6, 8), "x"), new TextReplacement(new ByteRange(7, 9), "y")]));
        var report = new ChangeApplier(_ => null).Apply(ChangeBuilder.Proposal("rename", "recipe:test", edit));
        Assert.Equal(1, report.Applied);
        Assert.Equal("hello there\n", File.ReadAllText(path));
        await Task.CompletedTask;
    }

    // ---- crash recovery, in process: a fault point that throws stands for the process dying there ----

    public static IEnumerable<object[]> CrashPoints()
    {
        yield return ["plan"];
        for (int i = 0; i < 5; i++)
        {
            yield return [$"before-swap:{i}"];
            yield return [$"after-swap:{i}"];
            yield return [$"after-file:{i}"];
        }
        yield return ["before-end"];
    }

    private sealed class Crash : Exception;

    [Theory]
    [MemberData(nameof(CrashPoints))]
    public async Task AnInterruptedBatchCanBeFinishedOrRolledBackToTheExpectedFiles(string point)
    {
        using var dir = new JournalTempDir();
        var samples = MakeFiles(dir.Path);
        string journal = dir.Combine("journal");
        var plan = await Plan(dir.Path, "OLD", "NEW-ě");
        var applier = new ChangeApplier(_ => null, journal) { FaultPoint = p => { if (p == point) throw new Crash(); } };
        Assert.Throws<Crash>(() => applier.Apply(plan.Proposal));

        string Snapshot(string name) => Path.Combine(dir.Path, name);
        var originalBytes = samples.ToDictionary(s => s.Name, s => s.Original);
        var expectedBytes = samples.ToDictionary(s => s.Name, s => Expected(s, "OLD", "NEW-ě"));

        // finish: a copy of the folder must end with every file replaced
        string copy = dir.Combine("copy");
        CopyDirectory(dir.Path, copy, skip: "copy");
        using (var pending = Assert.Single(BatchRecovery.Find(journal)))
        {
            var outcomes = pending.Finish();
            Assert.All(outcomes, o => Assert.Equal(ChangeStatus.Applied, o.Status));
        }
        foreach (var s in samples) Assert.Equal(expectedBytes[s.Name], File.ReadAllBytes(Snapshot(s.Name)));
        Assert.Empty(BatchRecovery.Find(journal));

        // roll back: restore the interrupted state first, from the copy
        foreach (var f in Directory.GetFiles(copy)) File.Copy(f, Path.Combine(dir.Path, Path.GetFileName(f)), overwrite: true);
        foreach (var f in Directory.GetFiles(Path.Combine(copy, "journal"))) File.Copy(f, Path.Combine(journal, Path.GetFileName(f)), overwrite: true);
        using (var pending = Assert.Single(BatchRecovery.Find(journal)))
        {
            Assert.All(pending.Files, f => Assert.True(f.State is BatchFileState.NotStarted or BatchFileState.Applied));
            Assert.All(pending.RollBack(), o => Assert.NotEqual(ChangeStatus.Failed, o.Status));
        }
        foreach (var s in samples) Assert.Equal(originalBytes[s.Name], File.ReadAllBytes(Snapshot(s.Name)));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    private static void CopyDirectory(string from, string to, string skip)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: true);
        foreach (var d in Directory.GetDirectories(from).Where(d => Path.GetFileName(d) != skip)) CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)), skip);
    }

    [Fact]
    public async Task RecoveryLeavesAFileSomeoneElseChangedAloneAndIsRepeatable()
    {
        using var dir = new JournalTempDir();
        var samples = MakeFiles(dir.Path);
        string journal = dir.Combine("journal");
        var plan = await Plan(dir.Path, "OLD", "NEW");
        var applier = new ChangeApplier(_ => null, journal) { FaultPoint = p => { if (p == "after-file:1") throw new Crash(); } };
        Assert.Throws<Crash>(() => applier.Apply(plan.Proposal));

        string other = Path.Combine(dir.Path, plan.Proposal.Files[3].DisplayName);
        File.AppendAllText(other, "edited meanwhile");
        var edited = File.ReadAllBytes(other);

        using (var pending = Assert.Single(BatchRecovery.Find(journal)))
        {
            Assert.Equal([BatchFileState.Applied, BatchFileState.Applied, BatchFileState.NotStarted, BatchFileState.Diverged, BatchFileState.NotStarted], pending.Files.Select(f => f.State));
            var outcomes = pending.Finish();
            Assert.Equal(ChangeStatus.Conflict, outcomes[3].Status);
            Assert.Equal(4, outcomes.Count(o => o.Status == ChangeStatus.Applied));
        }
        Assert.Equal(edited, File.ReadAllBytes(other));
        Assert.Empty(BatchRecovery.Find(journal));
        Assert.True(samples.Count == 5);
    }

    [Fact]
    public async Task ARunningBatchIsNotOfferedForRecoveryAndAnUnreadablePlanIsDropped()
    {
        using var dir = new JournalTempDir();
        MakeFiles(dir.Path);
        string journal = dir.Combine("journal");
        var plan = await Plan(dir.Path, "OLD", "NEW");
        IReadOnlyList<PendingBatch>? seenWhileRunning = null;
        var applier = new ChangeApplier(_ => null, journal) { FaultPoint = p => { if (p == "plan") seenWhileRunning = BatchRecovery.Find(journal); } };
        applier.Apply(plan.Proposal);
        Assert.Empty(seenWhileRunning!);

        Directory.CreateDirectory(journal);
        File.WriteAllBytes(Path.Combine(journal, "batch-garbage.njl"), [1, 2, 3]);
        Assert.Empty(BatchRecovery.Find(journal));
        Assert.Empty(Directory.GetFiles(journal, "batch-*.njl"));
    }
}

[Collection("Crash harness")]
public class BatchCrashHarnessTest
{
    private static FileChange Operation(string path, FileOperation operation, FileFingerprint? print = null) =>
        new(path, path, null, null, print, TextCodec.Utf8, [], operation);

    [Fact]
    public void FileOperationsAreDoneInOrderAndADeletedFileIsGoneOnlyWhenTheBatchEnds()
    {
        using var dir = new JournalTempDir();
        string gone = dir.Combine("gone.txt"), old = dir.Combine("old.txt");
        File.WriteAllText(gone, "bye");
        File.WriteAllText(old, "content");
        var proposal = ChangeBuilder.Proposal("ops", "test",
            Operation(dir.Combine("new.txt"), new FileOperation(FileOperationKind.Create, Content: [1, 2, 3])),
            Operation(old, new FileOperation(FileOperationKind.Rename, dir.Combine("renamed.txt"))),
            Operation(gone, new FileOperation(FileOperationKind.Delete), FileFingerprint.TryRead(gone))) with { Atomic = true };

        var report = new ChangeApplier(_ => null).Apply(proposal);

        Assert.Equal(3, report.Applied);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(dir.Combine("new.txt")));
        Assert.Equal("content", File.ReadAllText(dir.Combine("renamed.txt")));
        Assert.Empty(Directory.GetFiles(dir.Path, "gone.txt*"));
    }

    [Fact]
    public void AnAtomicBatchWithAStaleDeleteChangesNothing()
    {
        using var dir = new JournalTempDir();
        string target = dir.Combine("target.txt"), edited = dir.Combine("edited.txt");
        File.WriteAllText(target, "seen");
        File.WriteAllText(edited, "one two");
        var proposal = ChangeBuilder.Proposal("ops", "test",
            ChangeBuilder.ForFile(edited, [new TextReplacement(new ByteRange(0, 3), "1")]),
            Operation(dir.Combine("new.txt"), new FileOperation(FileOperationKind.Create, Content: [])),
            Operation(target, new FileOperation(FileOperationKind.Delete), FileFingerprint.TryRead(target))) with { Atomic = true };
        File.WriteAllText(target, "changed by someone else");

        var report = new ChangeApplier(_ => null, dir.Combine("journal")).Apply(proposal);

        Assert.Equal((0, 1), (report.Applied, report.Conflicts));
        Assert.Equal(ChangeConflict.ExternalChange, report.Files[2].Conflict);
        Assert.Equal("one two", File.ReadAllText(edited));
        Assert.False(File.Exists(dir.Combine("new.txt")));
        Assert.Equal("changed by someone else", File.ReadAllText(target));
    }

    [Fact]
    public void AFileOperationThatFailsUndoesTheOnesBeforeIt()
    {
        if (!OperatingSystem.IsWindows()) return; // relies on an open file refusing to be moved
        using var dir = new JournalTempDir();
        string locked = dir.Combine("locked.txt");
        File.WriteAllText(locked, "x");
        string old = dir.Combine("old.txt");
        File.WriteAllText(old, "y");
        var proposal = ChangeBuilder.Proposal("ops", "test",
            Operation(dir.Combine("new.txt"), new FileOperation(FileOperationKind.Create, Content: [7])),
            Operation(old, new FileOperation(FileOperationKind.Rename, dir.Combine("old2.txt"))),
            Operation(locked, new FileOperation(FileOperationKind.Rename, dir.Combine("locked2.txt")))) with { Atomic = true };

        ApplyReport report;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None)) report = new ChangeApplier(_ => null).Apply(proposal);

        Assert.Equal([ChangeStatus.Rejected, ChangeStatus.Rejected, ChangeStatus.Failed], report.Files.Select(f => f.Status));
        Assert.False(File.Exists(dir.Combine("new.txt")));
        Assert.True(File.Exists(old));
        Assert.False(File.Exists(dir.Combine("old2.txt")));
        Assert.True(File.Exists(locked));
    }

    [Fact]
    public void KillingTheEditorInsideAReplaceBatchLeavesABatchThatFinishesOrRollsBackToTheExpectedFiles()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Journal.CrashHarness.dll");
        Assert.True(File.Exists(dll), dll);
        using var dir = new JournalTempDir();
        var psi = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in new[] { dll, "batch", "--iterations", "10", "--parallel", "4", "--seed", "7000", "--work", dir.Combine("w") }) psi.ArgumentList.Add(a);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var output = proc.StandardOutput.ReadToEndAsync();
        var error = proc.StandardError.ReadToEndAsync();
        Assert.True(proc.WaitForExit(240_000), "harness timed out");
        Assert.True(proc.ExitCode == 0, output.Result + error.Result);
        Assert.Contains("failures 0", output.Result);
    }
}
