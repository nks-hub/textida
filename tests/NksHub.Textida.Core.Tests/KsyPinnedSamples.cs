namespace NksHub.Textida.Core.Tests;

/// <summary>A file for a pinned template: the template, the bytes and how deep the tree is shown. Every pinned template has a valid file and damaged ones (P8.10.5).</summary>
internal sealed record KsyPinnedSample(string Name, string TemplateId, byte[] Data, int Depth = 3);

internal static class KsyPinnedSamples
{
    private static byte[] Cut(byte[] data, int length) => data[..length];

    private static byte[] With(byte[] data, int at, params byte[] bytes)
    {
        var copy = (byte[])data.Clone();
        bytes.CopyTo(copy, at);
        return copy;
    }

    public static string Describe(KsyPinnedSample sample) => KsySamples.Dump(KsySamples.Read(sample.TemplateId, sample.Data), sample.Depth);

    /// <summary>The damaged files, with what is wrong: a wrong magic number, a file cut short, a count that does not fit the file and a byte order that no case names.</summary>
    public static IReadOnlyList<KsyPinnedSample> All { get; } =
    [
        new("bmp-valid", "bmp_header", KsySamples.Bmp()),
        new("bmp-wrong-magic", "bmp_header", With(KsySamples.Bmp(), 0, (byte)'X')),
        new("bmp-cut-in-the-info-header", "bmp_header", Cut(KsySamples.Bmp(), 32)),
        new("png-valid", "png_chunks", KsySamples.Png()),
        new("png-wrong-signature", "png_chunks", With(KsySamples.Png(), 1, (byte)'X')),
        new("png-cut-in-a-chunk", "png_chunks", Cut(KsySamples.Png(), 43)),
        new("zip-valid", "zip_archive", KsySamples.Zip()),
        new("zip-wrong-magic", "zip_archive", With(KsySamples.Zip(), 0, (byte)'X', (byte)'X')),
        new("zip-cut-in-the-central-directory", "zip_archive", Cut(KsySamples.Zip(), 70)),
        new("pe-valid", "pe_file", KsySamples.Pe(), 2),
        new("pe-wrong-signature", "pe_file", With(KsySamples.Pe(), 64, (byte)'X')),
        new("pe-cut-in-the-optional-header", "pe_file", Cut(KsySamples.Pe(), 300)),
        new("pe-more-sections-than-fit", "pe_file", With(KsySamples.Pe(), 70, 0xFF, 0xFF)),
        new("elf64-valid", "elf_file", KsySamples.Elf64()),
        new("elf32-big-endian-valid", "elf_file", KsySamples.Elf32Be()),
        new("elf-unknown-byte-order", "elf_file", With(KsySamples.Elf64(), 5, 3)),
        new("elf-cut-in-the-header", "elf_file", Cut(KsySamples.Elf64(), 30)),
        new("elf-more-section-headers-than-the-limit", "elf_file", With(KsySamples.Elf64(), 60, 0xFF, 0xFF)),
        new("stl-valid", "stl_binary", KsySamples.Stl()),
        new("stl-one-triangle-missing", "stl_binary", With(KsySamples.Stl(), 80, 3)),
        new("stl-count-over-the-limit", "stl_binary", With(KsySamples.Stl(), 80, 0xFF, 0xFF, 0xFF, 0xFF)),
    ];

    /// <summary>The tree each file is expected to read into.</summary>
    public static IReadOnlyDictionary<string, string> Expected { get; } = new Dictionary<string, string>
    {
        ["bmp-valid"] = """
            bmp_header=@0+54
              magic=424D@0+2
              file_size=54 (0x00000036)@2+4
              reserved=00000000@6+4
              pixel_offset=54 (0x00000036)@10+4
              info=@14+20
                header_size=40 (0x00000028)@14+4
                width=3@18+4
                height=-2@22+4
                planes=1 (0x0001)@26+2
                bits_per_pixel=24 (0x0018)@28+2
                compression=bitfields (3)@30+4
                row_size=12@14+0
            """,
        ["bmp-wrong-magic"] = """
            bmp_header=@0+54
              magic=!Contents@0+2
            """,
        ["bmp-cut-in-the-info-header"] = """
            bmp_header=@0+32
              magic=424D@0+2
              file_size=54 (0x00000036)@2+4
              reserved=00000000@6+4
              pixel_offset=54 (0x00000036)@10+4
              info=!OutOfFile@14+18
            """,
        ["png-valid"] = """
            png_chunks=@0+99
              signature=89504E470D0A1A0A@0+8
              chunks=@8+91
                chunks[0]=@8+25
                  length=13 (0x0000000D)@8+4
                  type="IHDR"@12+4
                  body=@16+13
                  crc=0 (0x00000000)@29+4
                chunks[1]=@33+16
                  length=4 (0x00000004)@33+4
                  type="gAMA"@37+4
                  body=@41+4
                  crc=0 (0x00000000)@45+4
                chunks[2]=@49+23
                  length=11 (0x0000000B)@49+4
                  type="tEXt"@53+4
                  body=@57+11
                  crc=0 (0x00000000)@68+4
                chunks[3]=@72+15
                  length=3 (0x00000003)@72+4
                  type="IDAT"@76+4
                  body=010203@80+3
                  crc=0 (0x00000000)@83+4
                chunks[4]=@87+12
                  length=0 (0x00000000)@87+4
                  type="IEND"@91+4
                  body=@95+0
                  crc=0 (0x00000000)@95+4
            """,
        ["png-wrong-signature"] = """
            png_chunks=@0+99
              signature=!Contents@0+8
            """,
        ["png-cut-in-a-chunk"] = """
            png_chunks=@0+43
              signature=89504E470D0A1A0A@0+8
              chunks=!OutOfFile@8+35
            """,
        ["zip-valid"] = """
            zip_archive=@0+113
              sections=@0+113
                sections[0]=@0+40
                  magic=504B@0+2
                  kind=local_file (1027)@2+2
                  body=@4+36
                sections[1]=@40+51
                  magic=504B@40+2
                  kind=central_entry (513)@42+2
                  body=@44+47
                sections[2]=@91+22
                  magic=504B@91+2
                  kind=end_of_central_directory (1541)@93+2
                  body=@95+18
            """,
        ["zip-wrong-magic"] = """
            zip_archive=@0+113
              sections=!Contents@0+2
            """,
        ["zip-cut-in-the-central-directory"] = """
            zip_archive=@0+70
              sections=!OutOfFile@0+70
            """,
        ["pe-valid"] = """
            pe_file=@0+536
              magic=4D5A@0+2
              dos_header=00000000000000000000000000000000 …@2+58
              pe_offset=64 (0x00000040)@60+4
              pe=@64+344
                signature=50450000@64+4
                machine=amd64 (34404)@68+2
                section_count=2 (0x0002)@70+2
                time_date_stamp=1593835520 (0x5F000000)@72+4
                symbol_table_offset=0 (0x00000000)@76+4
                symbol_count=0 (0x00000000)@80+4
                optional_header_size=240 (0x00F0)@84+2
                characteristics=@86+2
                optional_header=@88+240
                sections=@328+80
            """,
        ["pe-wrong-signature"] = """
            pe_file=@0+536
              magic=4D5A@0+2
              dos_header=00000000000000000000000000000000 …@2+58
              pe_offset=64 (0x00000040)@60+4
              pe=!Contents@64+4
            """,
        ["pe-cut-in-the-optional-header"] = """
            pe_file=@0+300
              magic=4D5A@0+2
              dos_header=00000000000000000000000000000000 …@2+58
              pe_offset=64 (0x00000040)@60+4
              pe=!OutOfFile@64+236
            """,
        ["pe-more-sections-than-fit"] = """
            pe_file=@0+536
              magic=4D5A@0+2
              dos_header=00000000000000000000000000000000 …@2+58
              pe_offset=64 (0x00000040)@60+4
              pe=!OutOfFile@64+472
            """,
        ["elf64-valid"] = """
            elf_file=@0+280
              magic=7F454C46@0+4
              bits=b64 (2)@4+1
              endian=le (1)@5+1
              ident_version=1@6+1
              os_abi=linux (3)@7+1
              abi_version=0@8+1
              padding=00000000000000@9+7
              header=@16+48
                file_type=executable (2)@16+2
                machine=x86_64 (62)@18+2
                version=1 (0x00000001)@20+4
                entry_point=4198400 (0x0000000000401000)@24+8
                program_header_offset=64 (0x0000000000000040)@32+8
                section_header_offset=120 (0x0000000000000078)@40+8
                flags=0 (0x00000000)@48+4
                header_size=64 (0x0040)@52+2
                program_header_size=56 (0x0038)@54+2
                program_header_count=1 (0x0001)@56+2
                section_header_size=64 (0x0040)@58+2
                section_header_count=2 (0x0002)@60+2
                section_names_index=0 (0x0000)@62+2
                program_headers=@64+56
                  program_headers[0]=@64+56
                section_headers=@120+128
                  section_headers[0]=@120+64
                  section_headers[1]=@184+64
            """,
        ["elf32-big-endian-valid"] = """
            elf_file=@0+84
              magic=7F454C46@0+4
              bits=b32 (1)@4+1
              endian=be (2)@5+1
              ident_version=1@6+1
              os_abi=system_v (0)@7+1
              abi_version=0@8+1
              padding=00000000000000@9+7
              header=@16+36
                file_type=executable (2)@16+2
                machine=powerpc (20)@18+2
                version=1 (0x00000001)@20+4
                entry_point=268435456 (0x10000000)@24+4
                program_header_offset=52 (0x00000034)@28+4
                section_header_offset=0 (0x00000000)@32+4
                flags=0 (0x00000000)@36+4
                header_size=52 (0x0034)@40+2
                program_header_size=32 (0x0020)@42+2
                program_header_count=1 (0x0001)@44+2
                section_header_size=40 (0x0028)@46+2
                section_header_count=0 (0x0000)@48+2
                section_names_index=0 (0x0000)@50+2
                program_headers=@52+32
                  program_headers[0]=@52+32
                section_headers=@0+0
            """,
        ["elf-unknown-byte-order"] = """
            elf_file=@0+280
              magic=7F454C46@0+4
              bits=b64 (2)@4+1
              endian=3@5+1
              ident_version=1@6+1
              os_abi=linux (3)@7+1
              abi_version=0@8+1
              padding=00000000000000@9+7
              header=!Expression@16+0
            """,
        ["elf-cut-in-the-header"] = """
            elf_file=@0+30
              magic=7F454C46@0+4
              bits=b64 (2)@4+1
              endian=le (1)@5+1
              ident_version=1@6+1
              os_abi=linux (3)@7+1
              abi_version=0@8+1
              padding=00000000000000@9+7
              header=!OutOfFile@16+14
            """,
        ["elf-more-section-headers-than-the-limit"] = """
            elf_file=@0+280
              magic=7F454C46@0+4
              bits=b64 (2)@4+1
              endian=le (1)@5+1
              ident_version=1@6+1
              os_abi=linux (3)@7+1
              abi_version=0@8+1
              padding=00000000000000@9+7
              header=@16+48
                file_type=executable (2)@16+2
                machine=x86_64 (62)@18+2
                version=1 (0x00000001)@20+4
                entry_point=4198400 (0x0000000000401000)@24+8
                program_header_offset=64 (0x0000000000000040)@32+8
                section_header_offset=120 (0x0000000000000078)@40+8
                flags=0 (0x00000000)@48+4
                header_size=64 (0x0040)@52+2
                program_header_size=56 (0x0038)@54+2
                program_header_count=1 (0x0001)@56+2
                section_header_size=64 (0x0040)@58+2
                section_header_count=65535 (0xFFFF)@60+2
                section_names_index=0 (0x0000)@62+2
                program_headers=@64+56
                  program_headers[0]=@64+56
                section_headers=!LimitRepeat@120+0
            """,
        ["stl-valid"] = """
            stl_binary=@0+184
              header=736F6C69642074657374000000000000 …@0+80
              triangle_count=2 (0x00000002)@80+4
              triangles=@84+100
                triangles[0]=@84+50
                  normal=@84+12
                  vertices=@96+36
                  attribute_bytes=0 (0x0000)@132+2
                triangles[1]=@134+50
                  normal=@134+12
                  vertices=@146+36
                  attribute_bytes=0 (0x0000)@182+2
            """,
        ["stl-one-triangle-missing"] = """
            stl_binary=@0+184
              header=736F6C69642074657374000000000000 …@0+80
              triangle_count=3 (0x00000003)@80+4
              triangles=!OutOfFile@84+100
            """,
        ["stl-count-over-the-limit"] = """
            stl_binary=@0+184
              header=736F6C69642074657374000000000000 …@0+80
              triangle_count=4294967295 (0xFFFFFFFF)@80+4
              triangles=!LimitRepeat@84+0
            """,
    };
}
