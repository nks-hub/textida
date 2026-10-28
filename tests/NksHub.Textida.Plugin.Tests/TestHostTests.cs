using System.Text.Json;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>P8b.2.1: the test host runs a plugin against the real APIs of the editor without a window, and replays recorded editor events.</summary>
public class TestHostTests
{
    private static readonly string[] EditGrants = [DocumentPermissions.Read, DocumentPermissions.EditDirect, DocumentPermissions.EditPropose];

    [Fact]
    public async Task A_command_edits_the_selection_through_the_real_document_api()
    {
        await using var editor = await TestEditor.StartAsync(new UpperPlugin(), new TestEditorOptions { Grants = EditGrants });
        var document = editor.OpenDocument("a.txt", "hello world\n");
        document.Select((6, 11));

        var reply = await editor.ExecuteCommandAsync("upper.run", document);

        Assert.Equal("applied", reply.GetProperty("status").GetString());
        Assert.Equal("hello WORLD\n", document.Text);
        Assert.Equal([DocumentMethods.Selections, DocumentMethods.ReadText, DocumentMethods.EditApply], editor.Calls.Select(c => c.Method));
        Assert.All(editor.Calls, c => Assert.Null(c.ErrorCode));
    }

    [Fact]
    public async Task A_command_started_on_an_old_revision_ends_with_a_revision_conflict_and_changes_nothing()
    {
        await using var editor = await TestEditor.StartAsync(new UpperPlugin(), new TestEditorOptions { Grants = EditGrants });
        var document = editor.OpenDocument("a.txt", "hello\n");
        var seen = document.Revision;
        document.Type(0, ">");
        document.Select((1, 6));

        var error = await Assert.ThrowsAsync<Rpc.RpcException>(() => editor.ExecuteCommandAsync("upper.run", document, revision: seen));

        Assert.Equal(DocumentErrorCodes.RevisionConflict, error.Code);
        Assert.Equal(">hello\n", document.Text);
        Assert.DoesNotContain(editor.Calls, c => c.Method == DocumentMethods.EditApply);
    }

    [Fact]
    public async Task A_plugin_without_the_permission_is_refused_and_the_test_sees_which_one_it_needed()
    {
        await using var editor = await TestEditor.StartAsync(new UpperPlugin(), new TestEditorOptions { Grants = [DocumentPermissions.Read] });
        var document = editor.OpenDocument("a.txt", "hello\n");
        document.Select((0, 5));

        var error = await Assert.ThrowsAsync<Rpc.RpcException>(() => editor.ExecuteCommandAsync("upper.run", document));

        Assert.Equal(DocumentErrorCodes.PermissionDenied, error.Code);
        Assert.Equal("hello\n", document.Text);
        Assert.Equal([DocumentPermissions.EditDirect, DocumentPermissions.Read], editor.UsedPermissions());
        Assert.Equal(DocumentErrorCodes.PermissionDenied, editor.Calls.Last().ErrorCode);
    }

    [Fact]
    public async Task A_proposal_waits_for_the_decision_of_the_user_that_the_test_sets()
    {
        await using var editor = await TestEditor.StartAsync(new UpperPlugin(), new TestEditorOptions { Grants = EditGrants });
        var document = editor.OpenDocument("a.txt", "quiet\n");

        editor.Proposals.Policy = ProposalPolicy.Reject;
        await editor.ExecuteCommandAsync("upper.propose", document);
        Assert.Equal("quiet\n", document.Text);

        editor.Proposals.Policy = ProposalPolicy.Accept;
        await editor.ExecuteCommandAsync("upper.propose", document);
        Assert.Equal("QUIET\n", document.Text);
        Assert.Equal(["Uppercase", "Uppercase"], editor.Proposals.Items.Select(p => p.Title));
    }

    [Fact]
    public async Task A_notice_of_the_plugin_reaches_the_ui_record()
    {
        await using var editor = await TestEditor.StartAsync(new UpperPlugin());

        await editor.ExecuteCommandAsync("upper.greet", argumentsJson: "{\"who\":\"Textida\"}");

        Assert.Equal([(NoticeLevel.Info, "Hello, Textida!")], editor.Ui.Notices);
    }

    [Fact]
    public async Task A_recorded_session_replays_against_the_plugin_and_its_expectations_hold()
    {
        var plugin = new UpperPlugin();
        await using var editor = await TestEditor.StartAsync(plugin, new TestEditorOptions { Grants = EditGrants });
        var recording = Recording.Parse("""
            {
              "documents": [{ "name": "a.txt", "text": "one two\n", "selections": [[0, 3]] }],
              "steps": [
                { "kind": "event", "name": "settings.changed", "payload": { "indent": 2 } },
                { "kind": "command", "name": "upper.run", "document": "a.txt", "status": "applied" },
                { "kind": "expectText", "document": "a.txt", "text": "ONE two\n" },
                { "kind": "edit", "document": "a.txt", "offset": 8, "text": "three\n" },
                { "kind": "select", "document": "a.txt", "ranges": [[4, 7], [8, 13]] },
                { "kind": "command", "name": "upper.run", "document": "a.txt", "status": "applied" },
                { "kind": "expectText", "document": "a.txt", "text": "ONE TWO\nTHREE\n" },
                { "kind": "expectCalled", "method": "edit/apply", "count": 2 },
                { "kind": "expectNotCalled", "method": "edit/propose" },
                { "kind": "command", "name": "upper.greet", "status": "greeted" },
                { "kind": "expectNotice", "text": "Hello, world" }
              ]
            }
            """);

        var result = await editor.ReplayAsync(recording);

        Assert.True(result.Passed, string.Join("\n", result.Failures));
        Assert.Equal(11, result.Steps.Count);
        await T.Until(() => plugin.Events.Contains("settings.changed"));
    }

    [Fact]
    public async Task A_replay_names_every_expectation_that_does_not_hold_and_goes_on()
    {
        await using var editor = await TestEditor.StartAsync(new UpperPlugin(), new TestEditorOptions { Grants = EditGrants });
        var recording = Recording.Parse("""
            {
              "documents": [{ "name": "a.txt", "text": "abc", "selections": [[0, 3]] }],
              "steps": [
                { "kind": "command", "name": "upper.run", "document": "a.txt", "status": "unchanged" },
                { "kind": "expectText", "document": "a.txt", "text": "ABC" },
                { "kind": "command", "name": "upper.missing", "document": "a.txt" },
                { "kind": "command", "name": "upper.missing", "document": "a.txt", "expectError": -32602 },
                { "kind": "expectCalled", "method": "edit/propose" },
                { "kind": "wobble" }
              ]
            }
            """);

        var result = await editor.ReplayAsync(recording, expectTimeout: TimeSpan.FromMilliseconds(200));

        Assert.False(result.Passed);
        Assert.Collection(result.Failures,
            f => Assert.Contains("step 0", f), // the plugin answered "applied" for the uppercase text
            f => Assert.Contains("step 2", f), // an unknown command ends with an error nobody expected
            f => Assert.Contains("step 4", f),
            f => Assert.Contains("step 5", f));
        Assert.Equal(6, result.Steps.Count);
    }

    [Fact]
    public void A_recording_with_an_unknown_member_is_refused_so_a_typo_cannot_turn_a_check_off()
    {
        Assert.Throws<JsonException>(() => Recording.Parse("""{ "steps": [{ "kind": "expectText", "documnet": "a" }] }"""));
    }

    [Fact]
    public async Task A_test_driven_plugin_hears_the_events_of_a_subscription()
    {
        await using var editor = await T.RawEditor(DocumentPermissions.Read);
        var document = editor.OpenDocument("a.txt", "abc");
        await editor.Raw!.RequestAsync(DocumentMethods.Subscribe, $"{{\"document\":\"{document.Id}\"}}");

        document.Type(3, "d");

        var changed = await editor.Raw.WaitForEventAsync(DocumentEvents.Changed);
        using var payload = JsonDocument.Parse(changed.PayloadJson);
        Assert.Equal(document.Id, payload.RootElement.GetProperty("document").GetString());
    }

    [Fact]
    public async Task A_plugin_program_runs_through_the_plugin_host_of_the_editor()
    {
        // samples/csharp-hello, built as a managed program; the handshake, ping and shutdown are those of the editor
        await using var editor = await TestEditor.StartProcessAsync(T.DotnetHost, [T.SampleDll], new TestEditorOptions { PluginId = "example.csharp-hello" });

        var answer = await editor.RequestPluginAsync("hello/greet", "{\"name\":\"Textida\"}");

        Assert.Equal("Hello, Textida!", answer.GetProperty("greeting").GetString());
        var error = await Assert.ThrowsAsync<Rpc.RpcException>(() => editor.RequestPluginAsync("nothing"));
        Assert.Equal(Rpc.RpcErrorCodes.MethodNotFound, error.Code);
        Assert.Empty(editor.UsedPermissions());
    }
}
