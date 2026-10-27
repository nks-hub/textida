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
