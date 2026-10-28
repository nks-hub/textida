# NksHub.Textida.Plugin.Validate

`textida-plugin-validate` checks a plugin folder of the Textida editor before anybody installs it.

```
dotnet tool install -g NksHub.Textida.Plugin.Validate
textida-plugin-validate <plugin folder> [--run] [--scenario <recording.json>] [--source <sources>] [--host-api <n>]
textida-plugin-validate --schema
```

| Check | What it does |
| --- | --- |
| `manifest`, `api` | Reads `nkspad-plugin.json` with every rule the editor applies when it loads one (unknown members, ids, commands, grammars, the program and its folder). The range `api.min` to `api.max` must be valid and contain the plugin API version of the SDK (`--host-api` names another). |
| `permissions` | Compares the permissions the manifest declares with the methods of the API the plugin's files refer to: a method without its declared permission is an error, a declared permission no method needs is a warning. A scan of the names in the program and the scripts, not an analysis. |
| `size` | The folder against the bounds of the editor for an installed plugin (files and bytes). |
| `aot` | The program must be native code: a managed assembly or a program with a runtime configuration beside it is a warning. With `--source`, the project must bring the analyzer `NKSPLG001` and not switch the trimming warnings off, and the analyzer runs over the C# sources. |
| `handshake`, `rpc` | With `--run`: starts the plugin through the plugin host of the editor, checks the handshake, and that a method the plugin does not have is answered with method-not-found. |
| `scenario` | With `--run --scenario`: replays a recorded session (see `NksHub.Textida.Plugin.TestHost`) and compares the permissions the plugin really used with the manifest. |

`--schema` prints the JSON Schema of the manifest. The exit code is 0 without errors, 1 with one, 2 for a wrong command line. The validator does not audit a binary for anything that is not a rule of the plugin API.
