# Windows PE image (EXE, DLL, SYS): DOS stub header, COFF header, optional header, data directories and section table.
# Written for Textida from Microsoft's public description of the PE format; MIT, like the rest of this repository.
meta:
  id: pe_file
  title: Windows PE image headers
  file-extension: [exe, dll, sys, ocx, scr, efi, cpl]
  endian: le
  bit-endian: le
  encoding: ASCII
seq:
  - id: magic
    contents: MZ
  - id: dos_header
    size: 58
  - id: pe_offset
    type: u4
instances:
  pe:
    pos: pe_offset
    type: pe_header
types:
  pe_header:
    enums:
      machine:
        0x0000: unknown
        0x014c: i386
        0x01c0: arm
        0x01c4: arm_thumb2
        0x0200: ia64
        0x5032: riscv32
        0x5064: riscv64
        0x8664: amd64
        0xaa64: arm64
    seq:
      - id: signature
        contents: [0x50, 0x45, 0x00, 0x00]
      - id: machine
        type: u2
        enum: machine
      - id: section_count
        type: u2
      - id: time_date_stamp
        type: u4
      - id: symbol_table_offset
        type: u4
      - id: symbol_count
        type: u4
      - id: optional_header_size
        type: u2
      - id: characteristics
        type: file_characteristics
        size: 2
      - id: optional_header
        type: optional_header
        size: optional_header_size
        if: optional_header_size > 0
      - id: sections
        type: section_header
        repeat: expr
        repeat-expr: section_count
  file_characteristics:
    seq:
      - id: relocations_stripped
        type: b1
      - id: executable_image
        type: b1
      - id: line_numbers_stripped
        type: b1
      - id: local_symbols_stripped
        type: b1
      - id: aggressive_trim
        type: b1
      - id: large_address_aware
        type: b1
      - id: reserved_6
        type: b1
      - id: bytes_reversed_lo
        type: b1
      - id: machine_32bit
        type: b1
      - id: debug_stripped
        type: b1
      - id: removable_run_from_swap
        type: b1
      - id: net_run_from_swap
        type: b1
      - id: system_file
        type: b1
      - id: dll
        type: b1
      - id: up_system_only
        type: b1
      - id: bytes_reversed_hi
        type: b1
  optional_header:
    enums:
      pe_format:
        0x010b: pe32
        0x020b: pe32_plus
      subsystem:
        1: native
        2: windows_gui
        3: windows_console
        5: os2_console
        7: posix_console
        9: windows_ce_gui
        10: efi_application
        11: efi_boot_service_driver
        12: efi_runtime_driver
        14: xbox
    seq:
      - id: format
        type: u2
        enum: pe_format
      - id: linker_major
        type: u1
      - id: linker_minor
        type: u1
      - id: code_size
        type: u4
      - id: initialized_data_size
        type: u4
      - id: uninitialized_data_size
        type: u4
      - id: entry_point_rva
        type: u4
      - id: code_base_rva
        type: u4
      - id: data_base_rva
        type: u4
        if: format == pe_format::pe32
      - id: image_base
        type:
          switch-on: format
          cases:
            pe_format::pe32: u4
            pe_format::pe32_plus: u8
      - id: section_alignment
        type: u4
      - id: file_alignment
        type: u4
      - id: os_major
        type: u2
      - id: os_minor
        type: u2
      - id: image_major
        type: u2
      - id: image_minor
        type: u2
      - id: subsystem_major
        type: u2
      - id: subsystem_minor
        type: u2
      - id: win32_version
        type: u4
      - id: image_size
        type: u4
      - id: headers_size
        type: u4
      - id: checksum
        type: u4
      - id: subsystem
        type: u2
        enum: subsystem
      - id: dll_characteristics
        type: u2
      - id: stack_reserve
        type:
          switch-on: format
          cases:
            pe_format::pe32: u4
            pe_format::pe32_plus: u8
      - id: stack_commit
        type:
          switch-on: format
          cases:
            pe_format::pe32: u4
            pe_format::pe32_plus: u8
      - id: heap_reserve
        type:
          switch-on: format
          cases:
            pe_format::pe32: u4
            pe_format::pe32_plus: u8
      - id: heap_commit
        type:
          switch-on: format
          cases:
            pe_format::pe32: u4
            pe_format::pe32_plus: u8
      - id: loader_flags
        type: u4
      - id: data_directory_count
        type: u4
      - id: data_directories
        type: data_directory
        repeat: expr
        repeat-expr: data_directory_count
  data_directory:
    seq:
      - id: virtual_address
        type: u4
      - id: size
        type: u4
  section_header:
    seq:
      - id: name
        type: str
        size: 8
      - id: virtual_size
        type: u4
      - id: virtual_address
        type: u4
      - id: raw_data_size
        type: u4
      - id: raw_data_offset
        type: u4
      - id: relocations_offset
        type: u4
      - id: line_numbers_offset
        type: u4
      - id: relocation_count
        type: u2
      - id: line_number_count
        type: u2
      - id: characteristics
        type: u4
    instances:
      raw_data:
        pos: raw_data_offset
        size: raw_data_size
