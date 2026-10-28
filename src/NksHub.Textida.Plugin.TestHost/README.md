# NksHub.Textida.Plugin.TestHost

The Textida editor without a window, for tests of a plugin. It runs the real document, UI, network and secret APIs of the editor over documents in memory, so a plugin meets the same revisions, permissions and errors as in the editor. What the editor would draw or ask the user is recorded, and the test answers it.

The plugin in the example is `CountPlugin` of the README of `NksHub.Textida.Plugin.Sdk`; like that example, this one is built and run by a test of the repository.

```csharp
using Docs;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost.Documents;

namespace Docs.Tests;

public static class CountExample
{
    public static async Task<string> RunAsync()
    {
        await using var editor = await TestEditor.StartAsync(new CountPlugin(), new TestEditorOptions { Grants = [DocumentPermissions.Read] });
        var document = editor.OpenDocument("a.txt", "hello world\n");

        await editor.ExecuteCommandAsync("example.count.run", document);

        return editor.Ui.Notices.Single().Message;
    }
}
```

- `StartAsync(PluginBase)` runs the plugin in the test process, `StartProcessAsync(path)` starts a plugin program through the plugin host of the editor (handshake, pings, shutdown), `StartRawAsync()` lets the test play the plugin and send requests to the editor itself (`editor.Raw`).
- `Grants` are the permissions the user gave the plugin; `editor.Permissions` grants or revokes more while the test runs. `editor.UsedPermissions()` says what the plugin needed, so a test can compare it with the manifest.
- `editor.Proposals.Policy` is the decision of the user on a proposal, `editor.Ui` holds panels, notices and tasks and answers prompts, `editor.Network` answers `net/request` without a network, `editor.Secrets` is the credential store.
- `Recording` is a session written down as JSON (documents, user edits, events, commands, and the expectations `expectText`, `expectCalled`, `expectNotCalled`, `expectNotice`); `editor.ReplayAsync(recording)` replays it and names every expectation that did not hold. An unknown member of the JSON is an error.

The limits of the test editor are values for tests, not the limits of the editor.
