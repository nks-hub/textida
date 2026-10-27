# Python hello plugin

A plugin written as a plain Python script. The editor starts it as a child process and talks to it
with one JSON-RPC 2.0 message per line on stdin and stdout.

Edit `main.executable` in `nkspad-plugin.json` and put the full path of your interpreter there, for example
`C:\\Python313\\python.exe` or `/usr/bin/python3`. The editor does not search `PATH`, so what runs is always the file you named.
`args` are passed to the interpreter and `main.py` is found relative to the plugin folder.

The plugin starts at the first run of `example.python-hello.greet` (activation event `onCommand:...`, derived from
the command list). It answers `initialize`, `$/resync`, `$/ping`, `shutdown` and `command/execute`, and leaves on `exit`
or when stdin closes. Anything written to stderr is kept as the plugin log.

A plugin that holds `document.read`, `document.edit.propose` or `document.edit.direct` also gets the `document` id of the active tab
and its `revision` in `command/execute`; it can read that document, propose edits (`edit/propose`) or apply them (`edit/apply`) until it answers.

`PluginManifestTests.APythonPluginRunsThroughTheHost` copies this folder, fills in the interpreter found on the test machine
and runs the command through the plugin host.
