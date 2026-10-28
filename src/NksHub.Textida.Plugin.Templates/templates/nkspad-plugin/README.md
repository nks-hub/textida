# NkspadPlugin

A plugin for the Textida editor, written in C# on the package `NksHub.Textida.Plugin.Sdk`. The editor runs it as a separate native program and talks to it in JSON-RPC.

- `NkspadPlugin/` - the plugin. `ExamplePlugin` has one command that writes the selection in capital letters; `nkspad-plugin.json` is the manifest (id, plugin API range, permissions, commands).
- `NkspadPlugin.Tests/` - tests on `NksHub.Textida.Plugin.TestHost`, which runs the real document, UI, network and secret APIs of the editor over documents in memory, and replays a recorded session (`recordings/shout.json`) against the plugin.

## Build and test

```
dotnet test
```

## Publish

The editor starts a native program, so publish with NativeAOT on the system you publish for (a Windows machine for `win-x64`, a Mac of that kind for `osx-arm64` and `osx-x64`):

```
dotnet publish NkspadPlugin -c Release -r win-x64
```

Set `main.executable` in `nkspad-plugin.json` to the program in the publish folder (`./NkspadPlugin.exe` on Windows, `./NkspadPlugin` on macOS).

## Check

```
dotnet tool install -g NksHub.Textida.Plugin.Validate
textida-plugin-validate NkspadPlugin/bin/Release/net10.0/win-x64/publish --run --scenario NkspadPlugin.Tests/recordings/shout.json --source NkspadPlugin
```

The validator reads the manifest with the rules of the editor, checks the range of the plugin API, compares the permissions the manifest declares with the methods the plugin calls, checks the size of the folder and that the program is native, starts the plugin and replays the session, and runs the analyzer that bans reflection over the sources.
