# Binary STL mesh: an 80-byte header, the number of triangles and, for each triangle, its normal, three vertices and two attribute bytes.
# Written for Textida from the description of the format; MIT, like the rest of this repository.
# An ASCII STL file (it starts with the word solid) is not read by this template.
meta:
  id: stl_binary
  title: Binary STL mesh
  file-extension: stl
  endian: le
seq:
  - id: header
    size: 80
  - id: triangle_count
    type: u4
  - id: triangles
    type: triangle
    repeat: expr
    repeat-expr: triangle_count
types:
  triangle:
    seq:
      - id: normal
        type: vector
      - id: vertices
        type: vector
        repeat: expr
        repeat-expr: 3
      - id: attribute_bytes
        type: u2
  vector:
    seq:
      - id: x
        type: f4
      - id: y
        type: f4
      - id: z
        type: f4
