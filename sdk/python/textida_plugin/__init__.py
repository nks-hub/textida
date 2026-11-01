"""Textida plugin SDK for Python: the plugin protocol (JSON-RPC 2.0, one message per line on stdin and stdout) and the calls of the document API.

It has no dependencies. The types of the calls are in textida_plugin.api, generated from the types of the C# SDK.
"""
from __future__ import annotations

import io
import json
import sys
import uuid
from typing import Any, Callable

from . import api

API_VERSION = 1
METHOD_NOT_FOUND = -32601
INVALID_PARAMS = -32602
INTERNAL_ERROR = -32603
REVISION_CONFLICT = -32020

__all__ = ["Plugin", "Document", "RpcError", "CommandContext", "API_VERSION", "api"]


class RpcError(Exception):
    """An error answer of the editor, or an error a handler wants to send."""

    def __init__(self, code: int, message: str, data: Any = None) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.data = data


class Document:
    """The document a command started on, bound to the revision the command saw: a call fails with a revision conflict when the document has moved on."""

    def __init__(self, plugin: "Plugin", id: str, revision: int) -> None:
        self.plugin = plugin
        self.id = id
        self.revision = revision

    def info(self) -> api.DocumentInfoResult:
        info = self.plugin.request("document/info", {"document": self.id})
        if info["revision"] != self.revision:
            raise RpcError(REVISION_CONFLICT, "Stale.")
        return info

    def selections(self) -> list[api.Selection]:
        """The selections as byte offsets, a bare caret included."""
        result = self.plugin.request("document/selections", {"document": self.id})
        if result["revision"] != self.revision:
            raise RpcError(REVISION_CONFLICT, "Stale.")
        return result["selections"]

    def read_text(self, start: int, end: int) -> api.ReadTextResult:
        """The bytes [start, end) as text, on character boundaries."""
        return self.plugin.request("document/readText", {"document": self.id, "revision": self.revision, "start": start, "end": end})

    def facts(self) -> api.FactsResult:
        return self.plugin.request("document/facts", {"document": self.id, "revision": self.revision})

    def position(self, offset: int) -> api.PositionResult:
        return self.plugin.request("document/offsetToPosition", {"document": self.id, "revision": self.revision, "offset": offset})

    def apply(self, edits: list[api.RangeEdit], batch_id: str | None = None) -> None:
        """Applies all edits as one undo step; none is applied unless all are. Needs the permission document.edit.direct."""
        self.plugin.request("edit/apply", {"document": self.id, "revision": self.revision, "batchId": batch_id or uuid.uuid4().hex, "edits": edits})

    def propose(self, title: str, edits: list[api.RangeEdit]) -> None:
        """Offers the edits as a proposal the user reviews; the document stays as it is until then. Needs document.edit.propose."""
        self.plugin.request("edit/propose", {"document": self.id, "revision": self.revision, "title": title, "edits": edits})


class CommandContext:
    """One command/execute request: the command, the language of the editor and the document it started on."""

    def __init__(self, plugin: "Plugin", params: dict[str, Any]) -> None:
        self.plugin = plugin
        self.command: str = params["command"]
        self.language: str = params.get("language", "en")
        self.params = params

    def document(self) -> Document:
        """The document and revision the command started on; raises when the command names none."""
        document, revision = self.params.get("document"), self.params.get("revision")
        if not isinstance(document, str) or not isinstance(revision, int):
            raise RpcError(INVALID_PARAMS, "The command names no document and revision.")
        return Document(self.plugin, document, revision)


class Plugin:
    """A plugin process. Register commands with @plugin.command("id") and call run().

    A handler runs on the thread that reads the messages; a call to the editor made from a handler waits for the answer and handles the messages that come meanwhile
    (a ping, an event) inside the wait, so a handler may call the editor as often as it likes.
    """

    def __init__(self, name: str, input: Any = None, output: Any = None) -> None:
        self.name = name
        # the protocol is UTF-8 whatever the code page of the console is
        self._input = input or io.TextIOWrapper(sys.stdin.buffer, encoding="utf-8", newline="\n")
        self._output = output or io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", newline="\n", write_through=True)
        self._commands: dict[str, Callable[[CommandContext], Any]] = {}
        self._methods: dict[str, Callable[[dict[str, Any]], Any]] = {}
        self._events: dict[str, Callable[[Any], None]] = {}
        self._next_id = 1
        self._answers: dict[int, dict[str, Any]] = {}
        self._exit = False
        self.settings: dict[str, Any] = {}

    def command(self, command_id: str):
        """Decorator for the handler of a command of the manifest. It gets a CommandContext and returns {"status": ...}, a status word or None."""

        def register(handler: Callable[[CommandContext], Any]):
            self._commands[command_id] = handler
            return handler

        return register

    def method(self, name: str):
        """Decorator for a method the editor may call that is not a command; the handler gets the parameters."""

        def register(handler: Callable[[dict[str, Any]], Any]):
            self._methods[name] = handler
            return handler

        return register

    def event(self, name: str):
        """Decorator for the handler of an event of the editor ($/event with this name); it gets the payload."""

        def register(handler: Callable[[Any], None]):
            self._events[name] = handler
            return handler

        return register

    def _send(self, message: dict[str, Any]) -> None:
        self._output.write(json.dumps(message, separators=(",", ":")) + "\n")
        self._output.flush()

    def request(self, method: str, params: Any = None) -> Any:
        """Calls a method of the editor and returns its result; raises RpcError for an error answer."""
        request_id = self._next_id
        self._next_id += 1
        self._send({"jsonrpc": "2.0", "id": request_id, "method": method, "params": params})
        while request_id not in self._answers:
            if not self._step():
                raise RpcError(INTERNAL_ERROR, "The editor closed the connection.")
        answer = self._answers.pop(request_id)
        if "error" in answer:
            error = answer["error"]
            raise RpcError(error.get("code", INTERNAL_ERROR), error.get("message", ""), error.get("data"))
        return answer.get("result")

    def notify(self, method: str, params: Any = None) -> None:
        self._send({"jsonrpc": "2.0", "method": method, "params": params})

    def show_notice(self, level: str, message: str) -> None:
        """Shows a notice in the window of the editor. level is info, warning or error."""
        self.request("ui/notify", {"level": level, "message": message})

    def run(self) -> None:
        """Handles the messages of the editor until it ends the process or closes the input."""
        while not self._exit and self._step():
            pass

    def _step(self) -> bool:
        line = self._input.readline()
        if line == "":
            return False
        line = line.strip()
        if not line:
            return True
        try:
            message = json.loads(line)
        except ValueError:
            return True
        if "method" in message:
            if "id" in message:
                self._answer(message)
            else:
                self._notification(message["method"], message.get("params") or {})
        elif "id" in message:
            self._answers[message["id"]] = message
        return True

    def _notification(self, method: str, params: dict[str, Any]) -> None:
        if method == "exit":
            self._exit = True
        elif method == "$/event":
            handler = self._events.get(params.get("name", ""))
            if handler:
                handler(params.get("payload"))

    def _answer(self, message: dict[str, Any]) -> None:
        try:
            result = self._dispatch(message["method"], message.get("params") or {})
            self._send({"jsonrpc": "2.0", "id": message["id"], "result": result})
        except RpcError as e:
            self._send({"jsonrpc": "2.0", "id": message["id"], "error": {"code": e.code, "message": e.message, "data": e.data}})
        except Exception as e:  # a handler's failure is the plugin's, the editor gets an error and the plugin goes on
            print(f"{self.name}: {message['method']} failed: {e}", file=sys.stderr, flush=True)
            self._send({"jsonrpc": "2.0", "id": message["id"], "error": {"code": INTERNAL_ERROR, "message": str(e)}})

    def _dispatch(self, method: str, params: dict[str, Any]) -> Any:
        if method == "initialize":
            if params.get("apiVersion") != API_VERSION:
                raise RpcError(INVALID_PARAMS, f"This SDK speaks plugin API version {API_VERSION}, the editor asked for {params.get('apiVersion')}.")
            self.settings = params.get("settings") or {}
            return {"apiVersion": API_VERSION, "name": self.name}
        if method == "$/resync":
            return None
        if method == "$/ping":
            return True
        if method == "shutdown":
            return None
        if method == "command/execute":
            handler = self._commands.get(params.get("command", ""))
            if handler is None:
                raise RpcError(METHOD_NOT_FOUND, f"Unknown command {params.get('command')}")
            result = handler(CommandContext(self, params))
            if isinstance(result, str):
                return {"status": result}
            return result or {"status": "done"}
        handler = self._methods.get(method)
        if handler is None:
            raise RpcError(METHOD_NOT_FOUND, f"Unknown method {method}")
        return handler(params)
