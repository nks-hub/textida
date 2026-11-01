# js-hello

A Textida plugin in JavaScript on the SDK in `sdk/js`. Its command writes the selections, or the whole document without one, in capital letters as one undo step.

To try it: copy `sdk/js` into `node_modules/textida-plugin` next to `main.js`, replace `/path/to/node` in `nkspad-plugin.json` with the full path of Node.js 18 or newer, and check it:

```
textida-plugin-validate . --run
```

`--run` starts the plugin and checks the handshake. The types of the API (`sdk/js/api.d.ts`) are generated from the types of the C# SDK; with them `// @ts-check` or a TypeScript file sees the shape of every call.
