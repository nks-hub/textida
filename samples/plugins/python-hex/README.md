# python-hex

A plugin process that adds to the hex view of Textida: a BCD decoder in the data inspector, a BLAKE2b-128 hash, a C array exporter and a Kaitai Struct template source.

The plugin needs the permission `hex.read`. Each request (`hex/decode`, `hex/hash`, `hex/export`, `hex/templates`) carries the bytes as Base64 in `data` and is answered with text, so the plugin changes no file and no document. To change bytes a plugin uses the document API, where the edit comes back as a proposal in the change review.

Replace `/path/to/python` in `nkspad-plugin.json` with the full path of a Python 3 interpreter before installing.
