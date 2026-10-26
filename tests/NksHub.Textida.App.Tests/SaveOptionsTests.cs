using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>Settings that change what a save or an open does: autosave, trailing blanks, final line break, detected indentation.</summary>
[Collection("LocState")]
public class SaveOptionsTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly TimeSpan _interval = OpenDocument.AutosaveInterval;

    public SaveOptionsTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        OpenDocument.AutosaveInterval = _interval;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;

    [AvaloniaFact]
    public async Task Autosave_writes_the_file_after_a_delay_when_the_setting_is_on()
    {
        OpenDocument.AutosaveInterval = TimeSpan.FromMilliseconds(150);
        _app.Workbench.Set("files.autoSave", "afterDelay");
        var path = _temp.File("auto.txt", "a");
        var tab = _w.OpenFile(path, null);
        tab.Editor!.Document.Insert(1, "b");

        for (int i = 0; i < 80 && File.ReadAllText(path) == "a"; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }

        Assert.Equal("ab", File.ReadAllText(path));
        Assert.False(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public async Task Turning_autosave_off_stops_it()
    {
        OpenDocument.AutosaveInterval = TimeSpan.FromMilliseconds(100);
        _app.Workbench.Set("files.autoSave", "afterDelay");
        var path = _temp.File("auto2.txt", "a");
        var tab = _w.OpenFile(path, null);
        Assert.NotNull(tab.Doc.Autosave);
        _app.Workbench.Set("files.autoSave", "off");
        Assert.Null(tab.Doc.Autosave);
        tab.Editor!.Document.Insert(1, "b");
        await Task.Delay(400);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("a", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task Save_trims_trailing_whitespace_and_adds_the_final_line_break_when_asked_and_undo_takes_it_back()
    {
        _app.Workbench.Set("files.trimTrailingWhitespace", JsonValue.Create(true));
        _app.Workbench.Set("files.insertFinalNewline", JsonValue.Create(true));
        var path = _temp.File("t.txt", "a  \r\nb\t\r\nc");
        var tab = _w.OpenFile(path, null);
        tab.Doc.FlushEol();
        tab.Editor!.Document.Insert(0, "x");

        Assert.True(await _w.SaveAsync(tab));

        // only the edited line is trimmed; the blanks of the line nobody touched stay
        Assert.Equal("xa\r\nb\t\r\nc\r\n", File.ReadAllText(path));
        _w.Undo();
        Assert.Equal("xa  \r\nb\t\r\nc", Text(tab));
        Assert.True(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public async Task Save_leaves_the_text_alone_by_default()
    {
        var path = _temp.File("d.txt", "a  \nb");
        var tab = _w.OpenFile(path, null);
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal("a  \nb", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public void Detected_indentation_overrides_the_setting_for_that_file_and_the_status_bar_shows_it()
    {
        var two = _w.OpenFile(_temp.File("two.js", "if (a) {\n  b();\n  if (c) {\n    d();\n  }\n}\n"), null);
        Assert.Contains("Mezery: 2", _w.StatusRightText);
        Dispatcher.UIThread.RunJobs();
        two.Editor!.TextArea.Focus();
        two.Editor.CaretOffset = 0;
        TestApp.Press(_w, Key.Tab);
        Assert.StartsWith("  if (a)", Text(two));

        var tabs = _w.OpenFile(_temp.File("tabs.js", "if (a) {\n\tb();\n\tif (c) {\n\t\td();\n\t}\n}\n"), null);
        Assert.Contains("Tabulátory: 4", _w.StatusRightText);

        var plain = _w.OpenFile(_temp.File("plain.txt", "no indentation here\nnone\n"), null);
        Assert.Contains("Mezery: 4", _w.StatusRightText);
        Assert.Null(plain.Doc.IndentOverride);
    }

    [AvaloniaFact]
    public void Detection_can_be_switched_off()
    {
        _app.Workbench.Set("editor.detectIndentation", JsonValue.Create(false));
        var tab = _w.OpenFile(_temp.File("off.js", "a\n  b\n  c\n"), null);
        Assert.Null(tab.Doc.IndentOverride);
        Assert.Contains("Mezery: 4", _w.StatusRightText);
    }
}
