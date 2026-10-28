using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.6.5 in the window: Insert Image writes the tag with the size of the picture, Insert Link wraps the selection.</summary>
[Collection("LocState")]
public class HtmlInsertAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public HtmlInsertAppTests()
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

    /// <summary>A real PNG written by the imaging library, so the header reader is checked against a file it did not produce.</summary>
    private string Png(string name, int width, int height)
    {
        string path = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var bitmap = new RenderTargetBitmap(new PixelSize(width, height))) bitmap.Save(path);
        using var decoded = new Bitmap(path);
        Assert.Equal(new PixelSize(width, height), decoded.PixelSize);
        return path;
    }

    private DocTab Open(string content, params (int Start, int End)[] selections)
    {
        var tab = _w.OpenFile(_temp.File("page.html", content), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        if (selections.Length == 0) tab.Editor.CaretOffset = tab.Editor.Document.TextLength;
        else tab.Editor.SetCarets(AdapterCarets.Create([.. selections.Select(s => new Sel(s.Start, s.End))], selections.Length - 1));
        return tab;
    }

    private static string Text(DocTab tab) => tab.Editor!.Document.Text;

    [AvaloniaFact]
    public async Task Insert_image_writes_the_tag_with_the_size_of_the_picture_as_one_undo_step()
    {
        string png = Png("img/pic one.png", 37, 21);
        var tab = Open("<p>");
        int depth = tab.Doc.Doc.UndoDepth;
        _dialogs.Picked.Add(png);

        await _w.HtmlInsertImageAsync();

        Assert.Equal("<p><img src=\"img/pic%20one.png\" width=\"37\" height=\"21\" alt=\"\">", Text(tab));
        Assert.Equal(Text(tab).Length - 2, tab.Editor!.CaretOffset); // between the quotes of the empty alt
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
        using var file = File.OpenRead(png);
        Assert.True(HtmlInsert.TryImageSize(file, out int width, out int height));
        Assert.Equal((37, 21), (width, height));
        _w.Undo();
        Assert.Equal("<p>", Text(tab));
    }

    [AvaloniaFact]
    public async Task A_selected_text_on_one_line_becomes_the_alt_text()
    {
        string png = Png("a.png", 5, 6);
        var tab = Open("<p>photo</p>", (3, 8));
        _dialogs.Picked.Add(png);

        await _w.HtmlInsertImageAsync();

        Assert.Equal("<p><img src=\"a.png\" width=\"5\" height=\"6\" alt=\"photo\"></p>", Text(tab));
    }

    [AvaloniaFact]
    public async Task A_file_without_a_readable_size_gives_a_tag_without_width_and_height_and_says_so()
    {
        string svg = _temp.File("logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        var tab = Open("");
        _dialogs.Picked.Add(svg);

        await _w.HtmlInsertImageAsync();

        Assert.Equal("<img src=\"logo.svg\" alt=\"\">", Text(tab));
        Assert.Contains(Loc.Get(K.HtmlImageNoSize), _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task A_dismissed_picker_changes_nothing()
    {
        var tab = Open("<p>");
        long revision = tab.Doc.Doc.Revision;

        await _w.HtmlInsertImageAsync();

        Assert.Equal("<p>", Text(tab));
        Assert.Equal(revision, tab.Doc.Doc.Revision);
    }

    [AvaloniaFact]
    public async Task Insert_link_wraps_every_selection_and_escapes_the_address()
    {
        var tab = Open("x y", (0, 1), (2, 3));
        _dialogs.Prompts.Enqueue("https://example.org/?a=1&b=2");

        await _w.HtmlInsertLinkAsync();

        Assert.Equal("<a href=\"https://example.org/?a=1&amp;b=2\">x</a> <a href=\"https://example.org/?a=1&amp;b=2\">y</a>", Text(tab));
    }

    [AvaloniaFact]
    public async Task With_nothing_selected_a_link_shows_its_address()
    {
        var tab = Open("<p>");
        _dialogs.Prompts.Enqueue("mailto:me@example.org");

        await _w.HtmlInsertLinkAsync();

        Assert.Equal("<p><a href=\"mailto:me@example.org\">mailto:me@example.org</a>", Text(tab));
    }

    [AvaloniaFact]
    public async Task A_link_to_an_image_file_holds_the_picture_with_its_size_and_a_link_to_any_other_file_is_relative()
    {
        Png("img/pic one.png", 37, 21);
        Directory.CreateDirectory(Path.Combine(_temp.Path, "notes"));
        _temp.File(Path.Combine("notes", "readme.txt"), "x");
        var tab = Open("");
        _dialogs.Prompts.Enqueue("img/pic one.png");

        await _w.HtmlInsertLinkAsync();

        Assert.Equal("<a href=\"img/pic%20one.png\"><img src=\"img/pic%20one.png\" width=\"37\" height=\"21\" alt=\"\"></a>", Text(tab));

        _dialogs.Prompts.Enqueue(Path.Combine(_temp.Path, "notes", "readme.txt"));
        await _w.HtmlInsertLinkAsync();

        Assert.EndsWith("<a href=\"notes/readme.txt\">" + Path.Combine(_temp.Path, "notes", "readme.txt") + "</a>", Text(tab));
    }

    [AvaloniaFact]
    public async Task A_dismissed_question_writes_no_link()
    {
        var tab = Open("<p>");

        await _w.HtmlInsertLinkAsync();

        Assert.Equal("<p>", Text(tab));
    }
}
