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
        type:
          switch-on: type
          cases:
            '"IHDR"': header
            '"gAMA"': gamma
            '"tEXt"': text
      - id: crc
        type: u4
  header:
    enums:
      color_type:
        0: grayscale
        2: truecolor
        3: indexed
        4: grayscale_alpha
        6: truecolor_alpha
    seq:
      - id: width
        type: u4
      - id: height
        type: u4
      - id: bit_depth
        type: u1
      - id: color_type
        type: u1
        enum: color_type
      - id: compression
        type: u1
      - id: filter
        type: u1
      - id: interlace
        type: u1
  gamma:
    seq:
      - id: raw
        type: u4
    instances:
      gamma:
        value: raw / 100000.0
  text:
    seq:
      - id: keyword
        type: strz
        encoding: ISO-8859-1
      - id: text
        type: str
        size-eos: true
        encoding: ISO-8859-1
