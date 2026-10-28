using System.Security.Cryptography;
using System.Text.Json;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.4.3: a workspace edit through a real plugin process. The batch is checked as a whole before the review sees it.</summary>
public partial class DocumentApiTests
{
    sealed class RecordingWorkspace : IWorkspaceEdits
    {
        public readonly List<WorkspaceEditRequest> Items = [];

        public ValueTask<ProposalDecision> SubmitAsync(WorkspaceEditRequest request, CancellationToken cancellation)
        {
            lock (Items) { Items.Add(request); return ValueTask.FromResult(new ProposalDecision("w" + Items.Count, ProposalOutcome.Applied, request.Proposal.ChangeCount, request.Proposal.ChangeCount)); }
        }
    }

    sealed record WorkspaceRig(Rig Rig, RecordingWorkspace Service, string Root, string Outside) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Rig.DisposeAsync();
            // links of the test go first: a recursive delete is refused on a junction made by mklink /J, on a machine without the right to make symbolic links
            foreach (var link in new DirectoryInfo(Root).EnumerateDirectories().Where(d => d.LinkTarget is not null)) link.Delete();
            Directory.Delete(Root, true);
            Directory.Delete(Outside, true);
        }
    }

    static string J(string s) => JsonSerializer.Serialize(s);

    static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static async Task<WorkspaceRig> WorkspaceAsync(bool filesGranted = true, Func<string, bool>? isOpen = null, long totalBytes = long.MaxValue, Func<string, Document?>? openDocument = null)
    {
        var service = new RecordingWorkspace();
        var root = T.TempDir();
        var outside = T.TempDir();
        var rig = await Rig.CreateAsync(Utf8("alpha beta\n"), o => o with { Workspace = new WorkspaceEditOptions(service, () => [root], isOpen ?? (_ => false), 10, 1 << 20, totalBytes) { OpenDocument = openDocument } });
        if (filesGranted) rig.Permissions.Grant("p", DocumentPermissions.WorkspaceFiles, GrantScope.All);
        return new WorkspaceRig(rig, service, root, outside);
    }

    [Fact]
    public async Task AWorkspaceEditWithEditCreateRenameAndDeleteBecomesOneAtomicProposal()
    {
        await using var w = await WorkspaceAsync();
        var edited = Path.Combine(w.Root, "edited.txt");
        var renamed = Path.Combine(w.Root, "old.txt");
        var deleted = Path.Combine(w.Root, "gone.txt");
        File.WriteAllText(edited, "one two\n");
        File.WriteAllText(renamed, "r");
        File.WriteAllText(deleted, "d");
        var created = Path.Combine(w.Root, "new.txt");
        var rev = w.Rig.Revision;

        var answer = await w.Rig.CallAsync(DocumentMethods.WorkspaceEdit, "{\"title\":\"Rename symbol\",\"changes\":[" +
            $"{{\"kind\":\"edit\",\"document\":\"{w.Rig.Doc.Id}\",\"revision\":{rev},\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"ALPHA\"}}]}}," +
            $"{{\"kind\":\"edit\",\"path\":{J(edited)},\"sha256\":\"{Sha(edited)}\",\"edits\":[{{\"start\":0,\"end\":3,\"text\":\"1\"}}]}}," +
            $"{{\"kind\":\"create\",\"path\":{J(created)},\"text\":\"hello\"}}," +
            $"{{\"kind\":\"rename\",\"path\":{J(renamed)},\"to\":{J(Path.Combine(w.Root, "renamed.txt"))}}}," +
            $"{{\"kind\":\"delete\",\"path\":{J(deleted)},\"sha256\":\"{Sha(deleted)}\"}}]}}");

        Assert.Equal(("applied", 5, 5), (answer.GetProperty("decision").GetString(), answer.GetProperty("accepted").GetInt32(), answer.GetProperty("total").GetInt32()));
        var request = Assert.Single(w.Service.Items);
        var proposal = request.Proposal;
        Assert.Equal(("p", "Rename symbol", "plugin:p", true), (request.PluginId, proposal.Title, proposal.Origin, proposal.Atomic));
        Assert.Equal(5, proposal.Files.Count);
        Assert.Equal(w.Rig.Doc.Id, proposal.Files[0].DocumentId);
        Assert.Equal(rev, proposal.Files[0].BaseRevision);
        Assert.Equal("ALPHA", proposal.Files[0].Hunks[0].NewText);
        Assert.Equal(Sha(edited), proposal.Files[1].BaseFingerprint!.Sha256);
        Assert.Equal([FileOperationKind.Create, FileOperationKind.Rename, FileOperationKind.Delete], proposal.Files.Skip(2).Select(f => f.Operation!.Kind));
        Assert.Equal("hello"u8.ToArray(), proposal.Files[2].Operation!.Content);
        Assert.Equal(Path.Combine(w.Root, "renamed.txt"), proposal.Files[3].Operation!.NewPath);
        // checked and shown only: nothing has been written
        Assert.False(File.Exists(created));
        Assert.True(File.Exists(renamed));
        Assert.True(File.Exists(deleted));
        Assert.Equal(rev, w.Rig.Revision);
    }

    [Fact]
    public async Task AChangeOutsideTheAllowedScopeRefusesTheWholeBatch()
    {
        await using var w = await WorkspaceAsync();
        var inside = Path.Combine(w.Root, "a.txt");
        File.WriteAllText(inside, "x");
        var elsewhere = Path.Combine(w.Outside, "b.txt");
        var document = $"{{\"kind\":\"edit\",\"document\":\"{w.Rig.Doc.Id}\",\"revision\":{w.Rig.Revision},\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"A\"}}]}}";

        // one good change next to a path outside the workspace: nothing reaches the review
        var outside = await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{document},{{\"kind\":\"create\",\"path\":{J(elsewhere)},\"text\":\"x\"}}]}}", DocumentErrorCodes.PermissionDenied);
        Assert.Equal("OutsideWorkspace", outside.GetProperty("data").GetProperty("reason").GetString());
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{document},{{\"kind\":\"create\",\"path\":{J(Path.Combine(w.Root, "..", Path.GetFileName(w.Outside), "c.txt"))},\"text\":\"x\"}}]}}", DocumentErrorCodes.PermissionDenied);
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{document},{{\"kind\":\"create\",\"path\":\"relative.txt\",\"text\":\"x\"}}]}}", RpcErrorCodes.InvalidParams);
        Assert.Empty(w.Service.Items);

        // a path edit needs the workspace.files permission, a document the grant for that document
        w.Rig.Permissions.Revoke("p", DocumentPermissions.WorkspaceFiles, GrantScope.All);
        var denied = await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{document},{{\"kind\":\"edit\",\"path\":{J(inside)},\"sha256\":\"{Sha(inside)}\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"y\"}}]}}]}}", DocumentErrorCodes.PermissionDenied);
        Assert.Equal(DocumentPermissions.WorkspaceFiles, denied.GetProperty("data").GetProperty("permission").GetString());
        w.Rig.Permissions.Revoke("p", DocumentPermissions.EditPropose, GrantScope.All);
        w.Rig.Permissions.Grant("p", DocumentPermissions.EditPropose, GrantScope.OfDocument(Guid.NewGuid()));
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{document}]}}", DocumentErrorCodes.PermissionDenied);

        // the same file twice, a secrets file, a missing file
        w.Rig.Permissions.Grant("p", DocumentPermissions.WorkspaceFiles, GrantScope.All);
        w.Rig.Permissions.Grant("p", DocumentPermissions.EditPropose, GrantScope.All);
        var twice = $"{{\"kind\":\"delete\",\"path\":{J(inside)},\"sha256\":\"{Sha(inside)}\"}}";
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{twice},{twice}]}}", RpcErrorCodes.InvalidParams);
        var secrets = Path.Combine(w.Root, ".env");
        File.WriteAllText(secrets, "KEY=value\n");
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"edit\",\"path\":{J(secrets)},\"sha256\":\"{Sha(secrets)}\",\"edits\":[{{\"start\":4,\"end\":9,\"text\":\"*\"}}]}}]}}", DocumentErrorCodes.PermissionDenied);
        Assert.Empty(w.Service.Items);
    }

    private static bool TryLinkFolder(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true })!;
            mklink.WaitForExit();
            return mklink.ExitCode == 0;
        }
    }

    [Fact]
    public async Task AFolderLinkInTheWorkspaceThatLeadsOutRefusesTheWholeBatchAndOneThatStaysInsideDoesNot()
    {
        await using var w = await WorkspaceAsync();
        string inner = Directory.CreateDirectory(Path.Combine(w.Root, "real")).FullName;
        string toOutside = Path.Combine(w.Root, "out"), toInside = Path.Combine(w.Root, "in");
        if (!TryLinkFolder(toOutside, w.Outside) || !TryLinkFolder(toInside, inner)) { Assert.False(OperatingSystem.IsWindows()); return; }
        string document = $"{{\"kind\":\"edit\",\"document\":\"{w.Rig.Doc.Id}\",\"revision\":{w.Rig.Revision},\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"A\"}}]}}";

        // the path is inside the workspace by its name, and outside by where the link leads
        var escape = await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{document},{{\"kind\":\"create\",\"path\":{J(Path.Combine(toOutside, "e.txt"))},\"text\":\"x\"}}]}}", DocumentErrorCodes.PermissionDenied);
        Assert.Equal("Link", escape.GetProperty("data").GetProperty("reason").GetString());
        File.WriteAllText(Path.Combine(w.Outside, "o.txt"), "o");
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"delete\",\"path\":{J(Path.Combine(toOutside, "o.txt"))},\"sha256\":\"{Sha(Path.Combine(w.Outside, "o.txt"))}\"}}]}}", DocumentErrorCodes.PermissionDenied);
        Assert.Empty(w.Service.Items);
        Assert.True(File.Exists(Path.Combine(w.Outside, "o.txt")));

        // a link that stays inside the workspace is an ordinary folder
        var inside = await w.Rig.CallAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"create\",\"path\":{J(Path.Combine(toInside, "n.txt"))},\"text\":\"x\"}}]}}");
        Assert.Equal("applied", inside.GetProperty("decision").GetString());
        Assert.Single(w.Service.Items);
    }

    [Fact]
    public async Task ManyChangesCannotBringMoreNewTextThanTheTotalLimitAndTheWholeBatchIsRefused()
    {
        await using var w = await WorkspaceAsync(totalBytes: 100);
        string at(int n) => Path.Combine(w.Root, $"n{n}.txt");
        string create(int n, int bytes) => $"{{\"kind\":\"create\",\"path\":{J(at(n))},\"text\":\"{new string('x', bytes)}\"}}";

        var ok = await w.Rig.CallAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{create(1, 60)},{create(2, 40)}]}}"); // exactly the limit
        Assert.Equal("applied", ok.GetProperty("decision").GetString());

        var over = await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{create(3, 60)},{create(4, 41)}]}}", DocumentErrorCodes.WorkspaceQuotaExceeded);
        Assert.Equal(100, over.GetProperty("data").GetProperty("limitBytes").GetInt64());
        string replaced = Path.Combine(w.Root, "r.txt");
        File.WriteAllText(replaced, "abc");
        // the replacement texts of an edit count as well
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"edit\",\"path\":{J(replaced)},\"sha256\":\"{Sha(replaced)}\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"{new string('y', 101)}\"}}]}}]}}", DocumentErrorCodes.WorkspaceQuotaExceeded);
        Assert.Single(w.Service.Items);
    }

    [Fact]
    public async Task AnOpenFileCanBeRenamedOrDeletedButIsEditedAsADocument()
    {
        await using var w = await WorkspaceAsync(isOpen: path => Path.GetFileName(path) == "open.txt");
        var open = Path.Combine(w.Root, "open.txt");
        File.WriteAllText(open, "text");

        var renamed = await w.Rig.CallAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"rename\",\"path\":{J(open)},\"to\":{J(Path.Combine(w.Root, "moved.txt"))},\"sha256\":\"{Sha(open)}\"}}]}}");
        var deleted = await w.Rig.CallAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"delete\",\"path\":{J(open)},\"sha256\":\"{Sha(open)}\"}}]}}");
        Assert.Equal(("applied", "applied"), (renamed.GetProperty("decision").GetString(), deleted.GetProperty("decision").GetString()));
        Assert.Equal([FileOperationKind.Rename, FileOperationKind.Delete], w.Service.Items.Select(i => i.Proposal.Files.Single().Operation!.Kind));
        Assert.Equal(open, w.Service.Items[1].Proposal.Files.Single().Path);

        // a text edit by path would bypass the revision of the open document
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"edit\",\"path\":{J(open)},\"sha256\":\"{Sha(open)}\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"T\"}}]}}]}}", RpcErrorCodes.InvalidParams);
        Assert.Equal(2, w.Service.Items.Count);
        Assert.Equal("text", File.ReadAllText(open));
    }

    [Fact]
    public async Task ATextEditByPathOfAFileATabShowsGoesThroughTheOpenDocumentWhileTheTabHoldsTheContentThePluginSaw()
    {
        Document? shown = null;
        string open = "";
        await using var w = await WorkspaceAsync(isOpen: path => path == open, openDocument: path => path == open ? shown : null);
        shown = w.Rig.Doc;
        open = Path.Combine(w.Root, "open.txt");
        File.WriteAllBytes(open, Utf8("alpha beta
")); // the content the tab holds
        var change = $"{{\"kind\":\"edit\",\"path\":{J(open)},\"sha256\":\"{Sha(open)}\",\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"ALPHA\"}}]}}";

        var answer = await w.Rig.CallAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{change}]}}");

        Assert.Equal("applied", answer.GetProperty("decision").GetString());
        var file = Assert.Single(Assert.Single(w.Service.Items).Proposal.Files);
        Assert.Equal((w.Rig.Doc.Id, w.Rig.Revision), (file.DocumentId, file.BaseRevision)); // an edit of the document, checked against its revision
        var hunk = Assert.Single(file.Hunks);
        Assert.Equal(("alpha", "ALPHA"), (hunk.OldText, hunk.NewText));
        Assert.Equal("alpha beta
", File.ReadAllText(open)); // the review writes it, not the API

        // the tab has unsaved changes: the bytes the plugin read are no longer what the tab holds
        w.Rig.Owner.Run(() => w.Rig.Doc.Insert(0, Utf8("x")));
        var unsaved = await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{change}]}}", DocumentErrorCodes.RevisionConflict);
        Assert.Equal("UnsavedTab", unsaved.GetProperty("data").GetProperty("reason").GetString());
        Assert.Single(w.Service.Items);
    }

    [Fact]
    public async Task AStaleRevisionOrHashIsAConflictThatNamesTheChange()
    {
        await using var w = await WorkspaceAsync();
        var file = Path.Combine(w.Root, "a.txt");
        File.WriteAllText(file, "text");
        var oldHash = Sha(file);
        var rev = w.Rig.Revision;
        w.Rig.Owner.Run(() => w.Rig.Doc.Insert(0, Utf8("x")));

        var stale = await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit,
            $"{{\"changes\":[{{\"kind\":\"create\",\"path\":{J(Path.Combine(w.Root, "n.txt"))},\"text\":\"\"}},{{\"kind\":\"edit\",\"document\":\"{w.Rig.Doc.Id}\",\"revision\":{rev},\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"A\"}}]}}]}}",
            DocumentErrorCodes.RevisionConflict);
        Assert.Equal(("Stale", 1), (stale.GetProperty("data").GetProperty("reason").GetString(), stale.GetProperty("data").GetProperty("index").GetInt32()));

        File.WriteAllText(file, "changed");
        var hash = await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit,
            $"{{\"changes\":[{{\"kind\":\"edit\",\"path\":{J(file)},\"sha256\":\"{oldHash}\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"T\"}}]}}]}}", DocumentErrorCodes.RevisionConflict);
        Assert.Equal(0, hash.GetProperty("data").GetProperty("index").GetInt32());
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"delete\",\"path\":{J(file)},\"sha256\":\"{oldHash}\"}}]}}", DocumentErrorCodes.RevisionConflict);
        await w.Rig.ErrorAsync(DocumentMethods.WorkspaceEdit, $"{{\"changes\":[{{\"kind\":\"create\",\"path\":{J(file)},\"text\":\"\"}}]}}", DocumentErrorCodes.RevisionConflict);
        Assert.Empty(w.Service.Items);
    }
}
