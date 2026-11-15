using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class PagedTouchTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _window;

    public PagedTouchTests()
    {
        _app = TestApp.Prepare(_temp);
        _app.History.Policy = new Core.History.HistoryPolicy { Enabled = false };
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        _window = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private async Task<PagedTextView> Open(byte[] bytes, string name = "touch.txt")
    {
        var tab = _window.OpenFile(_temp.Bytes(name, bytes), null);
        Assert.True(tab.Doc.IsLarge);
        var view = tab.Paged!;
        await Eventually(() => view.Index.Complete);
        view.Focus();
        Draw();
        return view;
    }

    private static byte[] Rows(string first) => Encoding.UTF8.GetBytes(first + "\n" + string.Concat(Enumerable.Repeat("padding row\n", 100_000)));

    private void Draw()
    {
        _window.UpdateLayout();
        _window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Eventually(Func<bool> done)
    {
        for (int i = 0; i < 500 && !done(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(done());
    }

    private static Point PointAt(PagedTextView view, long offset)
    {
        var rect = Assert.IsType<Rect>(view.TouchCaretRect(offset));
        return new Point(rect.X + 0.1, rect.Y + rect.Height / 2);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Touch_hit_test_keeps_character_boundaries_across_tabs_and_wide_encodings(bool wide)
    {
        string text = "a\tč😀 alpha_beta end\n" + string.Concat(Enumerable.Repeat("padding row\n", 100_000));
        Encoding encoding = wide ? Encoding.Unicode : new UTF8Encoding(false);
        byte[] bom = encoding.GetPreamble();
        var view = await Open([.. bom, .. encoding.GetBytes(text)]);
        foreach (string prefix in new[] { "a\t", "a\tč", "a\tč😀 ", "a\tč😀 alpha_beta" })
        {
            long offset = bom.Length + encoding.GetByteCount(prefix);
            Assert.Equal(offset, await view.HitTestTouchAsync(PointAt(view, offset), CancellationToken.None));
        }
        long word = bom.Length + encoding.GetByteCount("a\tč😀 alpha");
        var range = Assert.IsType<TouchRange>(await view.TouchWordAtAsync(word, CancellationToken.None));
        Assert.Equal(bom.Length + encoding.GetByteCount("a\tč😀 "), range.Start);
        Assert.Equal(bom.Length + encoding.GetByteCount("a\tč😀 alpha_beta"), range.End);
    }

    [AvaloniaFact]
    public async Task Finger_pan_preserves_all_carets_and_does_not_modify_the_document()
    {
        var view = await Open(Rows("first selection"));
        var before = SelectionSet.Create([new TextSelection(0, 5), new TextSelection(6, 15)], 1);
        view.SetCarets(before);
        using var finger = new TouchTestInput(view.Children[0], _window, PointerType.Touch);
        finger.Down(new Point(180, 210));
        finger.Move(new Point(180, 100));
        finger.Up(new Point(180, 100));
        Dispatcher.UIThread.RunJobs();
        Assert.True(view.TopLine > 0);
        Assert.Equal(before.Items, view.AllCarets().Items);
        Assert.Equal(before.Primary, view.AllCarets().Primary);
        Assert.Equal(0, _window.Active!.Doc.Doc.Revision);
    }

    [AvaloniaFact]
    public async Task Finger_tap_moves_the_caret_and_hold_selects_a_word_in_read_only_text()
    {
        var view = await Open(Rows("before alpha_beta after"));
        view.ReadOnlyReason = "read only";
        using var finger = new TouchTestInput(view.Children[0], _window, PointerType.Touch);
        var point = PointAt(view, 12);
        finger.Down(point);
        finger.Up(point);
        await Eventually(() => view.CaretOffset == 12);
        Assert.False(view.HasSelection);
        finger.Down(point);
        await Eventually(() => view.SelectedText() == "alpha_beta");
        finger.Up(point);
        Assert.Equal(new ByteRange(7, 17), view.SelectionRange);
        Assert.Equal(0, _window.Active!.Doc.Doc.Revision);
    }

    [AvaloniaFact]
    public async Task Pen_drag_selects_byte_exact_unicode_text_and_capture_loss_ends_the_drag()
    {
        var view = await Open(Rows("a\tč😀 alpha_beta end"));
        long start = Encoding.UTF8.GetByteCount("a\t"), end = Encoding.UTF8.GetByteCount("a\tč😀 alpha");
        using var pen = new TouchTestInput(view.Children[0], _window, PointerType.Pen);
        pen.Down(PointAt(view, start));
        pen.Move(PointAt(view, end));
        await Eventually(() => view.SelectionRange == new ByteRange(start, end));
        Assert.Equal("č😀 alpha", view.SelectedText());
        pen.LoseCapture();
        pen.Move(PointAt(view, end + 4));
        pen.Up(PointAt(view, end + 4));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new ByteRange(start, end), view.SelectionRange);
        Assert.Equal(0, _window.Active!.Doc.Doc.Revision);
    }

    [AvaloniaFact]
    public async Task Far_touch_hit_test_yields_the_UI_thread_and_discards_a_changed_revision()
    {
        var view = await Open(Encoding.UTF8.GetBytes(new string('a', 1_200_000) + "\nnext\n"));
        view.ScrollToColumn(900_000);
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        view.FarMoveBytes = 1;
        view.FarMoveHook = token =>
        {
            started.Set();
            gate.Wait(token);
        };
        var pending = view.HitTestTouchAsync(new Point(400, 8), CancellationToken.None);
        try
        {
            Assert.False(pending.IsCompleted);
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            TestApp.Type(_window, "x");
        }
        finally { gate.Set(); }
        Assert.Null(await pending);
    }

    [AvaloniaFact]
    public async Task Far_touch_hit_test_cancels_before_a_stale_offset_can_be_selected()
    {
        var view = await Open(Encoding.UTF8.GetBytes(new string('a', 1_200_000) + "\nnext\n"));
        view.ScrollToColumn(900_000);
        using var started = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        view.FarMoveBytes = 1;
        view.FarMoveHook = token =>
        {
            started.Set();
            token.WaitHandle.WaitOne();
            token.ThrowIfCancellationRequested();
        };
        long before = view.CaretOffset;
        var pending = view.HitTestTouchAsync(new Point(400, 8), cancellation.Token);
        try { Assert.True(started.Wait(TimeSpan.FromSeconds(5))); }
        finally { cancellation.Cancel(); }
        Assert.Null(await pending);
        Assert.Equal(before, view.CaretOffset);
        Assert.False(view.HasSelection);
    }

    [AvaloniaTheory]
    [InlineData(Key.Right)]
    [InlineData(Key.Escape)]
    public async Task Keyboard_input_on_the_focused_owner_cancels_a_pending_pen_hit_test(Key key)
    {
        byte[] content = Encoding.UTF8.GetBytes(new string('a', 1_200_000) + "\nnext\n");
        var view = await Open(content);
        view.SelectBytes(2, 6);
        view.ScrollToColumn(900_000);
        view.FarMoveBytes = 1;
        using var gate = new ManualResetEventSlim();
        using var started = new ManualResetEventSlim();
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken hitToken = default;
        view.FarMoveHook = token =>
        {
            hitToken = token;
            started.Set();
            try { gate.Wait(TimeSpan.FromSeconds(5)); }
            finally { returned.TrySetResult(); }
        };
        using var pen = new TouchTestInput(view.Children[0], _window, PointerType.Pen);
        pen.Down(new Point(400, 8));
        var expected = key == Key.Right ? new ByteRange(6, 6) : new ByteRange(2, 6);
        try
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(view.IsFocused);
            Assert.False(hitToken.IsCancellationRequested);
            TestApp.Press(_window, key);
            Assert.True(hitToken.IsCancellationRequested);
            Assert.Equal(expected, view.SelectionRange);
            pen.Up(new Point(400, 8));
        }
        finally { gate.Set(); }
        await returned.Task;
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(expected, view.SelectionRange);
        Assert.Equal(6, view.CaretOffset);
        var document = _window.Active!.Doc.Doc;
        Assert.Equal(0, document.Revision);
        Assert.Equal(content, document.ReadBytes(new ByteRange(0, document.ByteLength)));
    }
}
