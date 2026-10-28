using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;

namespace NksHub.Textida.App.Tests;

/// <summary>One typed character must not allocate in proportion to the size of the document or of its line.</summary>
[Collection("LocState")]
public class KeystrokeAllocationTests(ITestOutputHelper output) : IDisposable
{
    private const int Keys = 50;
    private const long BoundTextSurface = 60_000; // 640 000 before the watermark stopped binding to the whole text, 22 000 after
    private const long BoundPagedSurface = 80_000; // 174 000 before the lines behind an edit were kept, 39 000 after

    private readonly TempDir _temp = new();
    private App? _app;

    public void Dispose()
    {
        TypingTrace.Enabled = false;
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>
    /// Bytes the current thread allocates while the focused surface takes one typed character, averaged over keystrokes. The event goes to the surface itself:
    /// the headless key helper also lays out and draws the window inside the call, which is the work of the renderer and not of the typing.
    /// </summary>
    private long BytesPerKeystroke(IInputElement surface)
    {
        void Type()
        {
            surface.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "a", Source = surface });
            Dispatcher.UIThread.RunJobs();
        }
        for (int i = 0; i < 20; i++) Type();
        TypingTrace.Reset();
        TypingTrace.Enabled = true;
        long total = 0;
        for (int i = 0; i < Keys; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            surface.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "a", Source = surface });
            total += GC.GetAllocatedBytesForCurrentThread() - before;
            Dispatcher.UIThread.RunJobs();
        }
        string summary = TypingTrace.Summary();
        output.WriteLine($"{total / Keys} B per keystroke\n" + summary);
        var updates = Regex.Match(summary, @"win\.updateStatus\s+n=\s*(\d+)");
        Assert.Equal(Keys, int.Parse(updates.Groups[1].Value)); // 3 per keystroke on the text surface and 2 on the paged one before they were joined
        return total / Keys;
    }

    /// <summary>The lines behind a typed character are not decoded again, only moved; the next edit lower down must still land on its place.</summary>
    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Lines_behind_an_edit_keep_their_place_for_the_next_edit(string end)
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string text = string.Concat(Enumerable.Range(1, 150_000).Select(i => $"line {i}{end}"));
        var tab = w.OpenFile(_temp.Bytes("rows.log", Encoding.UTF8.GetBytes(text)), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();

        TestApp.Type(w, "ab");
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 3; i++) TestApp.Press(w, Avalonia.Input.Key.Down);
        TestApp.Type(w, "Z");

        var doc = tab.Doc.Doc;
        string head = Encoding.UTF8.GetString(doc.ReadBytes(new Core.Documents.ByteRange(0, 60)));
        Assert.StartsWith($"abline 1{end}line 2{end}line 3{end}liZne 4{end}line 5", head);
        Assert.Equal((4L, 4L), tab.CaretPosition());
    }

    [AvaloniaFact]
    public void Keystroke_in_a_large_text_document_allocates_little()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        var tab = w.NewDocument();
        tab.Editor!.Document.Text = string.Concat(Enumerable.Range(0, 3000).Select(i => $"line {i} " + new string('x', i % 50) + "\n")) + new string('y', 200_000);
        Dispatcher.UIThread.RunJobs();
        tab.Doc.FlushEol();
        tab.Editor.TextArea.Focus();
        tab.Editor.CaretOffset = 1000;

        Assert.True(BytesPerKeystroke(tab.Editor.TextArea) < BoundTextSurface);
    }

    [AvaloniaFact]
    public void Keystroke_in_a_paged_document_allocates_little()
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string text = string.Concat(Enumerable.Range(0, 40_000).Select(i => $"line {i} " + new string('x', i % 50) + "\n")) + new string('y', 200_000);
        var tab = w.OpenFile(_temp.Bytes("big.txt", Encoding.UTF8.GetBytes(text)), null);
        Assert.True(tab.Doc.IsLarge);
        w.UpdateLayout();
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 600 && !tab.Paged.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        tab.Paged.GoToOffset(1000);

        Assert.True(BytesPerKeystroke(tab.Paged) < BoundPagedSurface);
    }

    /// <summary>
    /// A keystroke in the first 16 KiB of a long line decodes that part again (the cached text no longer fits): 640 000 bytes while the decoder made a string for every
    /// character, about 130 000 since. A keystroke behind that part does not decode anything, which is the 80 000 of a short line.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(100, 300_000)]
    [InlineData(6_000_000, BoundPagedSurface)]
    public void Keystroke_in_a_long_line_of_a_paged_document_allocates_little(int column, long bound)
    {
        _app = TestApp.Prepare(_temp);
        var w = _app.CreateWindow(null);
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        string line = string.Concat(Enumerable.Repeat("abc ěščř def ", 600_000));
        var tab = w.OpenFile(_temp.Bytes("line.txt", Encoding.UTF8.GetBytes(line + "\nnext\n")), null);
        Assert.True(tab.Doc.IsLarge);
        w.UpdateLayout();
        tab.Paged!.Focus();
        Dispatcher.UIThread.RunJobs();
        for (int i = 0; i < 600 && !tab.Paged.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        tab.Paged.FarMoveBytes = 1 << 20; // the line is far shorter than 100 MiB; its far places wait for a count on the pool
        tab.Paged.GoToOffset(column);
        TestApp.Settle(tab.Paged);

        long bytes = BytesPerKeystroke(tab.Paged);
        Assert.True(bytes < bound, $"{bytes} B per keystroke");
    }
}
