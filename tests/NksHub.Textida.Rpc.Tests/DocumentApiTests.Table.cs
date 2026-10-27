using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P5.1.3: the cells selected in a table view as a model (values, header, revision) and the clipboard written as text and HTML, through a real plugin process.</summary>
public partial class DocumentApiTests
{
    const string Csv = "id,name,note\n1,Ann,\"it's\"\n2,Bob,x|y\n";

    static TableCell Cell(string value, long start, long end) => new(value, new ByteRange(start, end));

    /// <summary>The three columns of <see cref="Csv"/> with their header and both data rows, as the table view would give them.</summary>
    static TableSelectionData ThreeColumns() => new(0, 3,
        [Cell("id", 0, 2), Cell("name", 3, 7), Cell("note", 8, 12)],
        [
            new TableRow(1, [Cell("1", 13, 14), Cell("Ann", 15, 18), Cell("it's", 19, 25)]),
            new TableRow(2, [Cell("2", 26, 27), Cell("Bob", 28, 31), Cell("x|y", 32, 35)]),
        ]);

    [Fact]
    public async Task ASelectionOfThreeColumnsComesWithValuesHeaderAndRevisionAndTheClipboardTakesTextAndHtml()
    {
        await using var rig = await Rig.CreateAsync(Utf8(Csv));
        rig.Permissions.Grant("p", DocumentPermissions.ClipboardWrite, GrantScope.All);
        rig.Tools.Table = ThreeColumns;
        var rev = rig.Revision;

        var selection = await rig.CallAsync(DocumentMethods.TableSelection, rig.D($"\"revision\":{rev}"));
        Assert.Equal(rev, selection.GetProperty("revision").GetInt64());
        Assert.Equal(3, selection.GetProperty("columns").GetInt32());
        Assert.Equal(["id", "name", "note"], selection.GetProperty("header").EnumerateArray().Select(c => c.GetProperty("value").GetString()));
        var rows = selection.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal([1, 2], rows.Select(r => r.GetProperty("row").GetInt32()));
        Assert.Equal(["2", "Bob", "x|y"], rows[1].GetProperty("cells").EnumerateArray().Select(c => c.GetProperty("value").GetString()));
        var cell = rows[0].GetProperty("cells")[2];
        Assert.Equal((19, 25), (cell.GetProperty("start").GetInt32(), cell.GetProperty("end").GetInt32()));

        var written = await rig.CallAsync(DocumentMethods.ClipboardWrite, rig.D("\"text\":\"a\\tb\",\"html\":\"<table></table>\""));
        Assert.True(written.GetProperty("written").GetBoolean());
        await rig.CallAsync(DocumentMethods.ClipboardWrite, rig.D("\"text\":\"only text\""));
        Assert.Equal([("a\tb", "<table></table>"), ("only text", null)], rig.Tools.Clipboard);
    }

    [Fact]
    public async Task ASelectionOfAnOlderRevisionIsAConflictAndNothingIsRead()
    {
        await using var rig = await Rig.CreateAsync(Utf8(Csv));
        int reads = 0;
        rig.Tools.Table = () => { reads++; return ThreeColumns(); };
        var old = rig.Revision;
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("x")));

        var conflict = await rig.ErrorAsync(DocumentMethods.TableSelection, rig.D($"\"revision\":{old}"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal("Stale", conflict.GetProperty("data").GetProperty("reason").GetString());
        Assert.Equal(rig.Revision, conflict.GetProperty("data").GetProperty("currentRevision").GetInt64());
        await rig.ErrorAsync(DocumentMethods.TableSelection, rig.D($"\"revision\":{rig.Revision + 1}"), DocumentErrorCodes.RevisionConflict);
        Assert.Equal(0, reads);
    }

    [Fact]
    public async Task ADocumentWithoutATableViewHasNoSelectionAndTheClipboardNeedsItsOwnGrant()
    {
        await using var rig = await Rig.CreateAsync(Utf8(Csv));
        await rig.ErrorAsync(DocumentMethods.TableSelection, rig.D($"\"revision\":{rig.Revision}"), DocumentErrorCodes.NoTableView);

        await rig.ErrorAsync(DocumentMethods.ClipboardWrite, rig.D("\"text\":\"x\""), DocumentErrorCodes.PermissionDenied);
        Assert.Empty(rig.Tools.Clipboard);
        rig.Permissions.Grant("p", DocumentPermissions.ClipboardWrite, GrantScope.All);
        await rig.ErrorAsync(DocumentMethods.ClipboardWrite, rig.D("\"text\":\"" + new string('x', 1001) + "\""), RpcErrorCodes.MessageTooLarge);
        await rig.ErrorAsync(DocumentMethods.ClipboardWrite, rig.D("\"text\":\"x\",\"html\":5"), RpcErrorCodes.InvalidParams);
        Assert.Empty(rig.Tools.Clipboard);
    }

    [Fact]
    public async Task ASelectionLargerThanTheReadLimitIsRefused()
    {
        await using var rig = await Rig.CreateAsync(Utf8(Csv), o => o with { MaxReadBytes = 8 });
        rig.Tools.Table = ThreeColumns;
        await rig.ErrorAsync(DocumentMethods.TableSelection, rig.D($"\"revision\":{rig.Revision}"), RpcErrorCodes.MessageTooLarge);
    }

    [Fact]
    public async Task ACellOnAHiddenValueOfAnEnvFileReadsAsAStarUntilThePluginMaySeeSecrets()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        var (env, path) = await ExposeEnvAsync(rig, "prod.env", Utf8("K=secret\nL=open\n"));
        try
        {
            rig.Tools.Table = () => new TableSelectionData(0, 1, null,
                [new TableRow(1, [Cell("K=secret", 0, 8)]), new TableRow(2, [Cell("name", 9, 10)])]);
            string Values(JsonElement r) => string.Join(",", r.GetProperty("rows").EnumerateArray().Select(x => x.GetProperty("cells")[0].GetProperty("value").GetString()));
            var asked = $"\"document\":\"{env.Id}\",\"revision\":{env.Revision}";

            Assert.Equal("*,name", Values(await rig.CallAsync(DocumentMethods.TableSelection, "{" + asked + "}")));
            rig.Permissions.Grant("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(env.Id));
            Assert.Equal("K=secret,name", Values(await rig.CallAsync(DocumentMethods.TableSelection, "{" + asked + "}")));
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(env); env.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
