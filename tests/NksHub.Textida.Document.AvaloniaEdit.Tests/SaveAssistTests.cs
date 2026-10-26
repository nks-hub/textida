using System.Text;
using AvaloniaEdit.Document;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

public class SaveAssistTests
{
    private static (Doc Doc, TextDocument Text) New(string text)
    {
        var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        return (doc, ((AvaloniaEditBackend)doc.Backend).TextDocument);
    }

    [Theory]
    [InlineData("a  \nb\t\nc", "a\nb\nc", true)]
    [InlineData("a\r\nb \r\n", "a\r\nb\r\n", true)]
    [InlineData("clean\n", "clean\n", false)]
    [InlineData("   \n\t\n", "\n\n", true)]
    [InlineData("", "", false)]
    public void Trailing_whitespace_is_removed_and_line_endings_stay(string input, string expected, bool changes)
    {
        var (doc, text) = New(input);
        using var _ = doc;
        bool changed;
        using (doc.BeginUndoGroup()) changed = SaveAssists.TrimTrailingWhitespace(text);
        Assert.Equal(changes, changed);
        Assert.Equal(expected, text.Text);
        if (changes)
        {
            doc.Undo();
            Assert.Equal(input, text.Text);
            Assert.False(doc.CanUndo);
        }
    }

    [Theory]
    [InlineData("a", "\n", "a\n", true)]
    [InlineData("a\n", "\n", "a\n", false)]
    [InlineData("a\r\n", "\r\n", "a\r\n", false)]
    [InlineData("a", "\r\n", "a\r\n", true)]
    [InlineData("", "\n", "", false)]
    public void Final_line_break_is_added_only_when_missing(string input, string newLine, string expected, bool changes)
    {
        var (doc, text) = New(input);
        using var _ = doc;
        Assert.Equal(changes, SaveAssists.EnsureFinalNewline(text, newLine));
        Assert.Equal(expected, text.Text);
    }

    private static readonly IndentSettings Default = new(true, 4, 4);

    [Theory]
    [InlineData("a\n  b\n    c\n  d\ne\n  f\n", true, 2)]
    [InlineData("a\n    b\n        c\n    d\n", true, 4)]
    [InlineData("a\n   b\n      c\n", true, 3)]
    [InlineData("a\n\tb\n\t\tc\n\td\n", false, 4)]
    [InlineData("a\r\n  b\r\n    c\r\n", true, 2)]
    public void Indentation_is_read_from_the_file(string text, bool spaces, int size)
    {
        var detected = IndentDetector.Detect(text, Default);
        Assert.NotNull(detected);
        Assert.Equal(spaces, detected!.InsertSpaces);
        Assert.Equal(size, detected.IndentSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no indentation\nat all\n")]
    [InlineData(" one space only\n")]
    public void Nothing_is_detected_without_indented_lines(string text) => Assert.Null(IndentDetector.Detect(text, Default));
}
