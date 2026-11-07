using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using NksHub.Textida.Accessibility;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>A Markdown reader in a scrolled window of a given size, with the links and jumps it asks for recorded.</summary>
internal sealed class ReaderHost : IDisposable
{
    public readonly List<Uri> Opened = [];
    public readonly List<int> Jumps = [];
    public readonly Window Window;
    public readonly ScrollViewer Scroller;
    public readonly MarkdownReader Reader;

    public ReaderHost(string source, double width = 1000, double height = 800, ThemeVariant? theme = null, bool jumpScrolls = false, bool links = true)
    {
        var ctx = new PreviewRenderer.Ctx(new FontFamily("Consolas"), Open: links ? Opened.Add : null, JumpToLine: line =>
        {
            Jumps.Add(line);
            if (jumpScrolls) ScrollToLine(line);
        });
        Reader = new MarkdownReader(MarkdownReaderModel.Build(source), ctx);
        Scroller = new ScrollViewer { Content = Reader, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Window = new Window { Content = Scroller, Width = width, Height = height };
        if (theme is not null) Window.RequestedThemeVariant = theme;
        Window.Show();
        Settle();
    }

    public MarkdownReaderDocument Doc => Reader.Document;

    public void Settle()
    {
        for (int i = 0; i < 6; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
        }
    }

    public void ScrollToLine(double line)
    {
        Scroller.Offset = new Vector(0, Reader.Map.OffsetFor(line));
        Settle();
    }

    public int Find(Func<ReaderBlock, bool> match)
    {
        for (int i = 0; i < Doc.Blocks.Count; i++) if (match(Doc.Blocks[i])) return i;
        throw new InvalidOperationException("no such block");
    }

    public int Position(string text, int skip = 0) => Doc.PlainText.IndexOf(text, StringComparison.Ordinal) is var at and >= 0 ? at + skip : throw new InvalidOperationException("no text " + text);

    public Point At(int position) => Reader.TranslatePoint(Reader.PointAt(position) ?? throw new InvalidOperationException("not laid out"), Window)!.Value;

    public void Click(Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.MouseDown(point, MouseButton.Left, modifiers);
        Window.MouseUp(point, MouseButton.Left, modifiers);
        Settle();
    }

    public void Dispose() => Window.Close();
}

/// <summary>P9.3.4: the reader lays out only what is seen, selects across blocks like a browser, copies text and Markdown and follows links.</summary>
[Collection("LocState")]
public class MarkdownReaderTests : IDisposable
{
    private readonly List<ReaderHost> _hosts = [];

    public MarkdownReaderTests() => Loc.SetLanguage("en");

    public void Dispose()
    {
        foreach (var host in _hosts) host.Dispose();
        Loc.SetLanguage(Loc.DefaultLanguage);
    }

    private ReaderHost Host(string source, double width = 1000, double height = 800, bool jumpScrolls = false)
    {
        var host = new ReaderHost(source, width, height, jumpScrolls: jumpScrolls);
        _hosts.Add(host);
        return host;
    }

    private static string Todo => File.ReadAllText(Path.Combine(Repo.Root, "TODO.md"));

    private static async Task Until(Func<bool> condition, string what, ReaderHost host)
    {
        for (int i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(20);
            host.Settle();
        }
        Assert.True(condition(), "timed out waiting for " + what);
    }

    [AvaloniaFact]
    public void The_projects_todo_renders_whole_and_only_the_visible_blocks_are_laid_out()
    {
        string source = Todo;
        Assert.True(source.Split('\n').Length > 3000);
        var host = Host(source);
        var doc = host.Doc;
        Assert.Null(doc.Problem);
        Assert.True(doc.Blocks.Count > 1500, $"{doc.Blocks.Count} blocks");
        Assert.InRange(host.Reader.LaidCount, 1, 150);
        Assert.Equal(0, host.Reader.Realized.First);
        Assert.Contains(doc.Blocks, b => b.Kind == ReaderBlockKind.Heading && b.Level == 1);
        // the page is as tall as the whole document, measured or estimated
        Assert.True(host.Scroller.Extent.Height > 50_000, $"extent {host.Scroller.Extent.Height}");
        Assert.DoesNotContain(doc.Blocks.Take(host.Reader.Realized.Last + 1), b => b.Text.Contains("10000", StringComparison.Ordinal) && b.Text.Contains("prvků", StringComparison.Ordinal) && b.Kind == ReaderBlockKind.Code);
    }

    [AvaloniaFact]
    public void A_jump_to_a_heading_far_down_the_todo_lands_on_it_and_lays_out_only_around_it()
    {
        var host = Host(Todo, jumpScrolls: true);
        var heading = host.Doc.Headings[^3];
        host.ScrollToLine(heading.Line);
        int top = host.Reader.BlockAtY(host.Scroller.Offset.Y + 1);
        var block = host.Doc.Blocks[top];
        Assert.Equal((ReaderBlockKind.Heading, heading.Title), (block.Kind, block.Text));
        Assert.True(host.Reader.IsMeasured(top));
        Assert.True(host.Reader.Realized.First > 100, $"realized from {host.Reader.Realized.First}");
        Assert.InRange(host.Reader.LaidCount, 1, 400);
        Assert.InRange(host.Reader.Map.LineAt(host.Scroller.Offset.Y), heading.Line - 0.5, heading.Line + 1);
    }

    [AvaloniaFact]
    public void Scrolling_through_the_whole_todo_keeps_the_layouts_bounded_and_every_block_measurable()
    {
        var host = Host(Todo);
        double extent = host.Scroller.Extent.Height;
        for (double y = 0; y < extent; y += 20_000)
        {
            host.Scroller.Offset = new Vector(0, y);
            host.Settle();
            Assert.InRange(host.Reader.LaidCount, 1, 700);
        }
        host.Scroller.Offset = new Vector(0, host.Scroller.Extent.Height);
        host.Settle();
        Assert.Equal(host.Doc.Blocks.Count - 1, host.Reader.Realized.Last);
    }

    private const string Mixed = "# Title\n\nFirst **bold** paragraph here.\n\n- one\n- two\n\n```\ncode line\n```\n\n| a | b |\n|---|---|\n| c | d |\n\nLast words.\n";

    [AvaloniaFact]
    public void A_drag_selects_across_paragraphs_lists_code_and_table_cells()
    {
        var host = Host(Mixed);
        var from = host.At(host.Position("First"));
        var to = host.At(host.Position("d\n") + 1);
        host.Window.MouseDown(from, MouseButton.Left);
        host.Window.MouseMove(new Point(from.X + 40, from.Y + 30));
        host.Window.MouseMove(to);
        host.Window.MouseUp(to, MouseButton.Left);
        host.Settle();
        Assert.Equal("First bold paragraph here.\n\none\ntwo\n\ncode line\n\na\tb\nc\td", host.Reader.SelectedText());
        Assert.Empty(host.Opened);
    }

    [AvaloniaFact]
    public void A_double_click_takes_a_word_a_triple_click_the_block_and_shift_click_extends()
    {
        var host = Host(Mixed);
        var word = host.At(host.Position("paragraph", 3));
        host.Click(word);
        host.Click(word);
        Assert.Equal("paragraph", host.Reader.SelectedText());
        host.Click(word);
        Assert.Equal("First bold paragraph here.", host.Reader.SelectedText());

        host.Click(host.At(host.Position("Title")));
        Assert.False(host.Reader.HasSelection);
        // far enough in time that the next click is not a double click
        Thread.Sleep(600);
        host.Click(host.At(host.Position("two") + 3), RawInputModifiers.Shift);
        Assert.Equal("Title\n\nFirst bold paragraph here.\n\none\ntwo", host.Reader.SelectedText());
    }

    [AvaloniaFact]
    public async Task Ctrl_a_selects_everything_and_ctrl_c_copies_the_plain_text()
    {
        var host = Host(Mixed);
        host.Click(host.At(host.Position("Last")));
        host.Window.KeyPress(Key.A, TestApp.Mod, PhysicalKey.A, "a");
        host.Window.KeyPress(Key.C, TestApp.Mod, PhysicalKey.C, "c");
        host.Settle();
        Assert.Equal((0, host.Doc.Length), host.Reader.SelectionRange);
        string expected = "Title\n\nFirst bold paragraph here.\n\none\ntwo\n\ncode line\n\na\tb\nc\td\n\nLast words.";
        Assert.Equal(expected, host.Reader.SelectedText());
        Assert.Equal(expected, await host.Window.Clipboard!.TryGetTextAsync());
    }

    [AvaloniaFact]
    public void Copy_as_markdown_gives_the_source_the_selection_covers()
    {
        var host = Host(Mixed);
        host.Reader.Select(host.Position("bold"), host.Position("here"));
        Assert.Equal("bold** paragraph ", host.Reader.SelectedMarkdown());
        host.Reader.Select(host.Position("First"), host.Position("here.") + 5);
        Assert.Equal("First **bold** paragraph here.", host.Reader.SelectedMarkdown());
        host.Reader.Select(host.Position("one"), host.Position("two") + 3);
        Assert.Equal("- one\n- two", host.Reader.SelectedMarkdown());
        host.Reader.Select(host.Position("Title"), host.Position("code line") + 9);
        Assert.Equal("# Title\n\nFirst **bold** paragraph here.\n\n- one\n- two\n\n```\ncode line", host.Reader.SelectedMarkdown());
        host.Reader.Select(host.Position("a\tb"), host.Position("c\td") + 3);
        Assert.Equal("| a | b |\n|---|---|\n| c | d |", host.Reader.SelectedMarkdown());
    }

    [AvaloniaFact]
    public async Task Dragging_past_the_bottom_scrolls_and_the_selection_survives_scrolling_back()
    {
        string source = string.Concat(Enumerable.Range(1, 300).Select(i => $"Paragraph number {i} of the long page.\n\n"));
        var host = Host(source, height: 400);
        var from = host.At(host.Position("number 2 "));
        host.Window.MouseDown(from, MouseButton.Left);
        var below = new Point(from.X, host.Window.Height + 80);
        host.Window.MouseMove(below);
        await Until(() => host.Scroller.Offset.Y > 2000, "the page to scroll while dragging", host);
        host.Window.MouseMove(below);
        host.Window.MouseUp(below, MouseButton.Left);
        host.Settle();
        double offset = host.Scroller.Offset.Y;
        var (start, end) = host.Reader.SelectionRange;
        Assert.Equal(host.Position("number 2 "), start);
        Assert.True(host.Doc.BlockAt(end) > 40, $"selection ends in block {host.Doc.BlockAt(end)} at offset {offset}");
        string selected = host.Reader.SelectedText();
        host.Scroller.Offset = new Vector(0, 0);
        host.Settle();
        Assert.Equal(selected, host.Reader.SelectedText());
        Assert.StartsWith("number 2 of the long page.\n\nParagraph number 3", selected);
    }

    [AvaloniaFact]
    public void Selecting_in_the_todo_lays_out_no_more_than_the_view_needs()
    {
        var host = Host(Todo);
        host.Scroller.Offset = new Vector(0, host.Scroller.Extent.Height / 2);
        host.Settle();
        // points inside the view: a long TODO entry can start above it or reach below it
        double x = host.Reader.Bounds.Width / 2;
        var from = host.Reader.TranslatePoint(new Point(x, host.Scroller.Offset.Y + 10), host.Window)!.Value;
        var to = host.Reader.TranslatePoint(new Point(x, host.Scroller.Offset.Y + 700), host.Window)!.Value;
        host.Window.MouseDown(from, MouseButton.Left);
        for (int i = 1; i <= 10; i++) host.Window.MouseMove(new Point(from.X + (to.X - from.X) * i / 10, from.Y + (to.Y - from.Y) * i / 10));
        host.Window.MouseUp(to, MouseButton.Left);
        host.Settle();
        Assert.True(host.Reader.SelectedText().Length > 100);
        Assert.InRange(host.Reader.LaidCount, 1, 400);
    }

    [AvaloniaFact]
    public void A_click_on_a_link_opens_it_and_tab_and_enter_follow_one_from_the_keyboard()
    {
        var host = Host("Read [the manual](https://example.invalid/manual) and [go down](#end).\n\n# End\n");
        int link = host.Doc.Links.Select((l, i) => (l, i)).Single(x => x.l.External is not null).i;
        var point = host.Reader.TranslatePoint(host.Reader.LinkPoint(link)!.Value, host.Window)!.Value;
        host.Click(point);
        Assert.Equal("https://example.invalid/manual", Assert.Single(host.Opened).OriginalString);
        Assert.False(host.Reader.HasSelection);

        host.Reader.Focus();
        host.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        host.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
        Assert.Equal(1, host.Reader.FocusedLink);
        host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Assert.Equal([2], host.Jumps);
    }

    [AvaloniaFact]
    public void A_long_code_line_scrolls_sideways_instead_of_wrapping()
    {
        string line = string.Concat(Enumerable.Repeat("token_", 80));
        var host = Host("Text.\n\n```\n" + line + "\nshort\n```\n", width: 600);
        int code = host.Find(b => b.Kind == ReaderBlockKind.Code);
        var layout = host.Reader.LayoutOf(code)!;
        Assert.Equal(2, layout.TextLines.Count);
        var point = host.Reader.TranslatePoint(host.Reader.BlockBounds(code).Center, host.Window)!.Value;
        host.Window.MouseWheel(point, new Vector(0, -1), RawInputModifiers.Shift);
        host.Settle();
        int group = host.Doc.Blocks[code].Group;
        Assert.True(host.Reader.CodeScroll(group) > 0);
        // the text under the pointer follows the scroll
        int before = host.Reader.PositionAt(new Point(host.Reader.BlockBounds(code).Left + 40, host.Reader.BoxOrigin(code)!.Value.Y + 5));
        Assert.True(before - host.Doc.Blocks[code].DocStart > 4);
    }

    [AvaloniaFact]
    public void A_new_rendering_keeps_the_measured_heights_of_unchanged_blocks_and_the_selection()
    {
        var host = Host("# Heading\n\nKeep this phrase\n\nLast\n");
        host.Reader.Select(host.Position("this"), host.Position("this") + 4);
        host.Reader.SetDocument(MarkdownReaderModel.Build("Introduction\n\n# Heading\n\nKeep this phrase\n\nLast\n"));
        host.Settle();
        Assert.Equal("this", host.Reader.SelectedText());
        host.Reader.SetDocument(MarkdownReaderModel.Build("Introduction\n\n# Heading\n\nA replaced paragraph\n\nLast\n"));
        host.Settle();
        Assert.False(host.Reader.HasSelection);
    }

    [AvaloniaFact]
    public void The_context_menu_offers_copy_copy_as_markdown_and_select_all()
    {
        var host = Host(Mixed);
        var items = host.Reader.ContextMenu!.Items.OfType<MenuItem>().Select(i => i.Header as string).ToList();
        Assert.Equal([Loc.Get(K.ReaderCopy), Loc.Get(K.ReaderCopyMarkdown), Loc.Get(K.ReaderSelectAll)], items);
        Assert.Equal("Copy as Markdown", items[1]);
    }

    [AvaloniaFact]
    public void A_screen_reader_reads_the_page_by_line_and_word_and_follows_the_selection()
    {
        var host = Host("# Title\n\nFirst paragraph with words.\n\n- item one\n- item two\n");
        var peer = Assert.IsType<MarkdownReaderAutomationPeer>(Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(host.Reader));
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Document, peer.GetAutomationControlType());
        var text = peer.Text;
        const string Plain = "Title\nFirst paragraph with words.\nitem one\nitem two";
        Assert.Equal(Plain, text.GetText(0, text.Length));
        Assert.True(text.IsReadOnly);

        // by line: a block is a line
        var line = new TextRange(text, 0, 0);
        line.ExpandToEnclosingUnit(TextUnit.Line);
        Assert.Equal("Title\n", line.GetText());
        Assert.Equal(1, line.Move(TextUnit.Line, 1));
        Assert.Equal("First paragraph with words.\n", line.GetText());
        Assert.Equal(4, text.LineCount);
        Assert.Equal(text.Length, text.LineStart(text.LineCount));

        // by word, from the caret where the user clicked
        int paragraph = host.Doc.Blocks[1].DocStart;
        host.Reader.Select(paragraph + 8, paragraph + 8);
        var word = TextRange.Caret(text);
        word.ExpandToEnclosingUnit(TextUnit.Word);
        Assert.Equal("paragraph ", word.GetText());
        Assert.Equal(1, word.Move(TextUnit.Word, 1));
        Assert.Equal("with ", word.GetText());

        // the selection: the reader tells when it moves, and the screen reader can set it
        int raised = 0;
        text.SelectionChanged += () => raised++;
        host.Reader.Select(paragraph, paragraph + 5);
        Assert.Equal(1, raised);
        Assert.Equal(new TextSpan(paragraph, 5), text.Selection);
        Assert.Equal(paragraph + 5, text.CaretOffset);
        Assert.Equal("First", TextRange.SelectionOf(text).GetText());
        new TextRange(text, host.Doc.Blocks[2].DocStart, host.Doc.Blocks[2].DocStart + 4).Select();
        Assert.Equal("item", host.Reader.SelectedText());
        Assert.Equal(2, raised);

        // what is drawn: the character under a screen point is found again, and the page in view is the visible range
        Assert.Equal(new TextSpan(0, text.Length), text.VisibleRange);
        var bounds = Assert.Single(new TextRange(text, paragraph, paragraph + 5).GetBounds());
        Assert.True(bounds.Width > 10 && bounds.Height > 10, $"{bounds}");
        Assert.Equal(paragraph + 2, text.OffsetFromPoint(bounds.X + bounds.Width * 0.45, bounds.Y + bounds.Height / 2));
        Assert.Single(text.GetRangeBounds(paragraph + 3, 0)); // the caret

        // a new rendering is a text change
        var changes = new List<TextChange>();
        text.TextChanged += changes.Add;
        long revision = text.Revision;
        host.Reader.SetDocument(MarkdownReaderModel.Build("# Title\n\nNew text.\n"));
        var change = Assert.Single(changes);
        Assert.Equal((0, Plain.Length, "Title\nNew text.".Length), (change.Start, change.Removed, change.Inserted));
        Assert.Equal(revision + 1, text.Revision);
        Assert.Equal("New text.", text.GetText(text.LineStart(1), 9));
    }

    [AvaloniaFact]
    public void The_screen_reader_text_follows_the_mouse_and_focus_and_scrolls_a_range_into_view()
    {
        var host = Host(string.Concat(Enumerable.Range(0, 300).Select(i => $"Paragraph number {i}.\n\n")), 800, 400);
        var text = Assert.IsType<MarkdownReaderAutomationPeer>(Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(host.Reader)).Text;
        int selections = 0, focus = 0;
        text.SelectionChanged += () => selections++;
        text.FocusChanged += () => focus++;
        host.Reader.Focus();
        host.Settle();
        Assert.True(text.HasFocus);
        Assert.Equal(1, focus);

        var word = host.Reader.RangeBounds(3, 0, 9)[0];
        Point At(Point page) => host.Reader.TranslatePoint(page, host.Window)!.Value;
        host.Window.MouseDown(At(new Point(word.Left + 2, word.Center.Y)), MouseButton.Left);
        host.Window.MouseMove(At(new Point(word.Right - 1, word.Center.Y)));
        host.Window.MouseUp(At(new Point(word.Right - 1, word.Center.Y)), MouseButton.Left);
        Assert.True(selections >= 1);
        Assert.Equal(host.Reader.SelectedText(), TextRange.SelectionOf(text).GetText());
        Assert.Contains("aragrap", TextRange.SelectionOf(text).GetText(), StringComparison.Ordinal);

        int far = host.Doc.Blocks[250].DocStart;
        Assert.True(text.VisibleRange.End < far);
        Assert.Empty(text.GetRangeBounds(far, 5)); // not drawn
        new TextRange(text, far, far + 5).ScrollIntoView(alignToTop: true);
        host.Settle();
        Assert.True(text.VisibleRange.Start <= far && text.VisibleRange.End > far, $"{text.VisibleRange} should hold {far}");
        Assert.NotEmpty(text.GetRangeBounds(far, 5));
    }
}
