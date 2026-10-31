# Errors of the plugin API

Every request of a plugin to the editor ends with a result or with a JSON-RPC error: a `code`, a `message` and, for most codes below, a `data` object with the details named in the table. The SDK throws an error as `RpcException` (`Code`, `Message`, `DataJson`). The codes are constants: `RpcErrorCodes`, `DocumentErrorCodes` (the document API and the services of the editor) and `UiErrorCodes`. `PublicApi.txt` pins their values, and a test checks that every code is in this file.

The editor never answers a refused request with an empty result: a plugin that lacks a permission, or reads a revision that is gone, gets an error.

## Connection and protocol

| Code | Name | When | What to do |
| --- | --- | --- | --- |
| -32700 | ParseError | The message is not JSON. | Write exactly one JSON value per line; a library that wraps lines or writes a byte order mark breaks this. |
| -32600 | InvalidRequest | The message is JSON but not a request, or is a batch (batches are not supported). | Send one request or notification per message. |
| -32601 | MethodNotFound | The editor has no such method, or the plugin has none (an unknown command, an unknown method sent by the editor). | Check the name against `DocumentMethods`, `UiMethods` and `PluginServiceMethods`; a method the editor lacks means the editor is older than the SDK, so the `api` range of the manifest must not include it. A plugin answers a method it does not have with this code. |
| -32602 | InvalidParams | A parameter is missing, of the wrong type, out of range or breaks a rule (`data` is absent; the `message` names the parameter). | Fix the request. Repeating it unchanged fails again. |
| -32603 | InternalError | The handler failed in a way the caller cannot fix. | The request may or may not have had its effect: read the document again before going on. |
| -32800 | RequestCancelled | The request was cancelled by the peer. | Stop the work of that request; the token of the command handler is cancelled as well. |
| -32011 | MessageTooLarge | A message is larger than the limit of the connection. | Ask for smaller ranges (`document/readText` in chunks), send smaller batches. |
| -32012 | TooManyRequests | The peer already has as many requests of the plugin running as the limit of the connection allows. | Wait for answers before sending more. |
| -32013 | TooManyResults | A result holds more items than the limit allows. | Ask for a narrower range or use the `next`/`from` member of the answer to go on. |
| -32014 | SessionMemoryExceeded | The messages being processed hold more memory than the limit allows. | Send less at a time; the connection ends if the plugin keeps sending without reading. |
| -32015 | RequestTimeout | A handler gave up waiting for something it forwarded. | Try again later; the work may have been done. |

## Documents, edits, diff, virtual documents, sources, services

| Code | Name | When | What to do |
| --- | --- | --- | --- |
| -32020 | RevisionConflict | The revision named is not the current one (`data.reason`: `Stale` or `FutureRevision`, `data.currentRevision`); or the change log cannot map a position (`MapRanges`: `data.index` names the range). Also a `diff/stage` or a `virtual/update` with `baseRevision` into a document that moved on. | Nothing was written. Read the document again at `currentRevision`, compute the change again and send it. Never resend the old edit at the new revision without looking at the text. |
| -32021 | SnapshotExpired | The snapshot ran out of time, was released, or its plugin or document ended. | Take a new snapshot of the current revision. |
| -32022 | SnapshotQuotaExceeded | The plugin holds as many snapshots, or as many bytes in them, as it may (`data.reason`: `Count` with `limit`, or `Bytes` with `limitBytes` and `retainedBytes`). | Release snapshots (`snapshot/release`) it no longer needs. |
| -32023 | PermissionDenied | The plugin does not hold the permission the call needs for that target (`data.permission`; `data.reason`: `NotGranted`, `Revoked`, `Delegated`, `Secret`, `OutsideWorkspace`, `Link`). | `NotGranted`: the user has to grant it; declare it in `permissions` of the manifest and tell the user what the command needs it for. `Revoked`: the user took it away while the request ran; stop the work. `Delegated`: a command that another plugin started may not write documents. `Secret`: the edit or the diff touches a hidden value of a secrets file; the user can allow it with `document.secrets.reveal` for that document. `OutsideWorkspace`, `Link`: name a path below the workspace folders, and the file a link points to. |
| -32024 | UnknownDocument | The document is not open, or is not available to plugins. | List the documents again (`document/list`). |
| -32025 | InvalidRange | A range is outside the document, inside the byte order mark or not on a character boundary; or a hunk number does not exist (`data.hunkCount`). | Take offsets from `document/info` (`bomLength`, `byteLength`) and read text with `document/readText`, which stops at character boundaries. |
| -32026 | PositionUnknown | The line index does not reach the position yet, or the offset is not a position (`data.lines`, `data.complete`). | Go on with byte offsets, or ask again once `complete` is true. |
| -32027 | EditsOverlap | Two edits of one batch touch the same bytes. | Nothing was applied. Make the edits disjoint, or send them as two batches. |
| -32028 | SymbolsQuotaExceeded | The plugin published more symbols than it may. | Publish fewer, for example only the top levels. |
| -32029 | NoTextView | The document has no text view to take selections from or to show a range in (a hex view, for example). | Use the bytes of the document instead, or skip the document. |
| -32030 | NoTableView | The document is not shown as a table, so no cells are selected. | Ask only for documents shown as a table. |
| -32031 | ReadOnlyDocument | The document is a virtual document that is read only (`data.reason`: `Virtual`). | Change its content with `virtual/update` from the plugin that provides it. |
| -32032 | NotProvider | The document is not a virtual document this plugin provided. | Only the provider updates, stages into or closes it. |
| -32033 | VirtualQuotaExceeded | The plugin holds as many virtual documents as it may (`data.limit`). | Close one (`virtual/close`) first. |
| -32034 | DiagnosticsQuotaExceeded | The plugin published more diagnostics than it may for one source of one document. | Publish the most important ones. |
| -32035 | WorkspaceQuotaExceeded | A workspace edit holds more changes than the editor takes. | Split it into several edits. |
| -32036 | SourceConflict | A file source refused a write because the file is not at the expected revision (`data.currentRevision`, a string, or null when the file is gone). | Read the file again and merge; do not overwrite. |
| -32037 | SourceUnavailable | A file source cannot reach its data now (no connection); the editor may serve the cached copy. | Try again later. |
| -32038 | SourceNotFound | The source has no such file. | Check the address. |
| -32039 | SourceSchemeTaken | The URI scheme is already served by another plugin. | Choose another scheme, or ask the user to turn the other plugin off. |
| -32040 | AgentPanelBusy | The agent panel holds another conversation (an agent over ACP or another plugin). | Wait until it ends, or tell the user. |
| -32041 | NoAgentSession | The plugin has no conversation in the agent panel (never opened, or the user or the editor ended it). | Open it again with `agent/open`. |
| -32042 | NetworkUnavailable | A `net/request` got no answer: no connection, the proxy refused it, a timeout, an answer over the limit, or a network policy the editor cannot read. The `message` says which. | The request may have reached the server. Try again later; ask for less if the answer was too large. |
| -32043 | DiffTooLarge | A side of a diff is larger than the editor compares for a plugin (`data.limitBytes`, `data.bytes`). | Compare smaller parts. |
| -32044 | DecorationsQuotaExceeded | The plugin published more decorations (inlay hints or semantic tokens) of one kind for a document than it may (`data.limit`). | Publish the ones in the visible part of the document, or fewer. |

## User interface

| Code | Name | When | What to do |
| --- | --- | --- | --- |
| -32050 | QuotaExceeded | The plugin has as many panels, items, tasks or choices as the editor allows. | Remove a panel (`ui/panel/remove`) or end a task first, or send fewer items. |
| -32051 | UnknownTask | The task does not exist: never started, already ended, or ended by the editor after the user cancelled it and the plugin did not end it in time. | Stop reporting progress for it. |
