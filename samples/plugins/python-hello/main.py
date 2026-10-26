"""Smallest plugin process: speaks the Textida plugin protocol (one JSON-RPC 2.0 message per line on stdin/stdout)."""
import json
import sys


def send(message):
    sys.stdout.write(json.dumps(message) + "\n")
    sys.stdout.flush()


def reply(request_id, result):
    send({"jsonrpc": "2.0", "id": request_id, "result": result})


for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    message = json.loads(line)
    method = message.get("method")
    request_id = message.get("id")
    if request_id is None:
        if method == "exit":
            break
        continue
    if method == "initialize":
        reply(request_id, {"apiVersion": message["params"]["apiVersion"], "name": "python-hello"})
    elif method == "command/execute":
        print("executed " + message["params"]["command"], file=sys.stderr)
        reply(request_id, True)
    elif method in ("$/resync", "$/ping", "shutdown"):
        reply(request_id, None)
    else:
        send({"jsonrpc": "2.0", "id": request_id, "error": {"code": -32601, "message": "Unknown method " + str(method)}})
