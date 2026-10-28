using NksHub.Textida.Core.AiContext;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.9.1: the block for an AI assistant (Markdown or XML) and the folder walk behind it; values of a <c>.env</c> file never get in.</summary>
public class AiContextTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "textida-ai-" + Guid.NewGuid().ToString("N"));

    public AiContextTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static AiSource Source(string name, string text, string? path = null, long? first = 3, bool unsaved = false) =>
        new(name, path ?? name, "csharp", "UTF-8", 12, unsaved, [new AiRange(text, first, first + AiContextBuilder.CountLines(text) - 1)]);

    [Fact]
    public void Markdown_names_path_lines_language_encoding_revision_and_unsaved_changes()
    {
        var result = AiContextBuilder.Build([Source("src/A.cs", "int a;\nint b;\n", unsaved: true)], [], AiContextFormat.Markdown);
        Assert.Equal("# Context\n\n## src/A.cs\n- language: csharp\n- encoding: UTF-8\n- revision: 12\n- unsaved changes: yes\n\n### Lines 3-4\n\n```csharp\nint a;\nint b;\n```\n", result.Text);
        Assert.Equal(1, result.Files);
    }

    [Fact]
    public void A_fence_is_longer_than_any_run_of_backticks_in_the_text()
    {
        var result = AiContextBuilder.Build([Source("a.md", "```cs\nx\n```")], [], AiContextFormat.Markdown);
        Assert.Contains("\n````csharp\n```cs\nx\n```\n````\n", result.Text);
    }

    [Fact]
    public void Xml_escapes_attributes_and_keeps_a_cdata_end_inside_the_text()
    {
        var result = AiContextBuilder.Build([Source("a&\"b.cs", "x ]]> y")], [], AiContextFormat.Xml);
        Assert.Contains("<file path=\"a&amp;&quot;b.cs\" language=\"csharp\" encoding=\"UTF-8\" revision=\"12\">", result.Text);
        var doc = System.Xml.Linq.XDocument.Parse(result.Text);
        Assert.Equal("x ]]> y", doc.Descendants("lines").Single().Value);
    }

    [Fact]
    public void A_secrets_file_is_left_out_whatever_the_caller_passes_and_the_reason_is_named()
    {
        var env = Source(".env", "TOKEN=hunter2\n");
        foreach (var format in new[] { AiContextFormat.Markdown, AiContextFormat.Xml })
        {
            var result = AiContextBuilder.Build([env, Source("prod.env", "K=hunter2"), Source("ok.cs", "int a;")], [], format);
            Assert.DoesNotContain("hunter2", result.Text);
            Assert.Equal(1, result.Files);
            Assert.Equal([AiOmitReason.SecretFile, AiOmitReason.SecretFile], result.Omissions.Select(o => o.Reason));
            Assert.Contains("secrets", result.Text);
        }
    }

    [Fact]
    public void A_selection_without_a_line_index_says_so_and_counts_lines_by_line_breaks()
    {
        var source = new AiSource("big.log", "big.log", "log", "UTF-8", 3, false, [new AiRange("part")]);
        Assert.Contains("### Lines ?", AiContextBuilder.Build([source], [], AiContextFormat.Markdown).Text);
        Assert.Equal(1, AiContextBuilder.CountLines(""));
        Assert.Equal(2, AiContextBuilder.CountLines("a\r\nb"));
        Assert.Equal(2, AiContextBuilder.CountLines("a\nb\n"));
    }

    [Fact]
    public void A_source_past_the_size_limit_is_named_and_a_smaller_one_after_it_still_fits_in_both_formats()
    {
        // "ž" is two UTF-8 bytes: the limit counts UTF-8 bytes of the text, not characters
        var sources = new[] { Source("a.cs", "žžžžž"), Source("b.cs", "0123456789"), Source("c.cs", "abc") };
        foreach (var format in new[] { AiContextFormat.Markdown, AiContextFormat.Xml })
        {
            var result = AiContextBuilder.Build(sources, [], format, maxTextBytes: 13);
            Assert.Equal(2, result.Files);
            Assert.Contains("žžžžž", result.Text);
            Assert.Contains("abc", result.Text);
            Assert.DoesNotContain("0123456789", result.Text);
            Assert.Equal(new AiOmission(AiOmitReason.TotalLimit, "b.cs"), Assert.Single(result.Omissions));
            Assert.Contains("size-limit", result.Text);
        }
        Assert.Equal(3, AiContextBuilder.Build(sources, [], AiContextFormat.Markdown, maxTextBytes: 23).Files);
    }

    [Fact]
    public void The_limit_counts_utf8_bytes_so_five_two_byte_characters_do_not_fit_in_nine()
    {
        var five = new[] { Source("a.cs", "žžžžž") };
        foreach (var format in new[] { AiContextFormat.Markdown, AiContextFormat.Xml })
        {
            var tooSmall = AiContextBuilder.Build(five, [], format, maxTextBytes: 9);
            Assert.Equal(0, tooSmall.Files);
            Assert.Equal(new AiOmission(AiOmitReason.TotalLimit, "a.cs"), Assert.Single(tooSmall.Omissions));
            Assert.DoesNotContain("žžžžž", tooSmall.Text);
            Assert.Equal(1, AiContextBuilder.Build(five, [], format, maxTextBytes: 10).Files);
        }
    }

    [Fact]
    public void Stored_bytes_rule_a_text_out_only_when_even_utf32_could_not_fit()
    {
        Assert.False(AiContextBuilder.CannotFit(4 + 4 * 10, 10)); // BOM and ten UTF-32 characters are ten UTF-8 bytes
        Assert.True(AiContextBuilder.CannotFit(4 + 4 * 11, 10));
        Assert.False(AiContextBuilder.CannotFit(0, 0));
    }

    [Fact]
    public void A_folder_keeps_only_what_fits_in_the_size_limit_and_does_not_read_a_file_that_cannot_fit()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "12345");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "1234567890");
        File.WriteAllText(Path.Combine(_root, "c.txt"), "123");
        var huge = Path.Combine(_root, "huge.txt");
        File.WriteAllText(huge, new string('x', 1000));
        // held open without sharing: reading it would report it as unreadable, so the reason shows it was never read
        using var held = new FileStream(huge, FileMode.Open, FileAccess.Read, FileShare.None);

        var folder = AiContextFolder.Collect(_root, _ => "plaintext", maxTextBytes: 9);
        Assert.Equal(["a.txt", "c.txt"], folder.Sources.Select(s => s.Name).Order());
        Assert.Contains(folder.Omissions, o => o is { Reason: AiOmitReason.TotalLimit, Name: null, Count: 2 });
        Assert.DoesNotContain(folder.Omissions, o => o.Reason == AiOmitReason.Unreadable);
    }

    [Fact]
    public void A_folder_gives_text_files_and_names_what_it_left_out_with_the_reason()
    {
        File.WriteAllText(Path.Combine(_root, "a.cs"), "class A { }\n");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "b.txt"), "příliš žluťoučký\n");
        File.WriteAllText(Path.Combine(_root, ".env"), "TOKEN=hunter2\n");
        File.WriteAllText(Path.Combine(_root, ".env.local"), "TOKEN=hunter3\n");
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "skip.txt\n");
        File.WriteAllText(Path.Combine(_root, "skip.txt"), "ignored");
        File.WriteAllBytes(Path.Combine(_root, "data.bin"), [1, 2, 0, 3]);
        Directory.CreateDirectory(Path.Combine(_root, ".cache"));
        File.WriteAllText(Path.Combine(_root, ".cache", "x.txt"), "hidden");

        var folder = AiContextFolder.Collect(_root, _ => "plaintext");
        Assert.Equal(["a.cs", "sub/b.txt"], folder.Sources.Where(s => s.Name != ".gitignore").Select(s => s.Name).Order());
        Assert.Contains(folder.Omissions, o => o is { Reason: AiOmitReason.SecretFile, Name: ".env" });
        Assert.Contains(folder.Omissions, o => o is { Reason: AiOmitReason.SecretFile, Name: ".env.local" });
        Assert.Contains(folder.Omissions, o => o is { Reason: AiOmitReason.Binary, Name: "data.bin" });
        Assert.Contains(folder.Omissions, o => o is { Reason: AiOmitReason.IgnoreRules, Count: 1 });
        Assert.Contains(folder.Omissions, o => o is { Reason: AiOmitReason.Hidden, Count: >= 2 });

        var result = AiContextBuilder.Build(folder.Sources, folder.Omissions, AiContextFormat.Markdown);
        Assert.DoesNotContain("hunter", result.Text);
        Assert.DoesNotContain("ignored", result.Text);
        Assert.Contains("příliš žluťoučký", result.Text);
        Assert.Contains("- .env: secrets", result.Text);
        Assert.Contains("- data.bin: binary", result.Text);
    }
}
