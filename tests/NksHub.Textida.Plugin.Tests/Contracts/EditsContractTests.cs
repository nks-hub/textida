using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests.Contracts;

/// <summary>P8b.2.2, interface <b>edits</b> (edit/apply, edit/propose, secrets files in edits): a batch is one revision, bound to the revision the plugin read.</summary>
public class EditsContractTests
{
    private static readonly string[] AllEdits = [DocumentPermissions.Read, DocumentPermissions.EditDirect, DocumentPermissions.EditPropose];

    [Fact]
    public async Task Positive_a_batch_is_one_revision_and_a_repeated_batch_id_is_not_applied_twice()
    {
        await using var editor = await T.RawEditor(AllEdits);
        var document = editor.OpenDocument("a.txt", "hello world\n");
        var client = new DocumentClient(editor.Raw!.Connection, document.Id, document.Revision);
        var before = document.Revision;

        await client.ApplyAsync([new RangeEdit(0, 5, "HELLO"), new RangeEdit(6, 11, "WORLD")], "batch-1");

        Assert.Equal(("HELLO WORLD\n", before + 1), (document.Text, document.Revision));
        var repeat = await editor.Raw.RequestAsync(DocumentMethods.EditApply, $"{{\"document\":\"{document.Id}\",\"revision\":{before},\"batchId\":\"batch-1\",\"edits\":[{{\"start\":0,\"end\":5,\"text\":\"HELLO\"}},{{\"start\":6,\"end\":11,\"text\":\"WORLD\"}}]}}");
        Assert.True(repeat.GetProperty("duplicate").GetBoolean());
        Assert.Equal(before + 1, document.Revision);
    }

    [Fact]
    public async Task Positive_a_proposal_is_shown_to_the_user_and_only_what_the_user_accepts_changes_the_document()
    {
        await using var editor = await T.RawEditor(AllEdits);
        var document = editor.OpenDocument("a.txt", "abc");
        var client = new DocumentClient(editor.Raw!.Connection, document.Id, document.Revision);

        editor.Proposals.Policy = ProposalPolicy.Reject;
        await client.ProposeAsync("Shout", [new RangeEdit(0, 3, "ABC")]);
        Assert.Equal("abc", document.Text);

        editor.Proposals.Policy = ProposalPolicy.Accept;
        var accepted = await editor.Raw.RequestAsync(DocumentMethods.EditPropose, T.Doc(document, "\"title\":\"Shout\",\"edits\":[{\"start\":0,\"end\":3,\"text\":\"ABC\"}]"));
        Assert.Equal(("applied", 1, 1), (accepted.GetProperty("decision").GetString(), accepted.GetProperty("accepted").GetInt32(), accepted.GetProperty("total").GetInt32()));
        Assert.Equal("ABC", document.Text);
        Assert.Equal(["Shout", "Shout"], editor.Proposals.Items.Select(p => p.Title));
    }

    [Fact]
    public async Task Revision_conflict_edits_made_against_an_old_revision_change_nothing_and_a_proposal_is_not_shown()
    {
        await using var editor = await T.RawEditor(AllEdits);
        var document = editor.OpenDocument("a.txt", "abc");
        var stale = new DocumentClient(editor.Raw!.Connection, document.Id, document.Revision);
        document.Type(3, "d");

        var apply = await T.ErrorOf(() => stale.ApplyAsync([new RangeEdit(0, 1, "X")]));
        var propose = await T.ErrorOf(() => stale.ProposeAsync("X", [new RangeEdit(0, 1, "X")]));

        Assert.Equal((DocumentErrorCodes.RevisionConflict, DocumentErrorCodes.RevisionConflict), (apply.Code, propose.Code));
        Assert.Equal("Stale", T.Data(apply, "reason"));
        Assert.Equal("abcd", document.Text);
        Assert.Empty(editor.Proposals.Items);
    }

    [Fact]
    public async Task Revision_conflict_a_batch_with_edits_that_overlap_or_leave_the_document_is_refused_as_a_whole()
    {
        await using var editor = await T.RawEditor(AllEdits);
        var document = editor.OpenDocument("a.txt", "abcdef");
        var client = new DocumentClient(editor.Raw!.Connection, document.Id, document.Revision);

        Assert.Equal(DocumentErrorCodes.EditsOverlap, (await T.ErrorOf(() => client.ApplyAsync([new RangeEdit(0, 3, "x"), new RangeEdit(2, 4, "y")]))).Code);
        Assert.Equal(DocumentErrorCodes.InvalidRange, (await T.ErrorOf(() => client.ApplyAsync([new RangeEdit(0, 1, "x"), new RangeEdit(5, 99, "y")]))).Code);
        Assert.Equal(("abcdef", client.Revision), (document.Text, document.Revision));
    }

    [Fact]
    public async Task Permission_direct_edits_and_proposals_are_two_permissions_and_the_error_names_the_missing_one()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var document = editor.OpenDocument("a.txt", "abc");
        var client = new DocumentClient(editor.Raw!.Connection, document.Id, document.Revision);

        var apply = await T.ErrorOf(() => client.ApplyAsync([new RangeEdit(0, 1, "X")]));
        var propose = await T.ErrorOf(() => client.ProposeAsync("X", [new RangeEdit(0, 1, "X")]));

        Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.EditDirect, "NotGranted"), (apply.Code, T.Data(apply, "permission"), T.Data(apply, "reason")));
        Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.EditPropose), (propose.Code, T.Data(propose, "permission")));
        Assert.Equal("abc", document.Text);

        editor.Permissions.Grant(editor.PluginId, DocumentPermissions.EditPropose, NksHub.Textida.PluginHost.Permissions.GrantScope.All);
        await client.ProposeAsync("X", [new RangeEdit(0, 1, "X")]);
        Assert.Equal("Xbc", document.Text);
    }

    [Fact]
    public async Task Permission_an_edit_that_touches_a_value_of_a_secrets_file_is_refused_until_the_user_allows_the_plugin_to_see_it()
    {
        await using var editor = await T.RawEditor(AllEdits);
        var env = editor.OpenFile(".env", "KEY=hunter2\nB=2\n");
        var client = new DocumentClient(editor.Raw!.Connection, env.Id, env.Revision);

        var refused = await T.ErrorOf(() => client.ApplyAsync([new RangeEdit(4, 11, "*******")]));
        Assert.Equal((DocumentErrorCodes.PermissionDenied, DocumentPermissions.SecretsReveal, "Secret"), (refused.Code, T.Data(refused, "permission"), T.Data(refused, "reason")));
        Assert.Equal("KEY=hunter2\nB=2\n", env.Text);

        await client.ApplyAsync([new RangeEdit(0, 3, "APP")]); // the key is not secret
        Assert.Equal("APP=hunter2\nB=2\n", env.Text);
    }
}
