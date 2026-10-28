# NksHub.Textida.Plugin.Sdk

C# SDK for plugins of the Textida editor. A plugin is a separate NativeAOT program that talks to the editor in line-framed JSON-RPC over standard input and output.

The package holds:

- `PluginBase` and `PluginRunner` - the handshake, commands, events, ping and shutdown of a plugin process.
- `DocumentClient` - reads and edits the document a command started on, bound to its revision (a stale revision is a `DocumentErrorCodes.RevisionConflict`).
- `PluginTask`, `EditorHttpHandler`, `TypedRpc` - progress and cancellation, HTTP through the network policy of the editor, typed calls with source-generated JSON.
- The method names, error codes and permission names of the plugin API (`NksHub.Textida.PluginHost.*` namespaces) and `PluginApi.Version`.
- The analyzer `NKSPLG001`, which fails the build where the code uses reflection, and the build properties that turn trimming and AOT warnings into errors.

`NksHub.Textida.Rpc.dll` ships inside the package as the connection layer.

## Start

```
dotnet new install NksHub.Textida.Plugin.Templates
dotnet new nkspad-plugin -n MyPlugin
```

The template creates a plugin, its `nkspad-plugin.json` and a test project that replays editor events with `NksHub.Textida.Plugin.TestHost`. Check a built plugin folder with `textida-plugin-validate <folder>`.

## Versions

The plugin API version is `PluginApi.Version`. A manifest states the range it works with:

```json
"api": { "min": 1, "max": 1 }
```

The API is 0.x: the public surface is the list in `PublicApi/*.txt` of the repository, and a test fails when it changes.
