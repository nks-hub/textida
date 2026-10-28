using NksHub.Textida.Core.Hex.Ksy;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8.10.5 and P8.10.1: the list of pinned templates, a valid and a damaged file for each, the details of their trees, and the tree of a ZIP of ten gigabytes.</summary>
public class KsyTemplatesTests
{
    private static string Lines(string text) => text.ReplaceLineEndings("\n").TrimEnd('\n');

    private static KsyNode Child(KsyNode node, string name) => node.Children.Single(c => c.Name == name);

    // ---- the list ----

    [Fact]
    public void Every_pinned_template_parses_and_has_a_version_a_source_and_an_allowed_licence()
    {
        Assert.Empty(KsyCatalog.Problems);
        Assert.NotEmpty(KsyCatalog.All);
        foreach (var p in KsyCatalog.All)
        {
            Assert.Contains(p.License, KsyCatalog.AllowedLicenses);
            Assert.NotEqual("", p.Version);
            Assert.NotEqual("", p.Source);
            Assert.Equal(p.Id, p.Template.Id);
        }
        Assert.Equal(KsyCatalog.All.Count, KsyCatalog.All.Select(p => p.Id).Distinct().Count());
        Assert.DoesNotContain(KsyCatalog.AllowedLicenses, l => l.StartsWith("GPL", StringComparison.Ordinal) || l.StartsWith("LGPL", StringComparison.Ordinal));
    }

    [Fact]
    public void The_list_names_every_template_file_of_the_program_and_nothing_else()
    {
        var embedded = typeof(KsyCatalog).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Ksy/", StringComparison.Ordinal) && n.EndsWith(".ksy", StringComparison.Ordinal))
            .Select(n => n["Ksy/".Length..]);
        Assert.Equal(embedded.Order(StringComparer.Ordinal), KsyCatalog.All.Select(p => p.File).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_constructs_the_list_gives_for_a_template_are_the_ones_its_file_uses()
    {
        foreach (var p in KsyCatalog.All)
            Assert.Equal(p.Template.Constructs.Order(StringComparer.Ordinal), p.Constructs.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_subset_of_the_parser_is_the_union_of_the_constructs_of_the_pinned_templates()
    {
        var union = KsyCatalog.All.SelectMany(p => p.Constructs).ToHashSet();
        Assert.Equal(KsyParser.Constructs.Order(StringComparer.Ordinal), union.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Every_pinned_template_has_a_valid_file_and_a_damaged_one()
    {
        foreach (var p in KsyCatalog.All)
        {
            var own = KsyPinnedSamples.All.Where(s => s.TemplateId == p.Id).ToList();
            Assert.Contains(own, s => s.Name.EndsWith("-valid", StringComparison.Ordinal));
            Assert.Contains(own, s => !s.Name.EndsWith("-valid", StringComparison.Ordinal));
        }
        Assert.All(KsyPinnedSamples.All, s => Assert.NotNull(KsyCatalog.ById(s.TemplateId)));
    }

    // ---- the files ----

    public static IEnumerable<object[]> PinnedNames() => KsyPinnedSamples.All.Select(s => new object[] { s.Name });

    [Theory, MemberData(nameof(PinnedNames))]
    public void A_pinned_file_reads_into_the_expected_fields_with_their_byte_ranges(string name)
    {
        var sample = KsyPinnedSamples.All.Single(s => s.Name == name);
        Assert.Equal(Lines(KsyPinnedSamples.Expected[name]), Lines(KsyPinnedSamples.Describe(sample)));
    }

    [Fact]
    public void Every_pinned_file_has_an_expected_tree_and_a_damaged_file_shows_an_error_on_a_field()
    {
        Assert.Equal(KsyPinnedSamples.All.Select(s => s.Name).Order(), KsyPinnedSamples.Expected.Keys.Order());
        foreach (var s in KsyPinnedSamples.All.Where(s => !s.Name.EndsWith("-valid", StringComparison.Ordinal)))
            Assert.Contains("=!", KsyPinnedSamples.Expected[s.Name]);
        foreach (var s in KsyPinnedSamples.All.Where(s => s.Name.EndsWith("-valid", StringComparison.Ordinal)))
            Assert.DoesNotContain("=!", KsyPinnedSamples.Expected[s.Name]);
    }

    [Fact]
    public void The_catalog_picks_the_template_by_the_first_bytes_then_by_the_extension()
    {
        Assert.Equal("zip_archive", KsyCatalog.Find("data.bin", "PK\u0003\u0004"u8)?.Id);
        Assert.Equal("elf_file", KsyCatalog.Find("a.out", [0x7F, (byte)'E', (byte)'L', (byte)'F', 2])?.Id);
        Assert.Equal("pe_file", KsyCatalog.Find("setup", "MZ"u8)?.Id);
        Assert.Equal("pe_file", KsyCatalog.Find("lib.DLL", [])?.Id);
        Assert.Equal("stl_binary", KsyCatalog.Find("part.stl", "solid x"u8)?.Id);
        Assert.Equal("zip_archive", KsyCatalog.Find("report.docx", [])?.Id);
        Assert.Null(KsyCatalog.Find("notes.txt", "hello"u8));
    }

    [Fact]
    public void A_structure_that_starts_with_the_magic_number_gives_it_to_the_template()
    {
        Assert.Equal("PK"u8.ToArray(), KsyCatalog.ById("zip_archive")!.Template.Magic);
        Assert.Equal(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F' }, KsyCatalog.ById("elf_file")!.Template.Magic);
        Assert.Equal(8, KsyCatalog.ById("png_chunks")!.Template.Magic!.Length);
        Assert.Null(KsyCatalog.ById("stl_binary")!.Template.Magic);
    }

    // ---- details of the trees ----

    [Fact]
    public void The_flags_of_a_zip_entry_are_read_bit_by_bit_with_the_bytes_each_bit_is_in()
    {
        var data = new List<byte>(KsySamples.ZipLocal("a.txt", "hello"u8.ToArray(), 0x490D)); // bits 0, 2, 3, 8, 11 and 14
        data.AddRange(KsySamples.ZipEnd(0, 0, 0));
        var root = KsySamples.Read("zip_archive", [.. data]);
        var flags = Child(root, "sections").Children[0].Children[2].Children[1];
        Assert.Equal(("flags", 6L, 2L), (flags.Name, flags.Start, flags.Length));

        Assert.Equal(["true", "2", "true", "false", "false", "false", "2", "true", "false", "false", "1"], flags.Children.Select(c => c.Value));
        Assert.Equal((6L, 1L), (flags.Children[0].Start, flags.Children[0].Length));
        Assert.Equal((6L, 2L), (flags.Children[6].Start, flags.Children[6].Length)); // unused: bits 7 to 10 span two bytes
        Assert.Equal((7L, 1L), (flags.Children[7].Start, flags.Children[7].Length)); // utf8_names: bit 11
    }

    [Fact]
    public void A_zip_entry_of_four_gigabytes_or_more_takes_its_size_from_the_zip64_extra_field()
    {
        var body = new byte[30];
        var data = new List<byte>(KsySamples.ZipLocalWithExtra("big.bin", body, KsySamples.Zip64Extra(30, 30), 0xFFFFFFFF));
        data.AddRange(KsySamples.ZipEnd(0, 0, 0));
        var sections = Child(KsySamples.Read("zip_archive", [.. data]), "sections");
        Assert.Null(sections.Error);
        Assert.Equal(["local_file (1027)", "end_of_central_directory (1541)"], sections.Children.Select(s => Child(s, "kind").Value));

        var local = Child(sections.Children[0], "body");
        Assert.Equal(("4294967295 (0xFFFFFFFF)", "4294967295 (0xFFFFFFFF)"), (Child(local, "compressed_size").Value, Child(local, "uncompressed_size").Value));
        var zip64 = Child(Child(local, "extra"), "first");
        Assert.Equal(("zip64_sizes", 41L, 16L), (zip64.TypeName, zip64.Start, zip64.Length));
        Assert.Equal(["30 (0x000000000000001E)", "30 (0x000000000000001E)"], zip64.Children.Select(c => c.Value));
        var content = Child(local, "body");
        Assert.Equal((57L, 30L), (content.Start, content.Length));
        Assert.Equal(87L, sections.Children[1].Start);
    }

    [Fact]
    public void A_zip_size_of_ffffffff_without_a_zip64_field_is_an_error_and_other_extra_fields_do_not_change_the_walk()
    {
        var missing = new List<byte>(KsySamples.ZipLocalWithExtra("a", new byte[4], [], 0xFFFFFFFF));
        missing.AddRange(KsySamples.ZipEnd(0, 0, 0));
        Assert.Equal(KsyErrorKind.Expression, Child(KsySamples.Read("zip_archive", [.. missing]), "sections").Error?.Kind);

        byte[][] others = [[0x55, 0x54, 5, 0, 1, 1, 2, 3, 4], [0x55, 0x54], [0x55, 0x54, 200, 0, 1]];
        foreach (var extra in others)
        {
            var data = new List<byte>(KsySamples.ZipLocalWithExtra("a", new byte[4], extra, 4));
            data.AddRange(KsySamples.ZipEnd(0, 0, 0));
            var sections = Child(KsySamples.Read("zip_archive", [.. data]), "sections");
            Assert.Null(sections.Error);
            Assert.Equal(2, sections.Children.Count);
            Assert.Equal(35L + extra.Length, sections.Children[1].Start);
        }
    }

    [Fact]
    public void The_raw_data_of_a_pe_section_is_an_instance_at_the_offset_the_section_header_names()
    {
        var pe = Child(KsySamples.Read("pe_file", KsySamples.Pe()), "pe");
        var sections = Child(pe, "sections").Children;
        var raw = sections.Select(s => Child(s, "raw_data")).ToList();
        Assert.Equal([(512L, 16L), (528L, 8L)], raw.Select(r => (r.Start, r.Length)));
        Assert.Equal(["4096 (0x00001000)", "8192 (0x00002000)"], sections.Select(s => Child(s, "virtual_address").Value));

        var optional = Child(pe, "optional_header");
        var directory = Child(optional, "data_directories").Children[1];
        Assert.Equal([("8192 (0x00002000)", 208L), ("80 (0x00000050)", 212L)], directory.Children.Select(c => (c.Value, c.Start)));
        Assert.Equal(["false", "true", "false", "false", "false", "true", "false", "false"], Child(pe, "characteristics").Children.Take(8).Select(c => c.Value));
    }

    [Fact]
    public void The_dynamic_section_of_an_elf_is_read_up_to_its_end_mark()
    {
        var headers = Child(Child(KsySamples.Read("elf_file", KsySamples.Elf64()), "header"), "section_headers");
        var body = Child(headers.Children[1], "body");
        Assert.Equal(("dynamic_section", 248L, 32L), (body.TypeName, body.Start, body.Length));
        var entries = Child(body, "entries");
        Assert.Equal(("dynamic_entry[2]", 248L, 32L), (entries.TypeName, entries.Start, entries.Length));
        Assert.Equal(["1 (0x0000000000000001)", "0 (0x0000000000000000)"], entries.Children.Select(e => Child(e, "tag").Value));
        Assert.DoesNotContain(headers.Children[0].Children, c => c.Name == "body");
    }

    [Fact]
    public void The_chunks_of_a_png_that_the_template_knows_are_read_into_fields()
    {
        var chunks = Child(KsySamples.Read("png_chunks", KsySamples.Png()), "chunks").Children;
        var header = Child(chunks[0], "body").Children;
        Assert.Equal(["640 (0x00000280)", "480 (0x000001E0)", "8", "truecolor_alpha (6)"], header.Take(4).Select(c => c.Value));

        var gamma = Child(chunks[1], "body").Children;
        Assert.Equal(["45455 (0x0000B18F)", "0.45455"], gamma.Select(c => c.Value));

        var text = Child(chunks[2], "body").Children;
        Assert.Equal([("\"Title\"", 57L, 6L), ("\"Hello\"", 63L, 5L)], text.Select(c => (c.Value, c.Start, c.Length)));
        Assert.Equal("bytes", Child(chunks[3], "body").TypeName);
    }

    [Fact]
    public void An_elf_of_each_byte_order_gives_the_same_numbers()
    {
        var big = Child(KsySamples.Read("elf_file", KsySamples.Elf32Be()), "header").Children.ToDictionary(c => c.Name, c => c.Value);
        Assert.Equal("powerpc (20)", big["machine"]);
        Assert.Equal("268435456 (0x10000000)", big["entry_point"]);
        var little = Child(KsySamples.Read("elf_file", KsySamples.Elf64()), "header").Children.ToDictionary(c => c.Name, c => c.Value);
        Assert.Equal("x86_64 (62)", little["machine"]);
        Assert.Equal("4198400 (0x0000000000401000)", little["entry_point"]);
    }

    // ---- what is chosen per file type, and the files of the user ----

    [Fact]
    public void The_choices_per_file_type_are_written_and_read_back_and_a_bad_entry_is_skipped()
    {
        var choices = KsyCatalog.ParseChoices(" BIN = elf_file ; .dat=png_chunks,, bad, =x, y=");
        Assert.Equal("bin=elf_file, dat=png_chunks", KsyCatalog.FormatChoices(choices));
        Assert.Empty(KsyCatalog.ParseChoices(null));
        Assert.Empty(KsyCatalog.ParseChoices(""));
    }

    [Fact]
    public void A_template_from_a_file_is_read_like_a_pinned_one_and_a_refused_one_names_its_line()
    {
        Assert.True(KsyCatalog.TryParseCustom("meta:\n  id: mine\nseq:\n  - id: a\n    type: u1\n", "mine.ksy", out var pinned, out _));
        Assert.True(pinned!.Custom);
        Assert.Equal(("mine", "mine.ksy"), (pinned.Id, pinned.File));

        Assert.False(KsyCatalog.TryParseCustom("meta:\n  id: x\nseq:\n  - id: a\n    size: 4\n    process: zlib\n", "p.ksy", out _, out var error));
        Assert.Equal((KsyErrorKind.Unsupported, "process zlib", 6), (error!.Kind, error.Subject, error.Line));

        // a template of the user may use the processes the subset has beyond the pinned ones, and says so
        Assert.True(KsyCatalog.TryParseCustom("meta:\n  id: x\nseq:\n  - id: a\n    size: 4\n    process: xor(0x55)\n", "p.ksy", out var xor, out _));
        Assert.Equal(["bytes", "expr", "meta", "process.xor", "seq", "size"], xor!.Constructs.Order(StringComparer.Ordinal)); // the constructs it uses, as the parser lists them
    }
}

/// <summary>Runs alone: GC.GetTotalMemory counts every thread of the process, so tests running beside it would show up as growth.</summary>
[Collection("Process memory")]
public class KsyBigFileTests
{
    [Fact]
    public void The_tree_of_a_zip_of_ten_gigabytes_adds_less_than_one_hundred_mebibytes()
    {
        var zip = new KsySamples.BigZip();
        Assert.True(zip.ByteLength >= 10L << 30, zip.ByteLength.ToString());

        long before = GC.GetTotalMemory(true);
        var reader = new KsyReader(zip, KsyCatalog.ById("zip_archive")!.Template, new KsyLimits(MaxMilliseconds: 0));
        var sections = reader.Root.Children[0];
        Assert.Null(sections.Error);
        Assert.Equal("section[8003]", sections.TypeName);
        Assert.Equal(zip.ByteLength, sections.End);

        var kept = new List<KsyNode>();
        foreach (var group in sections.Children)
        {
            kept.Add(group);
            kept.AddRange(group.Children);
        }
        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(kept);
        GC.KeepAlive(reader);

        Assert.Equal(8003, kept.Count - sections.Children.Count);
        Assert.True(after - before < 100L * 1024 * 1024, $"the tree added {(after - before) >> 20} MiB");
    }

    [Fact]
    public void A_field_in_the_middle_of_a_ten_gigabyte_zip_is_found_without_reading_the_file()
    {
        var zip = new KsySamples.BigZip();
        var sections = new KsyReader(zip, KsyCatalog.ById("zip_archive")!.Template, new KsyLimits(MaxMilliseconds: 0)).Root.Children[0];
        long entrySpan = 40 + zip.BodyLength;
        var path = sections.PathTo(entrySpan * 2000 + 4);
        Assert.Equal(["sections[2000]", "body", "version_needed"], path.Skip(path.Count - 3).Select(n => n.Name));
    }
}
