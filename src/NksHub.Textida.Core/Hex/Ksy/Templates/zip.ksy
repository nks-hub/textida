# ZIP archive, read as the sequence of sections that PKWARE's APPNOTE describes.
# Written for Textida from that document; MIT, like the rest of this repository.
# An entry that has a data descriptor (general purpose flag bit 3) stores its sizes after the data, so the walk
# from section to section ends at that entry, with the error of the first byte that does not start a section.
meta:
  id: zip_archive
  title: ZIP archive sections
  file-extension: [zip, jar, apk, docx, xlsx, pptx, odt, ods, odp, epub]
  endian: le
  bit-endian: le
  encoding: UTF-8
seq:
  - id: sections
    type: section
    repeat: eos
types:
  section:
    enums:
      section_kind:
        0x0403: local_file
        0x0201: central_entry
        0x0605: end_of_central_directory
        0x0606: zip64_end_of_central_directory
        0x0706: zip64_locator
        0x0807: data_descriptor
    seq:
      - id: magic
        contents: PK
      - id: kind
        type: u2
        enum: section_kind
      - id: body
        type:
          switch-on: kind
          cases:
            section_kind::local_file: local_file
            section_kind::central_entry: central_entry
            section_kind::end_of_central_directory: end_of_central_directory
            section_kind::zip64_end_of_central_directory: zip64_end_of_central_directory
            section_kind::zip64_locator: zip64_locator
            section_kind::data_descriptor: data_descriptor
            _: unknown_section
  local_file:
    enums:
      compression:
        0: stored
        1: shrunk
        6: imploded
        8: deflated
        9: deflate64
        12: bzip2
        14: lzma
        93: zstandard
        95: xz
        98: ppmd
    seq:
      - id: version_needed
        type: u2
      - id: flags
        type: general_flags
        size: 2
      - id: compression
        type: u2
        enum: compression
      - id: mod_time
        type: u2
      - id: mod_date
        type: u2
      - id: crc32
        type: u4
      - id: compressed_size
        type: u4
      - id: uncompressed_size
        type: u4
      - id: name_len
        type: u2
      - id: extra_len
        type: u2
      - id: name
        type: str
        size: name_len
      - id: extra
        size: extra_len
      - id: body
        size: compressed_size
  general_flags:
    seq:
      - id: encrypted
        type: b1
      - id: compression_option
        type: b2
      - id: has_data_descriptor
        type: b1
      - id: enhanced_deflate
        type: b1
      - id: patched_data
        type: b1
      - id: strong_encryption
        type: b1
      - id: unused
        type: b4
      - id: utf8_names
        type: b1
      - id: reserved_12
        type: b1
      - id: masked_header
        type: b1
      - id: reserved_14
        type: b2
  central_entry:
    seq:
      - id: version_made_by
        type: u2
      - id: version_needed
        type: u2
      - id: flags
        type: general_flags
        size: 2
      - id: compression
        type: u2
        enum: local_file::compression
      - id: mod_time
        type: u2
      - id: mod_date
        type: u2
      - id: crc32
        type: u4
      - id: compressed_size
        type: u4
      - id: uncompressed_size
        type: u4
      - id: name_len
        type: u2
      - id: extra_len
        type: u2
      - id: comment_len
        type: u2
      - id: disk_start
        type: u2
      - id: internal_attributes
        type: u2
      - id: external_attributes
        type: u4
      - id: local_header_offset
        type: u4
      - id: name
        type: str
        size: name_len
      - id: extra
        size: extra_len
      - id: comment
        type: str
        size: comment_len
  end_of_central_directory:
    seq:
      - id: disk_number
        type: u2
      - id: central_directory_disk
        type: u2
      - id: entries_on_disk
        type: u2
      - id: entries_total
        type: u2
      - id: central_directory_size
        type: u4
      - id: central_directory_offset
        type: u4
      - id: comment_len
        type: u2
      - id: comment
        type: str
        size: comment_len
  zip64_end_of_central_directory:
    seq:
      - id: record_size
        type: u8
      - id: version_made_by
        type: u2
      - id: version_needed
        type: u2
      - id: disk_number
        type: u4
      - id: central_directory_disk
        type: u4
      - id: entries_on_disk
        type: u8
      - id: entries_total
        type: u8
      - id: central_directory_size
        type: u8
      - id: central_directory_offset
        type: u8
      - id: extensible_data
        size: record_size - 44
  zip64_locator:
    seq:
      - id: disk_with_end_record
        type: u4
      - id: end_record_offset
        type: u8
      - id: disk_count
        type: u4
  data_descriptor:
    seq:
      - id: crc32
        type: u4
      - id: compressed_size
        type: u4
      - id: uncompressed_size
        type: u4
  unknown_section:
    seq:
      - id: data
        size-eos: true
