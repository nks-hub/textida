using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Secrets;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>The list of secret file names is process-wide, so the tests that read or change it run alone.</summary>
[CollectionDefinition("SecretPatterns", DisableParallelization = true)]
public class SecretPatternsCollection;

[Collection("SecretPatterns")]
public class SecretValuesTests
{
    private static string[] Values(string text) => [.. SecretValues.Find(text).Select(s => text.Substring(s.Start, s.Length))];

    [Theory]
    [InlineData(".env", true)]
    [InlineData(".ENV", true)]
    [InlineData("C:\\work\\app\\.env.local", true)]
    [InlineData("/srv/app/prod.env", true)]
    [InlineData("environment.txt", false)]
    [InlineData("env", false)]
    [InlineData("settings.json", false)]
    [InlineData(null, false)]
    public void Variable_files_are_recognised_by_name(string? path, bool secret) => Assert.Equal(secret, SecretValues.IsSecretFile(path));

    [Fact]
    public void The_names_come_from_the_setting_whose_default_is_the_old_list_and_an_empty_list_hides_nothing()
    {
        string before = SecretValues.FilePatterns;
        try
        {
            Assert.Equal(".env;.env.*;*.env", SecretValues.DefaultFilePatterns);
            Assert.Equal(SecretValues.DefaultFilePatterns, SecretValues.FilePatterns);

            SecretValues.FilePatterns = SecretValues.DefaultFilePatterns + "; Secrets.* ;vault?.json;;";
            Assert.Equal(".env;.env.*;*.env;Secrets.*;vault?.json", SecretValues.FilePatterns);
            Assert.True(SecretValues.IsSecretFile("C:\\app\\secrets.yaml"));
            Assert.True(SecretValues.IsSecretFile("/srv/VAULT1.json"));
            Assert.False(SecretValues.IsSecretFile("vault10.json"));
            Assert.False(SecretValues.IsSecretFile("/srv/secrets/readme.txt")); // only the name counts, not the folder
            Assert.True(SecretValues.IsSecretFile(".env.local"));

            SecretValues.FilePatterns = "*.vault";
            Assert.False(SecretValues.IsSecretFile(".env"));
            Assert.True(SecretValues.IsSecretFile("prod.vault"));

            SecretValues.FilePatterns = "";
            Assert.False(SecretValues.IsSecretFile(".env"));
        }
        finally { SecretValues.FilePatterns = before; }
        Assert.True(SecretValues.IsSecretFile(".env"));
    }

    [Fact]
    public void The_secrets_of_a_stretch_of_bytes_become_the_character_spans_of_its_text()
    {
        var bytes = Encoding.UTF8.GetBytes("A=héslo\nB=2\n"); // é takes two bytes: the secrets are bytes, the spans characters
        using var doc = Doc.CreateVirtual(".env", bytes, PagedBackendFactory.Instance, TextCodec.Utf8);
        var hidden = SecretValues.HiddenRanges(doc.GetSnapshot(), 0, 1 << 20);
        Assert.Equal([new ByteRange(2, 8), new ByteRange(11, 12)], hidden);

        Assert.Equal([new SecretSpan(2, 5), new SecretSpan(10, 1)], SecretValues.SpansIn(TextCodec.Utf8.Decode(bytes), 0, hidden));
        // a line cut out of the file knows where it starts
        Assert.Equal([new SecretSpan(2, 1)], SecretValues.SpansIn(TextCodec.Utf8.Decode(bytes.AsMemory(9, 3)), 9, hidden));
        // a stretch that starts inside the first value holds the rest of it
        Assert.Equal([new SecretSpan(0, 3)], SecretValues.SpansIn(TextCodec.Utf8.Decode(bytes.AsMemory(5, 6)), 5, hidden));
        Assert.Empty(SecretValues.SpansIn(TextCodec.Utf8.Decode(bytes.AsMemory(0, 2)), 0, hidden));
        Assert.Equal("A=••••\nB=••••\n", SecretValues.Mask("A=héslo\nB=2\n", SecretValues.SpansIn(TextCodec.Utf8.Decode(bytes), 0, hidden)));
    }

    [Fact]
    public void A_file_past_the_scan_limit_is_hidden_whole_after_its_byte_order_mark()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("# note\nK=value\n")).ToArray();
        using var doc = Doc.CreateVirtual(".env", bytes, PagedBackendFactory.Instance, TextCodec.Utf8);
        Assert.Equal([new ByteRange(3, bytes.Length)], SecretValues.HiddenRanges(doc.GetSnapshot(), 3, 8));
        Assert.Equal([new ByteRange(12, 17)], SecretValues.HiddenRanges(doc.GetSnapshot(), 3, 1 << 20));
    }

    [Fact]
    public void A_masked_snapshot_reads_stars_for_the_values_across_every_read_and_leaves_the_document_alone()
    {
        var bytes = Encoding.UTF8.GetBytes("K=secret\nL=x\n");
        using var doc = Doc.CreateVirtual(".env", bytes, PagedBackendFactory.Instance, TextCodec.Utf8);
        var snapshot = doc.GetSnapshot();
        var masked = new MaskedSnapshot(snapshot, SecretValues.HiddenRanges(snapshot, 0, 1 << 20), TextCodec.Utf8);

        Assert.Equal(bytes.Length, masked.ByteLength);
        var all = new byte[bytes.Length];
        Assert.Equal(bytes.Length, masked.Read(0, all));
        Assert.Equal("K=******\nL=*\n", Encoding.UTF8.GetString(all));
        var part = new byte[4]; // starts inside the first value and ends at its end
        Assert.Equal(4, masked.Read(4, part));
        Assert.Equal("****", Encoding.UTF8.GetString(part));
        Assert.Equal(bytes, doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
    }

    [Fact]
    public async Task Search_previews_of_an_open_document_without_a_file_follow_its_name()
    {
        using var doc = Doc.CreateVirtual(".env", Encoding.UTF8.GetBytes("API_KEY=hunter2\n"), PagedBackendFactory.Instance, TextCodec.Utf8);
        var settings = new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false, Walk = new FileWalkOptions { IncludeHidden = true } };
        var (hits, _) = await SearchWorker.CollectAsync(new SearchPattern("hunter2"), new SearchScope([], [LiveBuffer.Of(doc)]), settings);
        Assert.Equal("API_KEY=••••", hits.Single().Preview);
    }

    [Fact]
    public void Values_are_found_for_every_form_of_a_variable()
    {
        Assert.Equal(["hunter2"], Values("API_KEY=hunter2\n"));
        Assert.Equal(["a b"], Values("export  K = a b  \r\nOTHER=\r\n"));
        Assert.Equal(["\"abc # def\"", "'x'"], Values("A=\"abc # def\" # note\nB='x'\n"));
        Assert.Equal(["v"], Values("# not=secret\n  \nK=v # trailing\n"));
        Assert.Equal(["#abc"], Values("K=#abc\n"));
        Assert.Equal([], Values("EMPTY=\nNOEQUALS\n"));
    }

    [Fact]
    public void A_quoted_value_may_span_lines_and_an_unclosed_quote_hides_the_rest()
    {
        const string text = "K=\"-----BEGIN\nabc==\n-----END\"\nNEXT=1\n";
        Assert.Equal(["\"-----BEGIN\nabc==\n-----END\"", "1"], Values(text));
        Assert.Equal(["\"open\nlines=1\n"], Values("K=\"open\nlines=1\n"));
    }

    [Fact]
    public void Masking_replaces_each_value_by_one_placeholder_whatever_its_length_and_keeps_the_rest()
    {
        const string text = "A=secret\nB=\"x y\"\n";
        var spans = SecretValues.Find(text);
        Assert.Equal("A=••••\nB=••••\n", SecretValues.Mask(text, spans));
        // a piece of the text: it starts inside the first value, so only the placeholder is left of it
        Assert.Equal("••••\nB=••••\n", SecretValues.Mask(text[4..], spans, 4));
        Assert.Equal("A=", SecretValues.Mask("A=", spans));
    }

    [Fact]
    public void A_line_seen_alone_hides_values_and_what_may_be_the_rest_of_one()
    {
        Assert.Equal("K=••••", SecretValues.MaskLine("K=secret"));
        Assert.Equal("export K=••••", SecretValues.MaskLine("export K=\"a b\""));
        Assert.Equal("# note", SecretValues.MaskLine("# note"));
        Assert.Equal("EMPTY=", SecretValues.MaskLine("EMPTY="));
        Assert.Equal("", SecretValues.MaskLine(""));
        Assert.Equal("••••", SecretValues.MaskLine("lines hunter2\""));
    }

    [Fact]
    public void Overlap_counts_a_touch_but_not_the_boundary_of_a_value()
    {
        var spans = SecretValues.Find("K=value\n");
        Assert.True(SecretValues.Overlaps(spans, 6, 9));
        Assert.True(SecretValues.Overlaps(spans, 4, 4));
        Assert.False(SecretValues.Overlaps(spans, 2, 2));
        Assert.False(SecretValues.Overlaps(spans, 7, 7));
        Assert.False(SecretValues.Overlaps(spans, 0, 2));
        Assert.Equal(new SecretSpan(2, 5), SecretValues.SpanAt(spans, 3));
        Assert.Null(SecretValues.SpanAt(spans, 1));
    }

    [Fact]
    public async Task Search_previews_of_a_secrets_file_hide_the_values_whether_it_is_on_disk_or_open()
    {
        var dir = Path.Combine(Path.GetTempPath(), "textida-secret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string content = "DB_PASSWORD=hunter2\nNOTE=\"two\nlines hunter2\"\n# hunter2 in a comment\n";
            File.WriteAllText(Path.Combine(dir, ".env"), content);
            File.WriteAllText(Path.Combine(dir, "readme.txt"), content);
            var settings = new SearchSettings { MaxResults = int.MaxValue, StopAtLimit = false, Walk = new FileWalkOptions { IncludeHidden = true } };

            var (hits, _) = await SearchWorker.CollectAsync(new SearchPattern("hunter2"), new SearchScope([dir]), settings);
            var env = hits.Where(h => h.Source.Path!.EndsWith(".env")).OrderBy(h => h.Line).Select(h => h.Preview).ToList();
            Assert.Equal(["DB_PASSWORD=••••", "••••", "# hunter2 in a comment"], env);
            Assert.Contains(hits, h => h.Source.Path!.EndsWith("readme.txt") && h.Preview == "DB_PASSWORD=hunter2");

            // the text of a pattern that is not a plain literal goes through the same filter, and so does an open document
            var (regex, _) = await SearchWorker.CollectAsync(new SearchPattern(@"PASS\w+=\w+", IsRegex: true), new SearchScope([dir]), settings);
            Assert.Equal("DB_PASSWORD=••••", regex.Single(h => h.Source.Path!.EndsWith(".env")).Preview);
            using var doc = Doc.Open(Path.Combine(dir, ".env"), PagedBackendFactory.Instance);
            var (live, _) = await SearchWorker.CollectAsync(new SearchPattern("hunter2"), new SearchScope([], [LiveBuffer.Of(doc)]), settings);
            Assert.Equal(["DB_PASSWORD=••••", "••••", "# hunter2 in a comment"], live.OrderBy(h => h.Line).Select(h => h.Preview));
        }
        finally { Directory.Delete(dir, true); }
    }
}
