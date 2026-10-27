using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Secrets;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

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
