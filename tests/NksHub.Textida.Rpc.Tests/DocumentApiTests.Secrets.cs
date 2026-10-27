using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P4.6.2: a plugin that was not allowed to see the values of a secrets file gets stars in their place, in every way it can read them.</summary>
public partial class DocumentApiTests
{
    const string EnvText = "# keys\nAPI_KEY=hunter2\nexport TOKEN=\"abc def\" # note\nEMPTY=\nPATH_X=/usr/bin\n";

    static string WriteEnv(string name, byte[] content)
    {
        var path = Path.Combine(T.TempDir(), name);
        File.WriteAllBytes(path, content);
        return path;
    }

    static async Task<(Document Doc, string Path)> ExposeEnvAsync(Rig rig, string name, byte[] content)
    {
        var path = WriteEnv(name, content);
        var doc = Document.Open(path, PagedBackendFactory.Instance);
        rig.Owner.Run(() => { rig.Api.Expose(doc); return 0; });
        await Task.CompletedTask;
        return (doc, path);
    }

    static string ReadParams(Document doc, long start, long end) => $"{{\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"start\":{start},\"end\":{end}}}";

    [Fact]
    public async Task ValuesOfAnEnvFileReadAsStarsInEveryReadAndTheCallLogHoldsNone()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        var bytes = Utf8(EnvText);
        var (doc, path) = await ExposeEnvAsync(rig, ".env", bytes);
        try
        {
            string masked = "# keys\nAPI_KEY=*******\nexport TOKEN=********* # note\nEMPTY=\nPATH_X=" + new string('*', 8) + "\n";

            var read = await rig.CallAsync(DocumentMethods.ReadBytes, ReadParams(doc, 0, bytes.Length));
            Assert.Equal(Utf8(masked), FromB64(read));
            var text = await rig.CallAsync(DocumentMethods.ReadText, ReadParams(doc, 0, bytes.Length));
            Assert.Equal(masked, text.GetProperty("text").GetString());
            // a range cut inside a value hides only what lies in it
            var part = await rig.CallAsync(DocumentMethods.ReadBytes, ReadParams(doc, 17, 22));
            Assert.Equal(Utf8("*****"), FromB64(part));

            var snapshot = await rig.CallAsync(DocumentMethods.SnapshotCreate, $"{{\"document\":\"{doc.Id}\"}}");
            var viaSnapshot = await rig.CallAsync(DocumentMethods.ReadText, $"{{\"snapshot\":\"{snapshot.GetProperty("snapshot").GetString()}\",\"start\":0,\"end\":{bytes.Length}}}");
            Assert.Equal(masked, viaSnapshot.GetProperty("text").GetString());

            Assert.DoesNotContain("hunter2", string.Join("\n", rig.CallLog.Entries().Select(e => e.ToString())));
            Assert.Equal(bytes, rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength))));
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task OnlyTheVariableFilesAreHiddenAndAGrantForOneDocumentShowsTheValues()
    {
        await using var rig = await Rig.CreateAsync(Utf8("A=1\n"));
        var (env, envPath) = await ExposeEnvAsync(rig, "prod.env", Utf8("K=secret\n"));
        var (plain, plainPath) = await ExposeEnvAsync(rig, "notes.txt", Utf8("K=secret\n"));
        try
        {
            Assert.Equal(Utf8("K=secret\n"), FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, ReadParams(plain, 0, 9))));
            Assert.Equal(Utf8("K=******\n"), FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, ReadParams(env, 0, 9))));

            rig.Permissions.Grant("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(plain.Id));
            Assert.Equal(Utf8("K=******\n"), FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, ReadParams(env, 0, 9))));
            rig.Permissions.Grant("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(env.Id));
            Assert.Equal(Utf8("K=secret\n"), FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, ReadParams(env, 0, 9))));
            rig.Permissions.Revoke("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(env.Id));
            Assert.Equal(Utf8("K=******\n"), FromB64(await rig.CallAsync(DocumentMethods.ReadBytes, ReadParams(env, 0, 9))));
        }
        finally
        {
            rig.Owner.Run(() => { rig.Api.Withdraw(env); rig.Api.Withdraw(plain); env.Dispose(); plain.Dispose(); return 0; });
            Directory.Delete(Path.GetDirectoryName(envPath)!, true);
            Directory.Delete(Path.GetDirectoryName(plainPath)!, true);
        }
    }

    [Fact]
    public async Task ValuesOfAUtf16FileAreHiddenWithoutBreakingTheText()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        var bytes = new UnicodeEncoding(false, true).GetPreamble().Concat(new UnicodeEncoding(false, false).GetBytes("K=héslo😀\nL=2\n")).ToArray();
        var (doc, path) = await ExposeEnvAsync(rig, ".env.local", bytes);
        try
        {
            var text = await rig.CallAsync(DocumentMethods.ReadText, ReadParams(doc, 2, bytes.Length));
            Assert.Equal("K=*******\nL=*\n", text.GetProperty("text").GetString());
            Assert.Equal(0, text.GetProperty("invalid").GetArrayLength());
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task ASearchSkipsMatchesThatTouchAValueSoItCannotBeProbed()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        var bytes = Utf8("KEY=hunter2\nOTHER=hunter2\n# KEY note\n");
        var (doc, path) = await ExposeEnvAsync(rig, ".env", bytes);
        try
        {
            var probe = await rig.CallAsync(DocumentMethods.Find, $"{{\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"text\":\"hunter2\"}}");
            Assert.Equal(0, probe.GetProperty("matches").GetArrayLength());
            var across = await rig.CallAsync(DocumentMethods.Find, $"{{\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"text\":\"=hun\"}}");
            Assert.Equal(0, across.GetProperty("matches").GetArrayLength());
            var key = await rig.CallAsync(DocumentMethods.Find, $"{{\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"text\":\"KEY\"}}");
            Assert.Equal([0L, 28L], key.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("start").GetInt64()));
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task AnEditThatTouchesAHiddenValueIsRefusedBecauseThePluginOnlyReadStars()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"));
        var bytes = Utf8("KEY=hunter2\nB=2\n");
        var (doc, path) = await ExposeEnvAsync(rig, ".env", bytes);
        try
        {
            var rev = doc.Revision;
            await rig.ErrorAsync(DocumentMethods.EditApply, $"{{\"document\":\"{doc.Id}\",\"revision\":{rev},\"batchId\":\"b1\",\"edits\":[{{\"start\":4,\"end\":11,\"text\":\"*******\"}}]}}", DocumentErrorCodes.PermissionDenied);
            await rig.ErrorAsync(DocumentMethods.EditApply, $"{{\"document\":\"{doc.Id}\",\"revision\":{rev},\"batchId\":\"b2\",\"edits\":[{{\"start\":6,\"end\":6,\"text\":\"x\"}}]}}", DocumentErrorCodes.PermissionDenied);
            await rig.ErrorAsync(DocumentMethods.EditPropose, $"{{\"document\":\"{doc.Id}\",\"revision\":{rev},\"edits\":[{{\"start\":0,\"end\":12,\"text\":\"\"}}]}}", DocumentErrorCodes.PermissionDenied);
            Assert.Equal(bytes, rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength))));

            // the key, the end of the value and the next line are not secret
            var ok = await rig.CallAsync(DocumentMethods.EditApply, $"{{\"document\":\"{doc.Id}\",\"revision\":{rev},\"batchId\":\"b3\",\"edits\":[{{\"start\":0,\"end\":3,\"text\":\"APP\"}},{{\"start\":11,\"end\":11,\"text\":\"!\"}},{{\"start\":12,\"end\":13,\"text\":\"C\"}}]}}");
            Assert.True(ok.GetProperty("applied").GetBoolean());
            Assert.Equal(Utf8("APP=hunter2!\nC=2\n"),rig.Owner.Run(() => doc.ReadBytes(new ByteRange(0, doc.ByteLength))));

            rig.Permissions.Grant("p", DocumentPermissions.SecretsReveal, GrantScope.OfDocument(doc.Id));
            var allowed = await rig.CallAsync(DocumentMethods.EditApply, $"{{\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"batchId\":\"b4\",\"edits\":[{{\"start\":4,\"end\":11,\"text\":\"new\"}}]}}");
            Assert.True(allowed.GetProperty("applied").GetBoolean());
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }

    [Fact]
    public async Task AnEnvFileLargerThanTheScanLimitIsHiddenWhole()
    {
        await using var rig = await Rig.CreateAsync(Utf8("x"), o => o with { MaxSecretScanBytes = 8 });
        var (doc, path) = await ExposeEnvAsync(rig, ".env", Utf8("# comment\nK=value\n"));
        try
        {
            var text = await rig.CallAsync(DocumentMethods.ReadText, ReadParams(doc, 0, 18));
            Assert.Equal(new string('*', 18), text.GetProperty("text").GetString());
        }
        finally { rig.Owner.Run(() => { rig.Api.Withdraw(doc); doc.Dispose(); return 0; }); Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
}
