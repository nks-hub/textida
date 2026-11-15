using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class EditorTouchTests : IDisposable
{
    private readonly TempDir temp = new();
    private readonly App app;
    private readonly MainWindow window;

    public EditorTouchTests()
    {
        app = TestApp.Prepare(temp);
        window = app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temp.Dispose();
    }

    private EditorSurface Open(string text)
    {
        var tab = window.NewDocument();
        var editor = tab.Editor!;
        editor.Document.Text = text;
        editor.CaretOffset = 0;
        editor.TextArea.Focus();
        Draw();
        return editor;
    }

    private void Draw()
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.CaptureRenderedFrame();
    }

    private static Point At(EditorSurface editor, int offset, VisualYPosition y = VisualYPosition.LineMiddle)
    {
        var view = editor.TextArea.TextView;
        view.EnsureVisualLines();
        return view.GetVisualPosition(new TextViewPosition(editor.Document.GetLocation(offset)), y) - view.ScrollOffset;
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

    [AvaloniaFact]
    public void Finger_pan_scrolls_both_axes_and_preserves_multiple_selections()
    {
        var editor = Open(string.Join('\n', Enumerable.Repeat(new string('x', 2000), 300)));
        editor.SetCarets(Carets.Create([new Sel(1, 8), new Sel(15, 22)], 1), reveal: false);
        var before = editor.GetCarets();
        long revision = editor.Doc.Doc.Revision;
        using var finger = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        finger.Down(new Point(220, 210));
        finger.Move(new Point(90, 100));
        finger.Up(new Point(90, 100));
        Draw();
        Assert.True(editor.VerticalOffset > 0);
        Assert.True(editor.HorizontalOffset > 0);
        Assert.Equal(before.Items, editor.GetCarets().Items);
        Assert.Equal(before.Primary, editor.GetCarets().Primary);
        Assert.Equal(revision, editor.Doc.Doc.Revision);
    }

    [AvaloniaFact]
    public async Task Tap_places_caret_after_tab_and_surrogate_pair()
    {
        var editor = Open("a\tč😀 alpha_beta end");
        using var finger = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        var point = At(editor, 6);
        finger.Down(point);
        Assert.Equal(0, editor.CaretOffset);
        finger.Up(point);
        await Eventually(() => editor.CaretOffset == 6);
        Assert.Equal(0, editor.SelectionLength);
    }

    [AvaloniaFact]
    public async Task Tapping_below_the_last_line_places_caret_at_its_end()
    {
        var editor = Open("first\nlast");
        using var finger = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        var blank = new Point(500, 250);
        finger.Down(blank);
        finger.Up(blank);
        await Eventually(() => editor.CaretOffset == editor.Document.TextLength);
        Assert.Equal(0, editor.SelectionLength);
    }

    [AvaloniaFact]
    public async Task Pen_drag_into_blank_space_extends_selection_through_last_line()
    {
        var editor = Open("first\nlast");
        using var pen = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Pen);
        pen.Down(At(editor, 2));
        pen.Move(new Point(500, 250));
        pen.Up(new Point(500, 250));
        await Eventually(() => editor.SelectedText == "rst\nlast");
        Assert.Equal(editor.Document.TextLength, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task Hold_selects_word_and_handles_adjust_both_ends_in_read_only_text()
    {
        var editor = Open("before alpha_beta after");
        editor.IsReadOnly = true;
        long revision = editor.Doc.Doc.Revision;
        using var finger = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        var point = At(editor, 12);
        finger.Down(point);
        await Eventually(() => editor.SelectedText == "alpha_beta");
        finger.Up(point);
        Draw();
        var start = At(editor, 7, VisualYPosition.LineBottom);
        var startHandle = new Point(Math.Max(12, start.X - 6), start.Y + 10);
        var first = At(editor, 0);
        var startDelta = startHandle - At(editor, 7);
        finger.Down(startHandle);
        finger.Move(first + startDelta);
        finger.Up(first + startDelta);
        await Eventually(() => editor.SelectedText == "before alpha_beta");
        Draw();
        var end = At(editor, 17, VisualYPosition.LineBottom);
        var endHandle = new Point(end.X + 6, end.Y + 10);
        var endDelta = endHandle - At(editor, 17);
        var last = At(editor, editor.Document.TextLength);
        finger.Down(endHandle);
        finger.Move(last + endDelta);
        finger.Up(last + endDelta);
        await Eventually(() => editor.SelectedText == "before alpha_beta after");
        Assert.Equal(revision, editor.Doc.Doc.Revision);
        Assert.Equal("before alpha_beta after", editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Pen_drag_selects_unicode_after_scroll_and_release_ends_the_drag()
    {
        string row = "a\tč😀 alpha_beta end\n";
        var editor = Open(string.Concat(Enumerable.Repeat(row, 300)));
        editor.ScrollToVerticalOffset(editor.TextArea.TextView.DefaultLineHeight * 20);
        Draw();
        int lineStart = editor.Document.GetLineByNumber(23).Offset;
        using var pen = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Pen);
        pen.Down(At(editor, lineStart + 2));
        pen.Move(At(editor, lineStart + 11));
        pen.Up(At(editor, lineStart + 11));
        await Eventually(() => editor.SelectedText == "č😀 alpha");
        var before = editor.GetCarets();
        pen.Move(At(editor, lineStart + 15));
        Assert.Equal(before.Items, editor.GetCarets().Items);
    }

    [AvaloniaFact]
    public async Task Capture_loss_and_second_finger_cancel_without_leaving_a_drag()
    {
        var editor = Open("before alpha_beta after");
        using var pen = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Pen);
        pen.Down(At(editor, 7));
        pen.Move(At(editor, 17));
        await Eventually(() => editor.SelectedText == "alpha_beta");
        pen.LoseCapture();
        pen.Move(At(editor, 22));
        pen.Up(At(editor, 22));
        Assert.Equal("alpha_beta", editor.SelectedText);
        using var first = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        using var second = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        first.Down(At(editor, 0));
        second.Down(At(editor, 20));
        first.Move(At(editor, 5));
        first.Up(At(editor, 5));
        second.Up(At(editor, 20));
        Assert.Equal("alpha_beta", editor.SelectedText);
        first.Down(At(editor, 3));
        first.Up(At(editor, 3));
        await Eventually(() => editor.CaretOffset == 3 && editor.SelectionLength == 0);
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.PenBarrelButton)]
    [InlineData(RawInputModifiers.PenEraser)]
    public void Pen_barrel_and_eraser_leave_text_and_selection_unchanged(RawInputModifiers modifier)
    {
        var editor = Open("before alpha_beta after");
        editor.SetCarets(Carets.One(new Sel(7, 17)));
        using var pen = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Pen);
        pen.Down(At(editor, 2), modifier);
        pen.Move(At(editor, 22), modifier);
        pen.Up(At(editor, 22));
        Assert.Equal("alpha_beta", editor.SelectedText);
        Assert.Equal("before alpha_beta after", editor.Document.Text);
    }

    [AvaloniaFact]
    public async Task Keyboard_move_cancels_pending_hold_before_it_can_replace_the_caret()
    {
        var editor = Open("before alpha_beta after");
        using var finger = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        var point = At(editor, 12);
        finger.Down(point);
        TestApp.Press(window, Key.Right);
        Assert.Equal(1, editor.CaretOffset);
        await Task.Delay((Application.Current!.PlatformSettings?.HoldWaitDuration ?? TimeSpan.FromMilliseconds(500)) + TimeSpan.FromMilliseconds(100));
        Dispatcher.UIThread.RunJobs();
        finger.Up(point);
        Assert.Equal(1, editor.CaretOffset);
        Assert.Equal(0, editor.SelectionLength);
    }

    [AvaloniaFact]
    public async Task Switching_tabs_during_hold_cancels_old_contact_and_allows_a_new_tap()
    {
        var editor = Open("before alpha_beta after");
        var original = window.Active!;
        using var finger = new TouchTestInput(editor.TextArea.TextView, window, PointerType.Touch);
        finger.Down(At(editor, 12));
        Open("second document");
        await Task.Delay(650);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, editor.SelectionLength);
        window.SelectTab(original);
        Draw();
        finger.Down(At(editor, 4));
        finger.Up(At(editor, 4));
        await Eventually(() => editor.CaretOffset == 4);
    }
}
