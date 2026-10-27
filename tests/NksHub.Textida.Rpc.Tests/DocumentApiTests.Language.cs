using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.4.1 and P8.4.2: diagnostics a plugin publishes and completion a plugin answers, through a real plugin process.</summary>
public partial class DocumentApiTests
{
    static string Diag(long start, long end, string severity = "error", string message = "bad") =>
        $"{{\"start\":{start},\"end\":{end},\"severity\":\"{severity}\",\"message\":\"{message}\",\"code\":\"c1\"}}";

    static string PublishDiag(long revision, string source, params string[] diagnostics) =>
        $"\"revision\":{revision},\"source\":\"{source}\",\"diagnostics\":[{string.Join(",", diagnostics)}]";

    private static IReadOnlyList<ByteRange> Ranges(Rig rig) => rig.Owner.Run(() => rig.Api.DiagnosticsOf(rig.Doc.Id)).Select(d => d.Range).ToList();

    [Fact]
    public async Task DiagnosticsOfTheCurrentRevisionAreStoredInBytesAndFollowTheTextWhenItIsEdited()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\nzz\n"), codec: TextCodec.Utf8);
        var rev = rig.Revision;

        var ok = await rig.CallAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "schema", Diag(4, 6, "warning"), Diag(7, 9))));
        Assert.Equal((2, 0), (ok.GetProperty("accepted").GetInt32(), ok.GetProperty("dropped").GetInt32()));
        var placed = rig.Owner.Run(() => rig.Api.DiagnosticsOf(rig.Doc.Id));
        Assert.Equal([new ByteRange(4, 6), new ByteRange(7, 9)], placed.Select(d => d.Range));
        Assert.Equal([DiagnosticSeverity.Warning, DiagnosticSeverity.Error], placed.Select(d => d.Severity));
        Assert.Equal(("p", "schema", "c1"), (placed[0].PluginId, placed[0].Source, placed[0].Code));
        Assert.True(rig.Owner.Run(() => rig.Api.HasDiagnostics(rig.Doc.Id)));

        // an edit in front moves both; an edit inside the first one takes only that one away
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("xy")));
        Assert.Equal([new ByteRange(6, 8), new ByteRange(9, 11)], Ranges(rig));
        rig.Owner.Run(() => rig.Doc.Replace(new ByteRange(7, 8), Utf8("Q")));
        Assert.Equal([new ByteRange(9, 11)], Ranges(rig));
        Assert.True(rig.Tools.DiagnosticsNotified.Count >= 3);

        // the checker publishes again for the new revision: its old set is replaced, an empty list takes the source away
        await rig.CallAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rig.Revision, "schema", Diag(0, 2))));
        Assert.Equal([new ByteRange(0, 2)], Ranges(rig));
        await rig.CallAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rig.Revision, "schema")));
        Assert.Empty(Ranges(rig));
        Assert.False(rig.Owner.Run(() => rig.Api.HasDiagnostics(rig.Doc.Id)));
    }

    [Fact]
    public async Task DiagnosticsForAnOlderRevisionAreMovedToTheCurrentOneOrDropped()
    {
        await using var rig = await Rig.CreateAsync(Utf8("aa\nbb\ncc\n"), codec: TextCodec.Utf8);
        var older = rig.Revision;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("12")));
        rig.Owner.Run(() => rig.Doc.Replace(new ByteRange(5, 6), Utf8("X"))); // "bb" lost its first byte

        // "bb" was touched since, "cc" only moved, "aa" moved by the insert
        var r = await rig.CallAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(older, "s", Diag(0, 2), Diag(3, 5), Diag(6, 8))));
        Assert.Equal((2, 1), (r.GetProperty("accepted").GetInt32(), r.GetProperty("dropped").GetInt32()));
        Assert.Equal([new ByteRange(2, 4), new ByteRange(8, 10)], Ranges(rig));

        // a revision the document has not reached is a conflict and changes nothing
        await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rig.Revision + 5, "s", Diag(0, 1))), DocumentErrorCodes.RevisionConflict);
        Assert.Equal(2, Ranges(rig).Count);
    }

    [Fact]
    public async Task DiagnosticsAreCheckedLimitedAndGoWhenTheDocumentIsWithdrawn()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\n"), o => o with { MaxDiagnosticsPerDocument = 2 }, codec: TextCodec.Utf8);
        var rev = rig.Revision;
        await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "s", Diag(0, 1))), DocumentErrorCodes.InvalidRange); // inside "é"
        await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "s", Diag(0, 99))), DocumentErrorCodes.InvalidRange);
        await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "s", Diag(3, 2))), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "s", Diag(0, 2, "fatal"))), RpcErrorCodes.InvalidParams);
        await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "", Diag(0, 2))), RpcErrorCodes.InvalidParams);
        var quota = await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "s", Diag(0, 2), Diag(3, 4), Diag(4, 5))), DocumentErrorCodes.DiagnosticsQuotaExceeded);
        Assert.Equal(2, quota.GetProperty("data").GetProperty("limit").GetInt32());
        Assert.Empty(Ranges(rig));

        // a point (empty range) is allowed: the editor draws it one character wide
        await rig.CallAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "s", Diag(4, 4))));
        Assert.Equal([new ByteRange(4, 4)], Ranges(rig));

        rig.Owner.Run(() => { rig.Api.Withdraw(rig.Doc); return 0; });
        Assert.Empty(Ranges(rig));
        Assert.Equal(rig.Doc.Id, rig.Tools.DiagnosticsNotified[^1]);

        rig.Permissions.RevokeAll("p");
        await rig.ErrorAsync(DocumentMethods.DiagnosticsPublish, rig.D(PublishDiag(rev, "s", Diag(0, 2))), DocumentErrorCodes.PermissionDenied);
    }

    private static string Answer(long revision, params string[] items) => $"{{\"revision\":{revision},\"items\":[{string.Join(",", items)}]}}";

    // replaces the "f" at [4,5) with "foo" and adds an import line at the top of the file
    private const string FooItem = "{\"label\":\"foo\",\"detail\":\"fn\",\"kind\":\"function\",\"start\":4,\"end\":5,\"text\":\"foo\",\"edits\":[{\"start\":0,\"end\":0,\"text\":\"import x\\n\"}]}";

    private static Task SetCompletionAsync(Rig rig, string answer, int delayMs = 0) =>
        rig.Host.CallAsync("p", "setCompletion", T.Json($"{{\"answer\":{answer},\"delayMs\":{delayMs}}}"));

    [Fact]
    public async Task ACompletionWithAnEditElsewhereInTheFileIsWrittenAsOneUndoStep()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\n"), codec: TextCodec.Utf8);
        Assert.False(rig.Api.HasCompletionProviders);
        Assert.Empty((await rig.Api.RequestCompletionAsync(rig.Doc.Id, 5)).Items);

        await rig.CallAsync(DocumentMethods.CompletionRegister, "{\"enabled\":true}");
        Assert.True(rig.Api.HasCompletionProviders);
        var rev = rig.Revision;
        await SetCompletionAsync(rig, Answer(rev, FooItem));

        var answer = await rig.Api.RequestCompletionAsync(rig.Doc.Id, 5, default, "rust");
        Assert.False(answer.Stale);
        var item = Assert.Single(answer.Items);
        Assert.Equal(("p", "foo", "fn", "function", rev), (item.PluginId, item.Label, item.Detail, item.Kind, item.Revision));
        var asked = (await rig.Host.CallAsync("p", "completionRequests"))[0];
        Assert.Equal((rev, 5L, "rust"), (asked.GetProperty("revision").GetInt64(), asked.GetProperty("offset").GetInt64(), asked.GetProperty("language").GetString()));

        var depth = rig.Owner.Run(() => rig.Doc.UndoDepth);
        var applied = rig.Owner.Run(() => rig.Api.ApplyCompletion(item));
        Assert.Equal(rev + 1, applied);
        Assert.Equal("import x\né1\nfoo2\n", Text(rig));
        Assert.Equal(depth + 1, rig.Owner.Run(() => rig.Doc.UndoDepth));
        Assert.Equal(16, Assert.Single(rig.Tools.Placed).Items[0].Caret); // after "foo", 9 bytes of import further on

        rig.Owner.Run(() => rig.Doc.Undo());
        Assert.Equal("é1\nf2\n", Text(rig));
    }

    [Fact]
    public async Task AnAnswerForAnOlderRevisionIsNotShownAndAnOfferIsNotAppliedToAChangedDocument()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\n"), codec: TextCodec.Utf8);
        await rig.CallAsync(DocumentMethods.CompletionRegister, "{\"enabled\":true}");
        var rev = rig.Revision;

        // the plugin answers for another revision than it was asked
        await SetCompletionAsync(rig, Answer(rev - 1, FooItem));
        Assert.Empty((await rig.Api.RequestCompletionAsync(rig.Doc.Id, 5)).Items);

        // the document moves on while the plugin thinks
        await SetCompletionAsync(rig, Answer(rev, FooItem), delayMs: 300);
        var pending = rig.Api.RequestCompletionAsync(rig.Doc.Id, 5);
        await Task.Delay(100);
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("z")));
        var late = await pending;
        Assert.True(late.Stale);
        Assert.Empty(late.Items);

        // an offer that was shown and then went stale is refused, nothing is written
        await SetCompletionAsync(rig, Answer(rig.Revision, FooItem.Replace("\"start\":4,\"end\":5", "\"start\":5,\"end\":6")));
        var item = Assert.Single((await rig.Api.RequestCompletionAsync(rig.Doc.Id, 6)).Items);
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("y")));
        var error = Assert.Throws<RpcException>(() => rig.Owner.Run(() => rig.Api.ApplyCompletion(item)));
        Assert.Equal(DocumentErrorCodes.RevisionConflict, error.Code);
        Assert.Equal("yzé1\nf2\n", Text(rig));
        Assert.Empty(rig.Tools.Placed);
    }

    [Fact]
    public async Task CompletionItemsThatAreNotValidForTheRevisionAreLeftOutAndASlowOrMissingPluginGivesNothing()
    {
        await using var rig = await Rig.CreateAsync(Utf8("é1\nf2\n"), o => o with { MaxCompletionItems = 1, CompletionTimeout = TimeSpan.FromMilliseconds(300) }, codec: TextCodec.Utf8);
        await rig.CallAsync(DocumentMethods.CompletionRegister, "{\"enabled\":true}");
        var rev = rig.Revision;

        var inside = "{\"label\":\"a\",\"start\":1,\"end\":2,\"text\":\"a\"}"; // inside "é"
        var overlap = "{\"label\":\"b\",\"start\":4,\"end\":5,\"text\":\"b\",\"edits\":[{\"start\":4,\"end\":6,\"text\":\"c\"}]}";
        await SetCompletionAsync(rig, Answer(rev, inside, overlap, FooItem, FooItem.Replace("foo", "fob")));
        var items = (await rig.Api.RequestCompletionAsync(rig.Doc.Id, 5)).Items;
        Assert.Equal(["foo"], items.Select(i => i.Label)); // the two bad ones are skipped, the item limit keeps the first good one

        await SetCompletionAsync(rig, Answer(rev, FooItem), delayMs: 5000);
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.Empty((await rig.Api.RequestCompletionAsync(rig.Doc.Id, 5)).Items);
        Assert.True(started.ElapsedMilliseconds < 4000, "Completion waited for a plugin that is too slow.");

        await SetCompletionAsync(rig, Answer(rev, FooItem));
        rig.Permissions.RevokeAll("p");
        Assert.Empty((await rig.Api.RequestCompletionAsync(rig.Doc.Id, 5)).Items);
    }
}
