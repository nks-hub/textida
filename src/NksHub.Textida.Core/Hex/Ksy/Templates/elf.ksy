# ELF executable, shared object or object file: identification bytes, file header, program headers and section headers
# for 32 and 64 bits, little and big endian. Written for Textida from the System V gABI ELF chapter; MIT, like the rest of this repository.
meta:
  id: elf_file
  title: ELF file and program and section headers
  file-extension: [elf, so, o, ko, axf]
  encoding: ASCII
seq:
  - id: magic
    contents: [0x7f, E, L, F]
  - id: bits
    type: u1
    enum: bits
  - id: endian
    type: u1
    enum: endian
  - id: ident_version
    type: u1
  - id: os_abi
    type: u1
    enum: os_abi
  - id: abi_version
    type: u1
  - id: padding
    size: 7
  - id: header
    type: header
enums:
  bits:
    1: b32
    2: b64
  endian:
    1: le
    2: be
  os_abi:
    0: system_v
    1: hp_ux
    2: netbsd
    3: linux
    6: solaris
    9: freebsd
    12: openbsd
    97: arm
    255: standalone
types:
  header:
    meta:
      endian:
        switch-on: _root.endian
        cases:
          endian::le: le
          endian::be: be
    enums:
      file_type:
        0: none
        1: relocatable
        2: executable
        3: shared
        4: core
      machine:
        0: none
        2: sparc
        3: x86
        8: mips
        20: powerpc
        21: powerpc64
        22: s390
        40: arm
        42: superh
        50: ia64
        62: x86_64
        183: aarch64
        243: riscv
        247: bpf
    seq:
      - id: file_type
        type: u2
        enum: file_type
      - id: machine
        type: u2
        enum: machine
      - id: version
        type: u4
      - id: entry_point
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: program_header_offset
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: section_header_offset
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: flags
        type: u4
      - id: header_size
        type: u2
      - id: program_header_size
        type: u2
      - id: program_header_count
        type: u2
      - id: section_header_size
        type: u2
      - id: section_header_count
        type: u2
      - id: section_names_index
        type: u2
    instances:
      program_headers:
        pos: program_header_offset
        type: program_header
        size: program_header_size
        repeat: expr
        repeat-expr: program_header_count
      section_headers:
        pos: section_header_offset
        type: section_header
        size: section_header_size
        repeat: expr
        repeat-expr: section_header_count
  program_header:
    meta:
      endian:
        switch-on: _root.endian
        cases:
          endian::le: le
          endian::be: be
    enums:
      segment_type:
        0: unused
        1: load
        2: dynamic
        3: interpreter
        4: note
        5: shlib
        6: program_header
        7: tls
    seq:
      - id: type
        type: u4
        enum: segment_type
      - id: flags64
        type: u4
        if: _root.bits == bits::b64
      - id: offset
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: virtual_address
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: physical_address
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: file_size
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: memory_size
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: flags32
        type: u4
        if: _root.bits == bits::b32
      - id: align
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
  section_header:
    meta:
      endian:
        switch-on: _root.endian
        cases:
          endian::le: le
          endian::be: be
    enums:
      section_type:
        0: unused
        1: progbits
        2: symtab
        3: strtab
        4: rela
        5: hash
        6: dynamic
        7: note
        8: nobits
        9: rel
        10: shlib
        11: dynsym
        14: init_array
        15: fini_array
    seq:
      - id: name_offset
        type: u4
      - id: type
        type: u4
        enum: section_type
      - id: flags
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: address
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: offset
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: size
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: link
        type: u4
      - id: info
        type: u4
      - id: align
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: entry_size
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
    instances:
      body:
        io: _root._io
        pos: offset
        size: size
        type:
          switch-on: type
          cases:
            section_type::dynamic: dynamic_section
        if: type != section_type::nobits and type != section_type::unused
  dynamic_section:
    seq:
      - id: entries
        type: dynamic_entry
        repeat: until
        repeat-until: _.tag == 0
  dynamic_entry:
    meta:
      endian:
        switch-on: _root.endian
        cases:
          endian::le: le
          endian::be: be
    seq:
      - id: tag
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
      - id: value
        type:
          switch-on: _root.bits
          cases:
            bits::b32: u4
            bits::b64: u8
