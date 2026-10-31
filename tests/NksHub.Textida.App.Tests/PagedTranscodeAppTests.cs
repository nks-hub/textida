using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.5.3: "Convert to" on a large file in the window: it waits as a step of the document, the loss is confirmed first, and the save writes the new encoding.</summary>
[Collection("LocState")]
public class PagedTranscodeAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public PagedTranscodeAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        var sw = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            Assert.True(sw.ElapsedMilliseconds < 60_000, "the task did not end");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        return await task;
    }

    private DocTab OpenLarge(string name, byte[] bytes)
    {
        var tab = _w.OpenFile(_temp.Bytes(name, bytes), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(10);
            Dispatcher.UIThread.RunJobs();
        }
        return tab;
    }

    [AvaloniaFact]
    public async Task Convert_to_is_offered_for_a_large_file_waits_for_the_save_and_undo_takes_it_back()
    {
        var text = string.Concat(Enumerable.Repeat("příliš žluťoučký kůň úpěl ďábelské ódy\n", 40_000));
        var tab = OpenLarge("cz.txt", Encoding.UTF8.GetBytes(text));
        var cp1250 = TextCodec.Cp1250;
        Assert.All(_w.ConvertChoices(tab), c => Assert.True(c.Enabled));

        Assert.True(await Pump(_w.ConvertToAsync(tab, cp1250)));
        Assert.Equal(cp1250, tab.Doc.Doc.PendingTranscode);
        Assert.Equal(TextCodec.Utf8, tab.Doc.Doc.Codec); // nothing is written, nothing is re-read yet
        Assert.True(tab.Doc.Doc.IsModified);
        Assert.Empty(_dialogs.Asked);

        tab.Doc.Doc.Undo();
        Assert.Null(tab.Doc.Doc.PendingTranscode);
        tab.Doc.Doc.Redo();

        Assert.True(await Pump(_w.SaveAsync(tab)));
        Assert.Equal(cp1250.Encode(text), File.ReadAllBytes(tab.Path!));
        Assert.Equal(cp1250, tab.Doc.Doc.Codec);
        Assert.False(tab.Doc.Doc.IsModified);

        tab.Doc.Doc.Undo(); // back to UTF-8 on the next save
        Assert.Equal(TextCodec.Utf8, tab.Doc.Doc.PendingTranscode);
    }

    [AvaloniaFact]
    public async Task Lost_characters_are_confirmed_before_the_conversion_is_staged_and_a_refusal_stages_nothing()
    {
        var text = "ok 日本語 emoji 😀\n" + string.Concat(Enumerable.Repeat("line\n", 300_000));
        var tab = OpenLarge("loss.txt", Encoding.UTF8.GetBytes(text));
        var cp1250 = TextCodec.Cp1250;

        _dialogs.Answers.Enqueue(1); // Cancel
        Assert.False(await Pump(_w.ConvertToAsync(tab, cp1250)));
        Assert.Null(tab.Doc.Doc.PendingTranscode);
        Assert.Contains("4", _dialogs.Asked[^1].Message); // 日 本 語 and the emoji

        _dialogs.Answers.Enqueue(0); // convert, replacing them with ?
        Assert.True(await Pump(_w.ConvertToAsync(tab, cp1250)));
        Assert.True(await Pump(_w.SaveAsync(tab)));
        Assert.Equal(cp1250.Encode(text.Replace("日", "?").Replace("本", "?").Replace("語", "?").Replace("😀", "?")), File.ReadAllBytes(tab.Path!));
    }
}
