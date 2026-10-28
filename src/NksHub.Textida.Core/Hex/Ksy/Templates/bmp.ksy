meta:
  id: bmp_header
  title: BMP file header and bitmap info header
  file-extension: bmp
  endian: le
seq:
  - id: magic
    contents: BM
  - id: file_size
    type: u4
  - id: reserved
    size: 4
  - id: pixel_offset
    type: u4
  - id: info
    type: info_header
types:
  info_header:
    seq:
      - id: header_size
        type: u4
      - id: width
        type: s4
      - id: height
        type: s4
      - id: planes
        type: u2
      - id: bits_per_pixel
        type: u2
      - id: compression
        type: u4
        enum: compression
    instances:
      row_size:
        value: (bits_per_pixel * width + 31) / 32 * 4
enums:
  compression:
    0: none
    1: rle8
    2: rle4
    3: bitfields
