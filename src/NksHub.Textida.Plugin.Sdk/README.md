# NksHub.Textida.Plugin.Sdk

C# SDK for plugins of the Textida editor. A plugin is a separate NativeAOT program that talks to the editor in line-framed JSON-RPC over standard input and output.

The package holds:

- `PluginBase` and `PluginRunner` - the handshake, commands, events, ping and shutdown of a plugin process.
- `DocumentClient` - reads and edits the document a command started on, bound to its revision (a stale revision is a `DocumentErrorCodes.RevisionConflict`).
- `PluginTask`, `EditorHttpHandler`, `TypedRpc` - progress and cancellation, HTTP through the network policy of the editor, typed calls with source-generated JSON.
- The method names, error codes and permission names of the plugin API (`NksHub.Textida.PluginHost.*` namespaces), `PluginMethodPermissions` (which permission a method needs) and `PluginApi.Version`.
- The analyzer `NKSPLG001`, which fails the build where the code uses reflection, and the build properties that turn trimming and AOT warnings into errors.
- `schemas/nkspad-plugin.schema.json`, the JSON Schema of the manifest, and `ERRORS.md`, every error code with what to do about it.

`NksHub.Textida.Rpc.dll` ships inside the package as the connection layer; only the types listed in `PublicApi.txt` of the repository are part of the API.

## Start

```
dotnet new install NksHub.Textida.Plugin.Templates
dotnet new nkspad-plugin -n Acme.Formatter --pluginId acme.formatter
```

The template creates a plugin, its `nkspad-plugin.json` and a test project that replays editor events with `NksHub.Textida.Plugin.TestHost`. Check a built plugin folder with `textida-plugin-validate <folder>` (package `NksHub.Textida.Plugin.Validate`).

## A command

A command reads the document it was started on, shows a notice and answers with a status. The example is compiled and run against the current API by a test of the repository; it relies on the implicit usings of a project made by the template.

```csharp
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;

namespace Docs;

public sealed class CountPlugin : PluginBase
{
    public CountPlugin() : base("count", PluginApi.Version) => OnCommand("example.count.run", CountAsync);

    private async ValueTask<CommandResult> CountAsync(CommandContext command, CancellationToken cancellation)
    {
        var document = DocumentOf(command);
        var info = await document.InfoAsync(cancellation);
        var text = await document.ReadTextAsync(info.BomLength, info.ByteLength, cancellation);
        await NotifyAsync(NoticeLevel.Info, $"{text.Text.Length} characters", cancellation);
        return new CommandResult("counted");
    }
}
```

It needs the permission `document.read` in `permissions` of the manifest. A command that finds the document changed gets a `RpcException` with `DocumentErrorCodes.RevisionConflict`; the editor tells the user and nothing is written.

## Versions

The plugin API version is `PluginApi.Version`. A manifest states the range it works with, and the editor starts the plugin only when its own version is inside it:

```json
"api": { "min": 1, "max": 1 }
```

The API is 0.x. The public surface is the list in `PublicApi.txt` of the repository, and a test fails when the code and the list differ.
