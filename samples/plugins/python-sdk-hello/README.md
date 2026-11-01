# python-sdk-hello

A Textida plugin in Python on the SDK in `sdk/python`. Its command writes the selections, or the whole document without one, in capital letters as one undo step. It needs Python 3.11 or newer.

To try it: copy the folder `sdk/python/textida_plugin` next to `main.py`, replace `/path/to/python` in `nkspad-plugin.json` with the full path of the interpreter, and check it:

```
textida-plugin-validate . --run
```

`--run` starts the plugin and checks the handshake. The types of the API (`textida_plugin/api.py`) are generated from the types of the C# SDK.
