using NkspadPlugin;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NkspadPlugin.Tests;

public class PluginTests
{
    // what the manifest asks for; the editor holds the user's decision, the test editor holds exactly this
    private static readonly string[] Grants = [DocumentPermissions.Read, DocumentPermissions.EditDirect];

    [Fact]
    public async Task The_command_writes_the_selection_in_capital_letters_as_one_edit()
    {
        await using var editor = await TestEditor.StartAsync(new ExamplePlugin(), new TestEditorOptions { Grants = Grants });
        var document = editor.OpenDocument("a.txt", "hello world\n");
        document.Select((0, 5));

        var reply = await editor.ExecuteCommandAsync(ExamplePlugin.ShoutCommand, document);

        Assert.Equal("applied", reply.GetProperty("status").GetString());
        Assert.Equal("HELLO world\n", document.Text);
        Assert.Single(editor.Calls, call => call.Method == DocumentMethods.EditApply);
    }

    [Fact]
    public async Task A_document_that_changed_after_the_command_started_is_a_revision_conflict_and_stays_as_it_is()
    {
        await using var editor = await TestEditor.StartAsync(new ExamplePlugin(), new TestEditorOptions { Grants = Grants });
        var document = editor.OpenDocument("a.txt", "hello\n");
        var seen = document.Revision;
        document.Type(0, "> ");

        var error = await Assert.ThrowsAsync<RpcException>(() => editor.ExecuteCommandAsync(ExamplePlugin.ShoutCommand, document, revision: seen));

        Assert.Equal(DocumentErrorCodes.RevisionConflict, error.Code);
        Assert.Equal("> hello\n", document.Text);
    }

    [Fact]
    public async Task The_command_needs_the_permissions_the_manifest_asks_for()
    {
        await using var editor = await TestEditor.StartAsync(new ExamplePlugin(), new TestEditorOptions { Grants = [DocumentPermissions.Read] });
        var document = editor.OpenDocument("a.txt", "hello\n");

        var error = await Assert.ThrowsAsync<RpcException>(() => editor.ExecuteCommandAsync(ExamplePlugin.ShoutCommand, document));

        Assert.Equal(DocumentErrorCodes.PermissionDenied, error.Code);
        Assert.Equal("hello\n", document.Text);
    }

    [Fact]
    public async Task A_recorded_session_replays_against_the_plugin()
    {
        await using var editor = await TestEditor.StartAsync(new ExamplePlugin(), new TestEditorOptions { Grants = Grants });
        var recording = Recording.Load(Path.Combine(AppContext.BaseDirectory, "recordings", "shout.json"));

        var result = await editor.ReplayAsync(recording);

        Assert.True(result.Passed, string.Join("\n", result.Failures));
    }
}
