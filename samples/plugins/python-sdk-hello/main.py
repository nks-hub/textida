"""Sample plugin on the Python SDK (sdk/python): writes the selections of the document, or all of it, in capital letters as one undo step."""
from textida_plugin import Plugin

plugin = Plugin("python-sdk-hello")


@plugin.command("example.python-sdk-hello.shout")
def shout(context):
    document = context.document()
    info = document.info()
    ranges = [(min(s["anchor"], s["caret"]), max(s["anchor"], s["caret"])) for s in document.selections() if s["anchor"] != s["caret"]]
    if not ranges:
        ranges = [(info["bomLength"], info["byteLength"])]

    edits = []
    for start, end in ranges:
        read = document.read_text(start, end)
        if read["invalid"]:
            return {"status": "failed", "diagnostics": ["The range holds bytes that are not valid in the encoding of the file."]}
        shouted = read["text"].upper()
        if shouted != read["text"]:
            edits.append({"start": start, "end": end, "text": shouted})
    if not edits:
        return "unchanged"
    document.apply(edits)
    return "applied"


plugin.run()
