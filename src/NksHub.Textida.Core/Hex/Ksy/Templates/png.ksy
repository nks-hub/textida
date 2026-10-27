meta:
  id: png_chunks
  title: PNG signature and chunk list
  file-extension: png
  endian: be
seq:
  - id: signature
    contents: [0x89, PNG, 0x0d, 0x0a, 0x1a, 0x0a]
  - id: chunks
    type: chunk
    repeat: eos
types:
  chunk:
    seq:
      - id: length
        type: u4
      - id: type
        type: str
        size: 4
        encoding: ASCII
      - id: body
        size: length
      - id: crc
        type: u4
