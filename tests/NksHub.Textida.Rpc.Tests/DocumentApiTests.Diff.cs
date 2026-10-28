using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P6.2.2: the diff of two revisions through the API, a diff tab opened by a plugin, and staging one hunk into a virtual document (the index) with a conflict when it moved.</summary>
public partial class DocumentApiTests
{
    static string Side(Guid document, long revision) => $"{{\"document\":\"{document}\",\"revision\":{revision}}}";

    static string SnapshotSide(string snapshot) => $"{{\"snapshot\":\"{snapshot}\"}}";

    static string Compute(string left, string right, string more = "") => $"{{\"left\":{left},\"right\":{right}{more}}}";

    /// <summary>A repository as a plugin sees it: the working tree file (the exposed document) and the index of that file, a virtual document the plugin provides.</summary>
    static async Task<(Rig Rig, Guid Index, long IndexRevision)> RepositoryAsync(string tree, string index, string[]? plugins = null)
    {
        var rig = await Rig.CreateAsync(Utf8(tree), codec: TextCodec.Utf8, plugins: plugins);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        var (id, revision) = await OpenVirtualAsync(rig, "index:file.txt", "file.txt", index);
        return (rig, id, revision);
    }

    [Fact]
    public async Task TheDiffOfTwoRevisionsListsHunksAsLinesAndBytes()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a\nb\nc\nd\ne\n"), codec: TextCodec.Utf8);
        var older = rig.Revision;
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        rig.Owner.Run(() => { rig.Doc.Replace(new ByteRange(2, 3), Utf8("B")); rig.Doc.Replace(new ByteRange(6, 7), Utf8("D")); return 0; });

        var diff = await rig.CallAsync(DocumentMethods.DiffCompute, Compute(SnapshotSide(snapshot), Side(rig.Doc.Id, rig.Revision)));
        Assert.Equal(older, diff.GetProperty("left").GetProperty("revision").GetInt64());
        Assert.Equal(rig.Revision, diff.GetProperty("right").GetProperty("revision").GetInt64());
        Assert.False(diff.GetProperty("identical").GetBoolean());
        Assert.True(diff.GetProperty("exact").GetBoolean());
        Assert.Equal(2, diff.GetProperty("hunkCount").GetInt32());
        var hunks = diff.GetProperty("hunks");
        var first = hunks[0];
        Assert.Equal(0, first.GetProperty("index").GetInt32());
        Assert.Equal((1, 1, 2, 4), (first.GetProperty("left").GetProperty("startLine").GetInt32(), first.GetProperty("left").GetProperty("lines").GetInt32(),
            first.GetProperty("left").GetProperty("start").GetInt32(), first.GetProperty("left").GetProperty("end").GetInt32()));
        Assert.Equal((3, 6, 8), (hunks[1].GetProperty("right").GetProperty("startLine").GetInt32(), hunks[1].GetProperty("right").GetProperty("start").GetInt32(), hunks[1].GetProperty("right").GetProperty("end").GetInt32()));

        var same = await rig.CallAsync(DocumentMethods.DiffCompute, Compute(Side(rig.Doc.Id, rig.Revision), Side(rig.Doc.Id, rig.Revision)));
        Assert.True(same.GetProperty("identical").GetBoolean());
        Assert.Equal(0, same.GetProperty("hunks").GetArrayLength());

        var paged = await rig.CallAsync(DocumentMethods.DiffCompute, Compute(SnapshotSide(snapshot), Side(rig.Doc.Id, rig.Revision), ",\"from\":1"));
        Assert.Equal([1], paged.GetProperty("hunks").EnumerateArray().Select(h => h.GetProperty("index").GetInt32()));
    }

    [Fact]
    public async Task ADiffNamesRevisionsAndRefusesAnythingElse()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a\nb\n"), codec: TextCodec.Utf8);
        var current = rig.Revision;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("x")));

        var stale = await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute(Side(rig.Doc.Id, current), Side(rig.Doc.Id, rig.Revision)), DocumentErrorCodes.RevisionConflict);
        Assert.Equal(rig.Revision, stale.GetProperty("data").GetProperty("currentRevision").GetInt64());
        await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute(SnapshotSide("gone"), Side(rig.Doc.Id, rig.Revision)), DocumentErrorCodes.SnapshotExpired);
        await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute(Side(Guid.NewGuid(), 0), Side(rig.Doc.Id, rig.Revision)), DocumentErrorCodes.UnknownDocument);
        await rig.ErrorAsync(DocumentMethods.DiffCompute, $"{{\"left\":{Side(rig.Doc.Id, rig.Revision)}}}", RpcErrorCodes.InvalidParams);
    }

    [Fact]
    public async Task ADiffReachesOnlyDocumentsThePluginMayReadAndHidesNothingItWasNotGiven()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a\n"), codec: TextCodec.Utf8);
        var other = Document.FromBytes(Utf8("b\n"), PagedBackendFactory.Instance);
        rig.Owner.Run(() => { rig.Api.Expose(other); return 0; });
        rig.Permissions.Revoke("p", DocumentPermissions.Read, GrantScope.All);
        rig.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.OfDocument(rig.Doc.Id));

        var denied = await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute(Side(rig.Doc.Id, rig.Revision), Side(other.Id, other.Revision)), DocumentErrorCodes.PermissionDenied);
        Assert.Equal(DocumentPermissions.Read, denied.GetProperty("data").GetProperty("permission").GetString());
        await rig.ErrorAsync(DocumentMethods.DiffOpen, Compute(Side(other.Id, other.Revision), Side(rig.Doc.Id, rig.Revision)), DocumentErrorCodes.PermissionDenied);
        Assert.Empty(rig.Tools.Diffs);

        // a snapshot taken while the plugin could read the document stops being usable when the grant goes
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        rig.Permissions.Revoke("p", DocumentPermissions.Read, GrantScope.OfDocument(rig.Doc.Id));
        rig.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.OfDocument(other.Id));
        await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute(SnapshotSide(snapshot), Side(other.Id, other.Revision)), DocumentErrorCodes.PermissionDenied);
        rig.Owner.Run(() => { rig.Api.Withdraw(other); other.Dispose(); return 0; });
    }

    [Fact]
    public async Task ADiffOfAFileWithHiddenValuesNeedsTheGrantThatShowsThem()
    {
        await using var rig = await Rig.CreateAsync(Utf8("A=1\n"), codec: TextCodec.Utf8);
        var (env, path) = await ExposeEnvAsync(rig, ".env", Utf8("A=2\nB=3\n"));
        try
        {
            var denied = await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute(Side(rig.Doc.Id, rig.Revision), Side(env.Id, env.Revision)), DocumentErrorCodes.PermissionDenied);
            Assert.Equal("Secret", denied.GetProperty("data").GetProperty("reason").GetString());
            await rig.ErrorAsync(DocumentMethods.DiffOpen, Compute(Side(env.Id, env.Revision), Side(rig.Doc.Id, rig.Revision)), DocumentErrorCodes.PermissionDenied);
            Assert.Empty(rig.Tools.Diffs);

            rig.Permissions.Grant("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(env.Id));
            var diff = await rig.CallAsync(DocumentMethods.DiffCompute, Compute(Side(rig.Doc.Id, rig.Revision), Side(env.Id, env.Revision)));
            Assert.Equal(1, diff.GetProperty("hunkCount").GetInt32());
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(env); env.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task APluginOpensTheDiffTabOfTwoRevisionsAndTheTabKeepsThoseRevisions()
    {
        await using var rig = await Rig.CreateAsync(Utf8("one\ntwo\n"), codec: TextCodec.Utf8);
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        var older = rig.Revision;
        rig.Owner.Run(() => rig.Doc.Replace(new ByteRange(0, 3), Utf8("ONE")));

        var opened = await rig.CallAsync(DocumentMethods.DiffOpen, Compute(SnapshotSide(snapshot), Side(rig.Doc.Id, rig.Revision), ",\"title\":\"HEAD to tree\",\"leftTitle\":\"HEAD\""));
        Assert.True(opened.GetProperty("opened").GetBoolean());
        var request = Assert.Single(rig.Tools.Diffs);
        Assert.Equal(("HEAD to tree", "HEAD", (string?)null), (request.Title, request.Left.Title, request.Right.Title));
        Assert.Equal((older, rig.Revision), (request.Left.Snapshot.Revision, request.Right.Snapshot.Revision));
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("later")));
        Assert.Equal("one\ntwo\n", Encoding.UTF8.GetString(request.Left.Snapshot.ReadBytes(new ByteRange(0, request.Left.Snapshot.ByteLength))));
        Assert.Equal("ONE\ntwo\n", Encoding.UTF8.GetString(request.Right.Snapshot.ReadBytes(new ByteRange(0, request.Right.Snapshot.ByteLength))));

        rig.Tools.DiffShown = false;
        Assert.False((await rig.CallAsync(DocumentMethods.DiffOpen, Compute(SnapshotSide(snapshot), Side(rig.Doc.Id, rig.Revision)))).GetProperty("opened").GetBoolean());
    }

    [Fact]
    public async Task ADiffTabCarriesTheKeyAndTheButtonsOfThePluginAndTheyAreChecked()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a\n"), o => o with { DiffActions = new DiffActionOptions(2, 20) }, codec: TextCodec.Utf8);
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("x")));
        string Open(string more) => Compute(SnapshotSide(snapshot), Side(rig.Doc.Id, rig.Revision), more);

        await rig.CallAsync(DocumentMethods.DiffOpen, Open(",\"key\":\"k\",\"actions\":[{\"command\":\"x.stage\",\"title\":\"Stage\",\"refresh\":\"x.show\"},{\"command\":\"x.undo\",\"title\":\"Undo\"}]"));
        var request = Assert.Single(rig.Tools.Diffs);
        Assert.Equal(("p", "k"), (request.PluginId, request.Key));
        Assert.Equal([("x.stage", "Stage", "x.show"), ("x.undo", "Undo", (string?)null)], request.Actions!.Select(a => (a.Command, a.Title, a.Refresh)));

        foreach (var bad in new[]
        {
            ",\"actions\":[{\"command\":\"a\",\"title\":\"A\"},{\"command\":\"b\",\"title\":\"B\"},{\"command\":\"c\",\"title\":\"C\"}]", // more than the editor takes
            ",\"actions\":[{\"command\":\"a\"}]",                                                                                    // no title
            ",\"actions\":[{\"command\":\"a\",\"title\":\"\"}]",
            ",\"actions\":[{\"command\":\"" + new string('c', 21) + "\",\"title\":\"A\"}]",                                         // too long
            ",\"actions\":[3]",
            ",\"key\":\"" + new string('k', 21) + "\"",
            ",\"key\":7",
        })
            await rig.ErrorAsync(DocumentMethods.DiffOpen, Open(bad), RpcErrorCodes.InvalidParams);
        Assert.Single(rig.Tools.Diffs); // a refused request opens nothing
    }

    [Fact]
    public async Task AnEditorThatTakesNoButtonsOpensTheDiffTabWithoutThem()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a\n"), codec: TextCodec.Utf8);
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;

        var opened = await rig.CallAsync(DocumentMethods.DiffOpen, Compute(SnapshotSide(snapshot), Side(rig.Doc.Id, rig.Revision), ",\"key\":\"k\",\"actions\":[{\"command\":\"a\",\"title\":\"A\"}]"));

        Assert.True(opened.GetProperty("opened").GetBoolean());
        var request = Assert.Single(rig.Tools.Diffs);
        Assert.Null(request.Key);
        Assert.Null(request.Actions);
    }

    [Fact]
    public async Task ADiffSideCanBeTextOrBytesThePluginSuppliesAndAShownOneNeedsTheGrantForOwnContent()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a\nb\n"), codec: TextCodec.Utf8);
        var tree = Side(rig.Doc.Id, rig.Revision);

        var diff = await rig.CallAsync(DocumentMethods.DiffCompute, Compute("{\"text\":\"a\\nB\\n\",\"name\":\"scratch.txt\"}", tree));
        Assert.True(diff.GetProperty("left").GetProperty("detached").GetBoolean());
        Assert.False(diff.GetProperty("left").TryGetProperty("document", out _));
        Assert.Equal(rig.Doc.Id, diff.GetProperty("right").GetProperty("document").GetGuid()); // a document side keeps its shape
        var hunk = Assert.Single(diff.GetProperty("hunks").EnumerateArray());
        Assert.Equal((1, 1, 2, 4), (hunk.GetProperty("left").GetProperty("startLine").GetInt32(), hunk.GetProperty("left").GetProperty("lines").GetInt32(),
            hunk.GetProperty("left").GetProperty("start").GetInt32(), hunk.GetProperty("left").GetProperty("end").GetInt32()));
        var bytes = Convert.ToBase64String(Utf8("a\nb\n"));
        Assert.True((await rig.CallAsync(DocumentMethods.DiffCompute, Compute($"{{\"bytes\":\"{bytes}\"}}", "{\"text\":\"a\\nb\\n\"}"))).GetProperty("identical").GetBoolean());
        await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute("{\"text\":\"x\",\"encoding\":\"no-such-encoding\"}", tree), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute("{\"text\":\"x\",\"bytes\":\"eA==\"}", tree), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute($"{{\"text\":\"x\",\"document\":\"{rig.Doc.Id}\",\"revision\":{rig.Revision}}}", tree), RpcErrorCodes.InvalidParams);

        // the tab shows content the plugin made up, so it needs the grant to provide documents
        var denied = await rig.ErrorAsync(DocumentMethods.DiffOpen, Compute("{\"text\":\"a\\nB\\n\",\"name\":\"scratch.txt\"}", tree), DocumentErrorCodes.PermissionDenied);
        Assert.Equal(DocumentPermissions.Virtual, denied.GetProperty("data").GetProperty("permission").GetString());
        Assert.Empty(rig.Tools.Diffs);
        rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        Assert.True((await rig.CallAsync(DocumentMethods.DiffOpen, Compute("{\"text\":\"a\\nB\\n\",\"name\":\"scratch.txt\"}", tree))).GetProperty("opened").GetBoolean());
        var request = Assert.Single(rig.Tools.Diffs);
        Assert.Equal(("scratch.txt", true, false, (DiffFile?)null), (request.Left.Title, request.Left.Detached, request.Right.Detached, request.Left.File));
        Assert.Equal("a\nB\n", Encoding.UTF8.GetString(request.Left.Snapshot.ReadBytes(new ByteRange(0, request.Left.Snapshot.ByteLength))));
        Assert.NotEqual(request.Left.Snapshot.DocumentId, request.Right.Snapshot.DocumentId);
    }

    [Fact]
    public async Task APinnedSideIsMarkedSoThatOnlyTheOthersGoOutOfDateWithTheirDocument()
    {
        await using var rig = await Rig.CreateAsync(Utf8("a\n"), codec: TextCodec.Utf8);
        var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("x")));

        await rig.CallAsync(DocumentMethods.DiffOpen, Compute(SnapshotSide(snapshot), Side(rig.Doc.Id, rig.Revision)));

        var request = Assert.Single(rig.Tools.Diffs);
        Assert.Equal((true, false, false), (request.Left.Pinned, request.Right.Pinned, request.Right.Detached));
    }

    [Fact]
    public async Task ADiffSideCanBeAFileBelowTheWorkspaceAndNothingElse()
    {
        await using var w = await WorkspaceAsync();
        var file = Path.Combine(w.Root, "a.txt");
        File.WriteAllText(file, "alpha beta\nsecond\n");
        var tree = Side(w.Rig.Doc.Id, w.Rig.Revision);
        string Of(string path) => Compute($"{{\"file\":{J(path)}}}", tree);

        var diff = await w.Rig.CallAsync(DocumentMethods.DiffCompute, Of(file));
        Assert.True(diff.GetProperty("left").GetProperty("detached").GetBoolean());
        Assert.Equal(1, diff.GetProperty("hunkCount").GetInt32());
        w.Rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
        await w.Rig.CallAsync(DocumentMethods.DiffOpen, Of(file));
        var request = Assert.Single(w.Rig.Tools.Diffs);
        Assert.Equal(("a.txt", file, new FileInfo(file).Length, true), (request.Left.Title, request.Left.File!.Path, request.Left.File.Length, request.Left.Detached));
        Assert.Equal(new FileInfo(file).LastWriteTimeUtc, request.Left.File.WriteTimeUtc);

        var outside = Path.Combine(w.Outside, "b.txt");
        File.WriteAllText(outside, "x\n");
        var elsewhere = await w.Rig.ErrorAsync(DocumentMethods.DiffCompute, Of(outside), DocumentErrorCodes.PermissionDenied);
        Assert.Equal("OutsideWorkspace", elsewhere.GetProperty("data").GetProperty("reason").GetString());
        File.WriteAllText(Path.Combine(w.Root, ".env"), "A=1\n");
        var secret = await w.Rig.ErrorAsync(DocumentMethods.DiffCompute, Of(Path.Combine(w.Root, ".env")), DocumentErrorCodes.PermissionDenied);
        Assert.Equal("Secret", secret.GetProperty("data").GetProperty("reason").GetString());
        await w.Rig.ErrorAsync(DocumentMethods.DiffCompute, Of(Path.Combine(w.Root, "missing.txt")), RpcErrorCodes.InvalidParams);
        await w.Rig.ErrorAsync(DocumentMethods.DiffCompute, Of("a.txt"), RpcErrorCodes.InvalidParams);
        Assert.Single(w.Rig.Tools.Diffs);
    }

    [Fact]
    public async Task AFileSideNeedsTheGrantToTouchFiles()
    {
        await using var w = await WorkspaceAsync(filesGranted: false);
        var file = Path.Combine(w.Root, "a.txt");
        File.WriteAllText(file, "x\n");

        var denied = await w.Rig.ErrorAsync(DocumentMethods.DiffCompute, Compute($"{{\"file\":{J(file)}}}", Side(w.Rig.Doc.Id, w.Rig.Revision)), DocumentErrorCodes.PermissionDenied);

        Assert.Equal(DocumentPermissions.WorkspaceFiles, denied.GetProperty("data").GetProperty("permission").GetString());
    }

    [Fact]
    public async Task ASideOverTheDiffLimitIsRefusedWhateverItIsMadeOf()
    {
        var root = T.TempDir();
        await using var rig = await Rig.CreateAsync(Utf8("0123456789\n"), o => o with
        {
            MaxDiffBytes = 16,
            Workspace = new WorkspaceEditOptions(new RecordingWorkspace(), () => [root], _ => false, 10, 1 << 20),
        }, codec: TextCodec.Utf8);
        try
        {
            rig.Permissions.Grant("p", DocumentPermissions.WorkspaceFiles, GrantScope.All);
            rig.Permissions.Grant("p", DocumentPermissions.Virtual, GrantScope.All);
            var tree = Side(rig.Doc.Id, rig.Revision);
            var big = Path.Combine(root, "big.txt");
            File.WriteAllText(big, "0123456789abcdefg");

            // exactly at the limit is allowed
            Assert.Equal(1, (await rig.CallAsync(DocumentMethods.DiffCompute, Compute("{\"text\":\"0123456789abcde\\n\"}", tree))).GetProperty("hunkCount").GetInt32());
            foreach (var left in new[] { "{\"text\":\"0123456789abcdefg\"}", $"{{\"bytes\":\"{Convert.ToBase64String(new byte[17])}\"}}", $"{{\"file\":{J(big)}}}" })
            {
                var refused = await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute(left, tree), DocumentErrorCodes.DiffTooLarge);
                Assert.Equal((16, 17), (refused.GetProperty("data").GetProperty("limitBytes").GetInt32(), refused.GetProperty("data").GetProperty("bytes").GetInt32()));
                await rig.ErrorAsync(DocumentMethods.DiffOpen, Compute(left, tree), DocumentErrorCodes.DiffTooLarge);
            }

            // a document that grew past it is refused too, and so is a snapshot of it
            var snapshot = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString()!;
            rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("0123456789")));
            await rig.ErrorAsync(DocumentMethods.DiffCompute, Compute("{\"text\":\"x\"}", Side(rig.Doc.Id, rig.Revision)), DocumentErrorCodes.DiffTooLarge);
            Assert.True((await rig.CallAsync(DocumentMethods.DiffCompute, Compute("{\"text\":\"x\"}", SnapshotSide(snapshot)))).GetProperty("exact").GetBoolean());
            Assert.Empty(rig.Tools.Diffs);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task OneHunkOfTextTheEditorWasGivenCanBeStagedAsWell()
    {
        var (rig, index, indexRevision) = await RepositoryAsync("x\n", "a\nb\nc\n");
        await using var _ = rig;

        await rig.CallAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision)},\"source\":{{\"text\":\"a\\nB\\nc\\n\"}},\"hunk\":0}}");

        Assert.Equal("a\nB\nc\n", VirtualText(rig, index));
    }

    [Fact]
    public async Task StagingOneHunkTakesThatHunkIntoTheIndexAndLeavesTheOthers()
    {
        var (rig, index, indexRevision) = await RepositoryAsync("a\nB\nc\nD\ne\n", "a\nb\nc\nd\ne\n");
        await using var _ = rig;
        var tree = Side(rig.Doc.Id, rig.Revision);

        var hunks = (await rig.CallAsync(DocumentMethods.DiffCompute, Compute(Side(index, indexRevision), tree))).GetProperty("hunks");
        Assert.Equal(2, hunks.GetArrayLength());

        var staged = await rig.CallAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision)},\"source\":{tree},\"hunk\":1}}");
        Assert.Equal(indexRevision + 1, staged.GetProperty("revision").GetInt64());
        Assert.Equal("a\nb\nc\nD\ne\n", VirtualText(rig, index));

        // the other hunk is still there, now alone, and staging it makes the index equal to the working tree
        var rest = await rig.CallAsync(DocumentMethods.DiffCompute, Compute(Side(index, indexRevision + 1), tree));
        Assert.Equal(1, rest.GetProperty("hunkCount").GetInt32());
        await rig.CallAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision + 1)},\"source\":{tree},\"hunk\":0}}");
        Assert.Equal("a\nB\nc\nD\ne\n", VirtualText(rig, index));
        Assert.True((await rig.CallAsync(DocumentMethods.DiffCompute, Compute(Side(index, indexRevision + 2), tree))).GetProperty("identical").GetBoolean());

        var doc = rig.Tools.Virtuals[0].Document;
        Assert.False(doc.IsModified);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public async Task StagingIntoAnIndexThatChangedMeanwhileIsAConflictAndStagesNothing()
    {
        var (rig, index, indexRevision) = await RepositoryAsync("a\nB\nc\nD\ne\n", "a\nb\nc\nd\ne\n");
        await using var _ = rig;
        var tree = Side(rig.Doc.Id, rig.Revision);
        await rig.CallAsync(DocumentMethods.DiffCompute, Compute(Side(index, indexRevision), tree));

        // another program staged something between the diff and the stage: the plugin put the new index in
        await rig.CallAsync(DocumentMethods.VirtualUpdate, $"{{\"document\":\"{index}\",\"text\":\"a\\nb\\nc\\nd\\ne\\nf\\n\"}}");
        var conflict = await rig.ErrorAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision)},\"source\":{tree},\"hunk\":0}}", DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", conflict.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(indexRevision + 1, conflict.GetProperty("data").GetProperty("currentRevision").GetInt64());
        Assert.Equal("a\nb\nc\nd\ne\nf\n", VirtualText(rig, index));
        Assert.Equal(indexRevision + 1, rig.Tools.Virtuals[0].Document.Revision);

        // after reading the index again the plugin stages against the revision it now has
        var hunks = (await rig.CallAsync(DocumentMethods.DiffCompute, Compute(Side(index, indexRevision + 1), tree))).GetProperty("hunks");
        Assert.Equal(3, hunks.GetArrayLength());
        await rig.CallAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision + 1)},\"source\":{tree},\"hunk\":0}}");
        Assert.Equal("a\nB\nc\nd\ne\nf\n", VirtualText(rig, index));
    }

    [Fact]
    public async Task OnlyThePluginThatProvidesTheIndexStagesIntoItAndAHunkMustExist()
    {
        var (rig, index, indexRevision) = await RepositoryAsync("a\nB\n", "a\nb\n", plugins: ["p", "q"]);
        await using var _ = rig;
        var tree = Side(rig.Doc.Id, rig.Revision);

        await rig.ErrorAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision)},\"source\":{tree},\"hunk\":0}}", DocumentErrorCodes.NotProvider, plugin: "q");
        await rig.ErrorAsync(DocumentMethods.DiffStage, $"{{\"base\":{tree},\"source\":{Side(index, indexRevision)},\"hunk\":0}}", DocumentErrorCodes.NotProvider);
        var missing = await rig.ErrorAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision)},\"source\":{tree},\"hunk\":5}}", DocumentErrorCodes.InvalidRange);
        Assert.Equal(1, missing.GetProperty("data").GetProperty("hunkCount").GetInt32());
        await rig.ErrorAsync(DocumentMethods.DiffStage, $"{{\"base\":{Side(index, indexRevision)},\"source\":{Side(index, indexRevision)},\"hunk\":0}}", RpcErrorCodes.InvalidParams);
        Assert.Equal("a\nb\n", VirtualText(rig, index));
    }
}
