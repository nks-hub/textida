using System.Collections.Concurrent;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P3b.4.1: grants bound to documents, revocation at run time, no delegated writes, per-plugin secrets and a call log without content.</summary>
public partial class DocumentApiTests
{
    sealed class MemorySecrets : ISecretBackend
    {
        readonly ConcurrentDictionary<(string, string), string> _values = new();
        public string? Get(string pluginId, string name) => _values.TryGetValue((pluginId, name), out var v) ? v : null;
        public void Set(string pluginId, string name, string value) => _values[(pluginId, name)] = value;
        public bool Delete(string pluginId, string name) => _values.TryRemove((pluginId, name), out _);
    }

    sealed class BlockingProposals : IEditProposals
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public volatile bool Cancelled;

        public async ValueTask<string> SubmitAsync(EditProposal proposal, CancellationToken cancellation)
        {
            Entered.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            return "never";
        }
    }

    /// <summary>The editor API of a test: documents, secrets and command/execute, routed by method name.</summary>
    static Func<Func<PluginHost.PluginHost>, DocumentApi, PluginPermissions, PluginCallLog, PluginApiCallback> Route(MemorySecrets? secrets = null) =>
        (host, api, permissions, log) =>
        {
            var store = new PluginSecrets(secrets ?? new MemorySecrets(), log);
            var gate = new PluginCommandGate(permissions, log, async (target, command, args, ct) => await host().CallAsync(target, "callEditor", args, ct: ct));
            return (plugin, method, p, ct) => method switch
            {
                PluginCommandGate.Execute => gate.HandleAsync(plugin, method, p, ct),
                PluginSecrets.Get or PluginSecrets.Set or PluginSecrets.Delete => store.HandleAsync(plugin, method, p, ct),
                _ => api.HandleAsync(plugin, method, p, ct),
            };
        };

    static void AssertDenied(JsonElement r, string reason)
    {
        Assert.Equal(DocumentErrorCodes.PermissionDenied, r.GetProperty("error").GetInt32());
        Assert.Equal(reason, r.GetProperty("data").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task APluginWithoutThePermissionGetsAnErrorNotAnEmptyResult()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"));
        var rev = rig.Revision;
        rig.Permissions.RevokeAll("p");

        AssertDenied(await rig.ErrorAsync(DocumentMethods.List, "{}", DocumentErrorCodes.PermissionDenied), "NotGranted");
        AssertDenied(await rig.ErrorAsync(DocumentMethods.ReadBytes, rig.D($"\"revision\":{rev},\"start\":0,\"end\":1"), DocumentErrorCodes.PermissionDenied), "NotGranted");
        AssertDenied(await rig.ErrorAsync(DocumentMethods.EditPropose, rig.D($"\"revision\":{rev},\"edits\":[]"), DocumentErrorCodes.PermissionDenied), "NotGranted");
        AssertDenied(await rig.ErrorAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"b\",\"edits\":[]"), DocumentErrorCodes.PermissionDenied), "NotGranted");
        Assert.Equal(rev, rig.Revision);
    }

    [Fact]
    public async Task AGrantBelongsToADocumentNotToTheActiveTab()
    {
        await using var rig = await Rig.CreateAsync(Utf8("first"));
        using var other = Document.FromBytes(Utf8("second"), PagedBackendFactory.Instance);
        rig.Owner.Run(() => { rig.Api.Expose(other); return 0; });
        rig.Permissions.RevokeAll("p");
        rig.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.OfDocument(rig.Doc.Id));

        var list = await rig.CallAsync(DocumentMethods.List, "{}");
        Assert.Equal([rig.Doc.Id.ToString()], list.EnumerateArray().Select(x => x.GetProperty("id").GetString()));
        Assert.Equal("first", (await rig.CallAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{rig.Revision},\"start\":0,\"end\":5"))).GetProperty("text").GetString());
        var rev = rig.Owner.Run(() => other.Revision);
        AssertDenied(await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"document\":\"{other.Id}\",\"revision\":{rev},\"start\":0,\"end\":1}}", DocumentErrorCodes.PermissionDenied), "NotGranted");

        // A snapshot stays tied to the document it was taken from.
        var snap = (await rig.CallAsync(DocumentMethods.SnapshotCreate, rig.D())).GetProperty("snapshot").GetString();
        rig.Permissions.Revoke("p", DocumentPermissions.Read, GrantScope.OfDocument(rig.Doc.Id));
        rig.Permissions.Grant("p", DocumentPermissions.Read, GrantScope.OfDocument(other.Id));
        AssertDenied(await rig.ErrorAsync(DocumentMethods.ReadBytes, $"{{\"snapshot\":\"{snap}\",\"start\":0,\"end\":1}}", DocumentErrorCodes.PermissionDenied), "NotGranted");
    }

    [Fact]
    public async Task RevokingAGrantCancelsThePendingRequestAndRefusesTheNextOne()
    {
        var proposals = new BlockingProposals();
        await using var rig = await Rig.CreateAsync(Utf8("hello"), tune: o => o with { Proposals = proposals });
        var rev = rig.Revision;
        var pending = rig.CallAsync(DocumentMethods.EditPropose, rig.D($"\"revision\":{rev},\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"J\"}}]"));
        await proposals.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        rig.Permissions.Revoke("p", DocumentPermissions.EditPropose, GrantScope.All);

        AssertDenied(await pending.WaitAsync(TimeSpan.FromSeconds(30)), "Revoked");
        await T.Until(() => proposals.Cancelled);
        AssertDenied(await rig.ErrorAsync(DocumentMethods.EditPropose, rig.D($"\"revision\":{rev},\"edits\":[]"), DocumentErrorCodes.PermissionDenied), "NotGranted");
        Assert.Equal(rev, rig.Revision);
    }

    [Fact]
    public async Task RevokingReadStopsTheEventsOfASubscription()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"));
        await rig.CallAsync(DocumentMethods.Subscribe, rig.D());
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("a")));
        await rig.WaitEventsAsync(e => e.Count == 1);

        rig.Permissions.Revoke("p", DocumentPermissions.Read, GrantScope.All);
        rig.Owner.Run(() => rig.Doc.Insert(0, Utf8("b")));
        await Task.Delay(500);
        Assert.Single(await rig.EventsAsync());
    }

    [Fact]
    public async Task APermittedCommandCannotPassAWriteOnToAnotherPlugin()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"), plugins: ["a", "b"], route: Route());
        var rev = rig.Revision;
        rig.Permissions.Revoke("a", DocumentPermissions.EditDirect, GrantScope.All);
        var write = $"{{\"method\":\"{DocumentMethods.EditApply}\",\"params\":{rig.D($"\"revision\":{rev},\"batchId\":\"x\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"J\"}}]")}}}";
        var execute = $"{{\"plugin\":\"b\",\"command\":\"format\",\"args\":{write}}}";

        // Without the command permission the call itself is refused.
        AssertDenied(await rig.ErrorAsync(PluginCommandGate.Execute, execute, DocumentErrorCodes.PermissionDenied, "a"), "NotGranted");

        // With it, plugin b runs the command but its write is refused: b holds the write permission, a's request does not lend it out.
        rig.Permissions.Grant("a", PluginPermissionNames.CommandExecute, GrantScope.OfPlugin("b"));
        var result = await rig.CallAsync(PluginCommandGate.Execute, execute, "a");
        AssertDenied(result, "Delegated");
        Assert.Equal(rev, rig.Revision);

        // a plugin's own, user driven write is allowed again once the command ended.
        var own = await rig.CallAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"y\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"J\"}}]"), "b");
        Assert.True(own.GetProperty("applied").GetBoolean());
    }

    [Fact]
    public async Task SecretsBelongToOnePluginAndNeverReachTheCallLog()
    {
        await using var rig = await Rig.CreateAsync(Utf8("hello"), plugins: ["a", "b"], route: Route());
        var rev = rig.Revision;
        await rig.CallAsync(PluginSecrets.Set, "{\"name\":\"api\",\"value\":\"TOKEN-123\"}", "a");
        Assert.Equal("TOKEN-123", (await rig.CallAsync(PluginSecrets.Get, "{\"name\":\"api\"}", "a")).GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, (await rig.CallAsync(PluginSecrets.Get, "{\"name\":\"api\"}", "b")).GetProperty("value").ValueKind);
        await rig.CallAsync(DocumentMethods.EditApply, rig.D($"\"revision\":{rev},\"batchId\":\"z\",\"edits\":[{{\"start\":0,\"end\":1,\"text\":\"TOPSECRET-TEXT\"}}]"), "a");
        rig.Permissions.RevokeAll("b");
        await rig.CallAsync(DocumentMethods.ReadText, rig.D($"\"revision\":{rev + 1},\"start\":0,\"end\":1"), "b");

        var log = string.Join("\n", rig.CallLog.Entries().Select(e => e.ToString()));
        Assert.DoesNotContain("TOKEN-123", log);
        Assert.DoesNotContain("TOPSECRET-TEXT", log);
        var entries = rig.CallLog.Entries();
        Assert.Contains(entries, e => e.PluginId == "a" && e.Method == PluginSecrets.Set && e.Outcome == CallOutcome.Ok);
        Assert.Contains(entries, e => e.PluginId == "a" && e.Method == DocumentMethods.EditApply && e.DocumentId == rig.Doc.Id && e.Outcome == CallOutcome.Ok);
        Assert.Contains(entries, e => e.PluginId == "b" && e.Method == DocumentMethods.ReadText && e.Outcome == CallOutcome.Denied && e.ErrorCode == DocumentErrorCodes.PermissionDenied);
    }
}
