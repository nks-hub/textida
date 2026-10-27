using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.5.1: the source of the app reaches the bytes of a whole-file action only through the byte inspector.</summary>
public class InspectorGateSourceTests
{
    [Fact]
    public void The_document_actions_that_rewrite_whole_files_are_not_called_from_the_app_outside_the_inspector()
    {
        var forbidden = new Regex(@"\.(ConvertTo|UnifyEol|ApplyPreview|PreviewUnifyEol|PreviewNormalize|PreviewConvert)\(|\bdoc\.Normalize\(|\bDoc\.Normalize\(", RegexOptions.CultureInvariant);
        var allowed = new[] { "MainWindow.Files.cs" }; // PreviewConvert sizes the typed-text loss dialog, which cannot run before the text is typed
        var hits = Directory.EnumerateFiles(Repo.AppSources, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !allowed.Contains(Path.GetFileName(f)))
            .SelectMany(f => File.ReadLines(f).Select((line, i) => (File: Path.GetFileName(f), Line: i + 1, Text: line)))
            .Where(x => forbidden.IsMatch(x.Text) && !x.Text.TrimStart().StartsWith("//") && !x.Text.TrimStart().StartsWith("///"))
            .Select(x => $"{x.File}:{x.Line}: {x.Text.Trim()}").ToList();
        Assert.True(hits.Count == 0, "whole-file actions bypass the inspector: " + string.Join("; ", hits));
    }
}

/// <summary>P5.5.1: the whole-file actions of the window list their ranges and wait for the answer.</summary>
[Collection("LocState")]
public class InspectorGateTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static InspectorGateTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public InspectorGateTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static byte[] DocBytes(DocTab tab) => tab.Doc.Doc.ReadBytes(new(0, tab.Doc.Doc.ByteLength));

    [AvaloniaFact]
    public async Task Apply_editorconfig_converts_charset_and_line_endings_each_through_the_inspector()
    {
        _temp.File(".editorconfig", "root = true\n[*.txt]\ncharset = latin1\nend_of_line = crlf\n");
        var path = _temp.Bytes("a.txt", Encoding.UTF8.GetBytes("é\nb\n"));
        var tab = _w.OpenFile(path, null);
        var original = DocBytes(tab);

        _dialogs.Answers.Enqueue(1); // cancel the charset step: nothing may change
        Assert.False(await _w.ApplyEditorConfigFormatAsync(tab));
        Assert.Equal(original, DocBytes(tab));
        Assert.Equal(Loc.Get(K.InspectTitle), Assert.Single(_dialogs.Asked).Title);
        Assert.Contains("Windows-1252", _dialogs.Asked[0].Message);

        _dialogs.Answers.Enqueue(0);
        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.ApplyEditorConfigFormatAsync(tab));
        Assert.Equal(new byte[] { 0xE9, 0x0D, 0x0A, 0x62, 0x0D, 0x0A }, DocBytes(tab));
        Assert.Equal(TextCodec.Cp1252, tab.Doc.Doc.Codec);
        Assert.Equal(3, _dialogs.Asked.Count); // the cancelled run, then the charset and the line endings

        Assert.False(await _w.ApplyEditorConfigFormatAsync(tab)); // already as configured: no dialog, a message
        Assert.Equal(3, _dialogs.Asked.Count);
    }

    [AvaloniaFact]
    public async Task A_file_without_editorconfig_properties_is_left_alone_with_a_message()
    {
        var tab = _w.OpenFile(_temp.File("plain.txt", "x\n"), null);
        Assert.False(await _w.ApplyEditorConfigFormatAsync(tab));
        Assert.Empty(_dialogs.Asked);
        Assert.Contains(Loc.Get(K.InspectEditorConfigNone), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task The_hex_changes_dialog_lists_the_same_ranges_as_the_core_report()
    {
        var path = _temp.Bytes("h.bin", Enumerable.Range(0, 64).Select(i => (byte)i).ToArray());
        var tab = _w.OpenFile(path, null);
        _w.SetHex(tab.Doc, true);
        var hex = tab.Doc.Views.First(v => v.Hex is not null);
        Assert.True(hex.Hex!.Editor.Write(new(4, 6), [0xAA, 0xBB]).Applied);
        _dialogs.Answers.Enqueue(1); // close
        await _w.ShowHexChangesAsync(hex);
        var ask = Assert.Single(_dialogs.Asked);
        var line = Assert.Single(ask.Details!);
        Assert.Contains("AA BB", line);
    }
}
