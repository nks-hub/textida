"""Plugin process for the hex view: a data inspector decoder, a hash, a text exporter and a template source.

It speaks the Textida plugin protocol (one JSON-RPC 2.0 message per line on stdin/stdout). Every hex request carries the bytes as Base64 in "data"
and is answered with text; the plugin cannot change a file or a document by answering.
"""
import base64
import hashlib
import json
import sys

TEMPLATE = """meta:
  id: pair
  title: Pair of numbers (sample plugin)
  endian: le
seq:
  - id: first
    type: u2
  - id: second
    type: u2
"""


def send(message):
    sys.stdout.write(json.dumps(message) + "\n")
    sys.stdout.flush()


def reply(request_id, result):
    send({"jsonrpc": "2.0", "id": request_id, "result": result})


def decode(params):
    data = base64.b64decode(params["data"])
    if params["decoder"] == "bcd":
        if len(data) < 1:
            return {"status": "incomplete", "text": "", "length": 1}
        digits = [data[0] >> 4, data[0] & 15]
        if max(digits) > 9:
            return {"status": "invalid", "text": "", "length": 1}
        return {"status": "ok", "text": "%d%d" % tuple(digits), "length": 1}
    raise ValueError("unknown decoder " + params["decoder"])


def export(params):
    data = base64.b64decode(params["data"])
    body = ",\n".join("  0x%02X" % b for b in data)
    return {"name": "bytes.c", "text": "const unsigned char data[%d] = {\n%s\n};\n" % (len(data), body)}


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
    params = message.get("params") or {}
    try:
        if method == "initialize":
            reply(request_id, {"apiVersion": params["apiVersion"], "name": "python-hex"})
        elif method == "hex/decode":
            reply(request_id, decode(params))
        elif method == "hex/hash":
            reply(request_id, {"value": hashlib.blake2b(base64.b64decode(params["data"]), digest_size=16).hexdigest()})
        elif method == "hex/export":
            reply(request_id, export(params))
        elif method == "hex/templates":
            reply(request_id, {"templates": [{"name": "pair.ksy", "ksy": TEMPLATE}]})
        elif method in ("$/resync", "$/ping", "shutdown"):
            reply(request_id, None)
        else:
            send({"jsonrpc": "2.0", "id": request_id, "error": {"code": -32601, "message": "Unknown method " + str(method)}})
    except (KeyError, ValueError) as e:
        send({"jsonrpc": "2.0", "id": request_id, "error": {"code": -32602, "message": str(e)}})
