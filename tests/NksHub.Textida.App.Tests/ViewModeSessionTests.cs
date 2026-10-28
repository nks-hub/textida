using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.11 and P9.3.4 after a crash: the tabs of the last run come back, a picture as a picture, the text of a file as the journal kept it.</summary>
[Collection("LocState")]
public class ViewModeSessionTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App _app;

    public ViewModeSessionTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>The previous run ends the way a crash ends it: the journal is flushed and released, no clean-exit flag is written.</summary>
    private MainWindow Crash()
    {
        _app.SaveSession();
        _app.Journal.Flush();
        _app = TestApp.Restart(_temp, _app, _dialogs);
        return _app.CreateWindow(null);
    }

    [AvaloniaFact]
    public async Task A_picture_tab_comes_back_as_a_picture_and_a_markdown_tab_with_unsaved_text_stays_in_the_editor()
    {
        var w = _app.CreateWindow(null);
        w.OpenFile(_temp.Bytes("back.png", Pictures.Png(50, 20)), null);
        var notes = w.OpenFile(_temp.File("notes.md", "# Notes\n"), null);
        notes.Editor!.Document.Insert(notes.Editor.Document.TextLength, "unsaved\n");

        var w2 = Crash();
        w2.RestoreFrom(_app.Journal);

        var picture = w2.Tabs.Single(t => t.Path?.EndsWith("back.png", StringComparison.Ordinal) == true);
        Assert.Equal(ViewMode.Image, picture.Doc.Mode);
        Assert.True(picture.Doc.IsImageFile);
        var view = Assert.IsType<ImageView>(picture.Custom);
        for (var sw = System.Diagnostics.Stopwatch.StartNew(); view.Loading && sw.Elapsed < TimeSpan.FromSeconds(20);)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.Equal((50, 20), (view.Info!.Value.Width, view.Info.Value.Height));
        Assert.Equal(3, picture.ModeBar!.Buttons.Count);

        var restored = w2.Tabs.Single(t => t.Path?.EndsWith("notes.md", StringComparison.Ordinal) == true);
        Assert.NotNull(restored.Editor);
        Assert.Equal("# Notes\nunsaved\n", restored.Editor!.Document.Text);
        Assert.True(restored.Doc.Doc.IsModified);
        Assert.Equal(3, restored.ModeBar!.Buttons.Count);
    }
}
