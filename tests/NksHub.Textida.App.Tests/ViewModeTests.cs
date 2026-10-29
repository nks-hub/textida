using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using SkiaSharp;

namespace NksHub.Textida.App.Tests;

/// <summary>Pictures of every format the viewer opens, made in memory so the tests carry no binary files.</summary>
internal static class Pictures
{
    private static byte[] Encode(SKEncodedImageFormat format, int width, int height, bool transparent)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, transparent ? SKAlphaType.Premul : SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(transparent ? new SKColor(255, 0, 0, 100) : new SKColor(30, 120, 220));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static byte[] Png(int width = 64, int height = 32, bool transparent = false) => Encode(SKEncodedImageFormat.Png, width, height, transparent);

    public static byte[] Jpeg(int width = 64, int height = 32) => Encode(SKEncodedImageFormat.Jpeg, width, height, false);

    public static byte[] Webp(int width = 64, int height = 32) => Encode(SKEncodedImageFormat.Webp, width, height, false);

    /// <summary>A 24-bit bitmap of one color.</summary>
    public static byte[] Bmp(int width = 64, int height = 32)
    {
        int row = (width * 3 + 3) & ~3, pixels = row * height;
        var bytes = new byte[54 + pixels];
        "BM"u8.CopyTo(bytes);
        BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 2);
        BitConverter.GetBytes(54).CopyTo(bytes, 10);
        BitConverter.GetBytes(40).CopyTo(bytes, 14);
        BitConverter.GetBytes(width).CopyTo(bytes, 18);
        BitConverter.GetBytes(height).CopyTo(bytes, 22);
        BitConverter.GetBytes((short)1).CopyTo(bytes, 26);
        BitConverter.GetBytes((short)24).CopyTo(bytes, 28);
        BitConverter.GetBytes(pixels).CopyTo(bytes, 34);
        for (int i = 54; i < bytes.Length; i++) bytes[i] = (byte)(i % 3 == 0 ? 200 : 40);
        return bytes;
    }

    /// <summary>An icon whose one entry holds a PNG.</summary>
    public static byte[] Ico(int width = 32, int height = 32)
    {
        var png = Png(width, height, transparent: true);
        var bytes = new byte[22 + png.Length];
        bytes[2] = 1;
        bytes[4] = 1;
        bytes[6] = (byte)(width >= 256 ? 0 : width);
        bytes[7] = (byte)(height >= 256 ? 0 : height);
        bytes[10] = 1;
        bytes[12] = 32;
        BitConverter.GetBytes(png.Length).CopyTo(bytes, 14);
        BitConverter.GetBytes(22).CopyTo(bytes, 18);
        png.CopyTo(bytes, 22);
        return bytes;
    }

    /// <summary>The smallest GIF there is: one transparent pixel.</summary>
    public static byte[] Gif() => Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    public static IEnumerable<object[]> All()
    {
        yield return new object[] { "png", Png(), "PNG", 64, 32 };
        yield return new object[] { "jpg", Jpeg(), "JPEG", 64, 32 };
        yield return new object[] { "gif", Gif(), "GIF", 1, 1 };
        yield return new object[] { "bmp", Bmp(), "BMP", 64, 32 };
        yield return new object[] { "webp", Webp(), "WebP", 64, 32 };
        yield return new object[] { "ico", Ico(), "ICO", 32, 32 };
    }
}

/// <summary>P3.6.11 and P9.3.4 in the app: the per-tab view mode and its one switcher bar, the picture viewer and the Markdown reading, editing and source modes.</summary>
[Collection("LocState")]
public class ViewModeTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly ScriptedDialogs _dialogs = new();

    public ViewModeTests()
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

    // ---- helpers ----

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 20_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private void Layout()
    {
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        _w.UpdateLayout();
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static Button ButtonOf(DocTab tab, string label) => tab.ModeBar!.Buttons.Single(b => (string?)b.Content == label);

    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static string TextOf(Control root) => string.Join("\n", Tree(root).OfType<TextBlock>().Select(t =>
        t.Inlines is { Count: > 0 } inlines ? string.Concat(inlines.OfType<Run>().Select(r => r.Text)) : t.Text ?? ""));

    private async Task<(DocTab Tab, ImageView View)> OpenPicture(string name, byte[] bytes)
    {
        _w.OpenFile(_temp.Bytes(name, bytes), null);
        return await Shown();
    }

    /// <summary>Waits until the active tab shows a picture that was decoded or has its message.</summary>
    private async Task<(DocTab Tab, ImageView View)> Shown()
    {
        await Until(() => _w.Active?.Custom is ImageView { Loading: false }, "the picture to be decoded");
        Layout();
        return (_w.Active!, (ImageView)_w.Active!.Custom!);
    }

    private void Chord(Key second)
    {
        Dispatcher.UIThread.RunJobs();
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, second, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
    }

    private static string Lines(int count) => string.Concat(Enumerable.Range(0, count).Select(i => $"Paragraph {i}\n\n"));

    // ---- pictures ----

    [AvaloniaTheory]
    [MemberData(nameof(Pictures.All), MemberType = typeof(Pictures))]
    public async Task Every_picture_format_opens_as_a_picture_with_its_size_format_and_file_size_in_the_status_bar(string extension, byte[] bytes, string format, int width, int height)
    {
        var (tab, view) = await OpenPicture("picture." + extension, bytes);
        Assert.Equal(ViewMode.Image, tab.Doc.Mode);
        Assert.Null(tab.Surface);
        Assert.Null(tab.Paged);
        Assert.Null(tab.Hex);
        Assert.NotNull(view.Info);
        Assert.Equal((format, width, height, (long)bytes.Length), (view.Info!.Value.Format, view.Info.Value.Width, view.Info.Value.Height, view.Info.Value.FileBytes));
        Assert.Null(view.Problem);
        Assert.True(view.Paper.IsVisible);
        Assert.NotNull(view.Surface.Source);
        string status = _w.StatusLeftText;
        Assert.Contains($"{width} × {height} px", status);
        Assert.Contains(format, status);
        Assert.Contains(OpenDocument.FormatSize(bytes.Length), status);
        Assert.Equal([Loc.Get(K.ViewModeShowImage), Loc.Get(K.ViewModeShowText), Loc.Get(K.ViewModeShowHex)], tab.ModeBar!.Buttons.Select(b => (string?)b.Content));
        Assert.True(tab.ModeBar.Buttons[0].IsChecked);
        Assert.Empty(_w.Documents.Where(d => d != tab.Doc));
    }

    [AvaloniaFact]
    public async Task A_svg_stays_text_and_a_file_with_a_picture_name_that_is_not_a_picture_stays_text()
    {
        var svg = _w.OpenFile(_temp.File("logo.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"><rect/></svg>"), null);
        Assert.NotNull(svg.Surface);
        Assert.Null(svg.ModeBar);
        var fake = _w.OpenFile(_temp.File("fake.png", "BMW is a car maker, and this plain text file begins with two letters of a bitmap."), null);
        Assert.NotNull(fake.Surface);
        Assert.Equal(ViewMode.Text, fake.Doc.Mode);
        Assert.False(fake.Doc.IsImageFile);
        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public void An_icon_is_a_picture_of_the_viewer_and_the_pictures_of_a_markdown_preview_stay_what_they_were()
    {
        Assert.True(PreviewPolicy.Drawable(Pictures.Png()));
        Assert.True(PreviewPolicy.Drawable(Pictures.Webp()));
        Assert.False(PreviewPolicy.Drawable(Pictures.Ico()));
    }

    [AvaloniaFact]
    public async Task The_picture_is_fitted_and_zoomed_by_keys_the_commands_and_ctrl_wheel_and_moved_with_the_mouse()
    {
        var (tab, view) = await OpenPicture("big.png", Pictures.Png(2400, 1600));
        Assert.True(view.IsFit);
        int fitted = view.ZoomPercent;
        Assert.InRange(fitted, 1, 99);
        Assert.InRange(view.Paper.Width, 2400 * fitted / 100.0 - 24, 2400 * fitted / 100.0 + 24);

        view.Focus();
        TestApp.Press(_w, Key.D1);
        Assert.Equal(100, view.ZoomPercent);
        Assert.False(view.IsFit);
        Assert.Equal(2400.0, view.Paper.Width, 0.5);
        TestApp.Press(_w, Key.OemPlus, RawInputModifiers.Shift);
        Assert.Equal(125, view.ZoomPercent);
        TestApp.Press(_w, Key.OemMinus);
        Assert.Equal(100, view.ZoomPercent);
        TestApp.Press(_w, Key.D0);
        Assert.True(view.IsFit);
        Assert.Equal(fitted, view.ZoomPercent);

        TestApp.Press(_w, Key.OemPlus, TestApp.Mod); // the keys of the Zoom In and Reset Zoom commands work on the picture as well
        Assert.True(view.ZoomPercent > fitted);
        TestApp.Press(_w, Key.D0, TestApp.Mod);
        Assert.True(view.IsFit);
        Assert.Equal(fitted, view.ZoomPercent);

        _w.Zoom(1); // the Zoom In command of the View menu
        Assert.InRange(view.ZoomPercent, (int)Math.Round(fitted * 1.25) - 1, (int)Math.Round(fitted * 1.25) + 1);
        _w.ZoomReset(); // Reset Zoom fits the picture
        Assert.True(view.IsFit);
        Assert.Equal(fitted, view.ZoomPercent);

        Layout();
        var inside = view.TranslatePoint(new Point(200, 150), _w)!.Value;
        _w.MouseWheel(inside, new Vector(0, 1), TestApp.Mod);
        Assert.True(view.ZoomPercent > fitted);
        Assert.False(view.IsFit);
        double afterWheel = view.ZoomPercent;
        _w.MouseWheel(inside, new Vector(0, 1), RawInputModifiers.None); // the wheel alone scrolls, it does not zoom
        Assert.Equal(afterWheel, view.ZoomPercent);

        TestApp.Press(_w, Key.D1);
        Layout();
        Assert.True(view.Extent.Width > view.Viewport.Width);
        view.Offset = default;
        _w.MouseDown(inside, MouseButton.Left);
        _w.MouseMove(new Point(inside.X - 120, inside.Y - 80));
        _w.MouseUp(new Point(inside.X - 120, inside.Y - 80), MouseButton.Left);
        Assert.InRange(view.Offset.X, 119, 121);
        Assert.InRange(view.Offset.Y, 79, 81);
        TestApp.Press(_w, Key.Down);
        Assert.True(view.Offset.Y > 80);
    }

    [AvaloniaFact]
    public async Task A_checkerboard_stands_under_the_picture_and_a_transparent_picture_has_alpha()
    {
        var (_, view) = await OpenPicture("alpha.png", Pictures.Png(40, 40, transparent: true));
        Assert.IsType<DrawingBrush>(view.Paper.Background);
        Assert.Same(view.Surface, view.Paper.Child);
        Assert.Equal(40.0, view.Paper.Width, 0.5);
    }

    [AvaloniaFact]
    public async Task The_bar_shows_the_picture_as_text_or_hex_and_back_without_opening_the_file_again()
    {
        var bytes = Pictures.Png();
        var (tab, _) = await OpenPicture("again.png", bytes);
        var od = tab.Doc;
        var id = od.Doc.Id;
        long revision = od.Doc.Revision;

        Click(ButtonOf(tab, Loc.Get(K.ViewModeShowText)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ViewMode.Text, od.Mode);
        Assert.Same(_w.Active!.ModeBar!.Buttons[1], _w.FocusManager!.GetFocusedElement()); // the keyboard user stays on the bar
        Assert.NotNull(_w.Active!.Surface);
        Assert.Same(od, _w.Active.Doc);
        Assert.True(_w.Active.ModeBar!.Buttons[1].IsChecked);
        Assert.False(_w.Active.ModeBar.Buttons[0].IsChecked);
        Assert.Equal(AutomationProperties.GetName(_w.Active.ModeBar), Loc.Get(K.ViewModeBar, ("mode", Loc.Get(K.ViewModeText))));

        Click(ButtonOf(_w.Active, Loc.Get(K.ViewModeShowHex)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ViewMode.Hex, od.Mode);
        Assert.NotNull(_w.Active!.Hex);
        Assert.Equal(bytes, od.Doc.ReadBytes(new Core.Documents.ByteRange(0, od.Doc.ByteLength)));

        Click(ButtonOf(_w.Active, Loc.Get(K.ViewModeShowImage)));
        await Shown();
        Assert.Equal(ViewMode.Image, od.Mode);
        Assert.IsType<ImageView>(_w.Active!.Custom);
        Assert.Same(od, _w.Active.Doc);
        Assert.Equal(id, od.Doc.Id);
        Assert.Equal(revision, od.Doc.Revision);
        Assert.Single(_w.Documents);
        Assert.False(od.Doc.IsModified);

        // pressing the pressed button changes nothing
        Click(_w.Active.ModeBar!.Buttons[0]);
        Assert.True(_w.Active.ModeBar.Buttons[0].IsChecked);
        Assert.Equal(ViewMode.Image, od.Mode);
    }

    [AvaloniaFact]
    public async Task The_hex_toggle_and_the_cycle_command_walk_the_modes_of_a_picture_and_hex_returns_to_the_picture()
    {
        var (tab, _) = await OpenPicture("cycle.png", Pictures.Png());
        var od = tab.Doc;
        Chord(Key.H);
        Assert.Equal(ViewMode.Hex, od.Mode);
        Chord(Key.H);
        await Shown();
        Assert.Equal(ViewMode.Image, od.Mode); // back to where the user came from, not to text

        Chord(Key.V); // the cycle command: picture, text, hex, picture
        Assert.Equal(ViewMode.Text, od.Mode);
        Chord(Key.V);
        Assert.Equal(ViewMode.Hex, od.Mode);
        Chord(Key.V);
        await Shown();
        Assert.Equal(ViewMode.Image, od.Mode);
        Assert.Single(_w.Documents);
    }

    [AvaloniaFact]
    public async Task The_setting_picks_how_a_picture_file_opens_and_the_bar_is_there_in_every_case()
    {
        var bytes = Pictures.Png();
        _app.Workbench.Set("files.imageOpen", "text");
        var asText = _w.OpenFile(_temp.Bytes("t.png", bytes), null);
        Assert.NotNull(asText.Surface);
        Assert.Equal(ViewMode.Text, asText.Doc.Mode);
        Assert.Null(asText.Bar); // the binary offer is the bar's job here
        Assert.Equal(3, asText.ModeBar!.Buttons.Count);
        Click(ButtonOf(asText, Loc.Get(K.ViewModeShowImage)));
        await Shown();
        Assert.Equal(ViewMode.Image, _w.Active!.Doc.Mode);

        _app.Workbench.Set("files.imageOpen", "hex");
        var asHex = _w.OpenFile(_temp.Bytes("h.png", bytes), null);
        Assert.NotNull(asHex.Hex);
        Assert.Equal(ViewMode.Hex, asHex.Doc.Mode);
        Assert.True(asHex.ModeBar!.Buttons[2].IsChecked);

        _app.Workbench.Set("files.imageOpen", "image");
        _w.OpenFile(_temp.Bytes("i.png", bytes), null);
        await Shown();
        Assert.Equal(ViewMode.Image, _w.Active!.Doc.Mode);
    }

    [AvaloniaFact]
    public async Task A_damaged_file_and_a_picture_with_too_many_pixels_show_a_message_and_offer_text_and_hex_without_making_the_pixels()
    {
        var good = Pictures.Png();
        var (damaged, damagedView) = await OpenPicture("cut.png", good[..45]);
        Assert.Null(damagedView.Info);
        Assert.Equal(Loc.Get(K.ImageBroken), damagedView.Problem);
        Assert.False(damagedView.Paper.IsVisible);
        Assert.Equal(Loc.Get(K.ImageBroken), AutomationProperties.GetName(damagedView));
        Assert.Equal(Loc.Get(K.ImageBroken), _w.StatusLeftText.Split(" · ")[0]);
        Assert.Equal(3, damaged.ModeBar!.Buttons.Count);

        var huge = Pictures.Png();
        BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(50_000)).CopyTo(huge, 16);
        BitConverter.GetBytes(System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(50_000)).CopyTo(huge, 20);
        var (tooMany, tooManyView) = await OpenPicture("claims.png", huge);
        Assert.Null(tooManyView.Info);
        Assert.Contains("50000 × 50000", tooManyView.Problem!.Replace("\u00A0", ""));
        Assert.Null(tooManyView.Surface.Source);

        Click(ButtonOf(tooMany, Loc.Get(K.ViewModeShowHex)));
        Assert.NotNull(_w.Active!.Hex); // the editor goes on
        Click(ButtonOf(_w.Active, Loc.Get(K.ViewModeShowText)));
        Assert.NotNull(_w.Active!.Surface);
    }

    [AvaloniaFact]
    public async Task A_file_over_the_byte_limit_shows_a_message_and_is_never_read_into_memory()
    {
        string path = Path.Combine(_temp.Path, "huge.png");
        using (var stream = File.Create(path))
        {
            stream.Write(Pictures.Png());
            stream.SetLength(PluginLimitsPlaceholders.ImageViewMaxBytes + 1024);
        }
        long before = GC.GetTotalAllocatedBytes();
        _w.OpenFile(path, null);
        var (tab, view) = await Shown();
        long allocated = GC.GetTotalAllocatedBytes() - before;
        Assert.Null(view.Info);
        Assert.Equal(Loc.Get(K.ImageTooLarge, ("size", OpenDocument.FormatSize(PluginLimitsPlaceholders.ImageViewMaxBytes + 1024)), ("limit", OpenDocument.FormatSize(PluginLimitsPlaceholders.ImageViewMaxBytes))), view.Problem);
        Assert.True(allocated < PluginLimitsPlaceholders.ImageViewMaxBytes / 2, $"{allocated} bytes were allocated for a file over the limit");
        Click(ButtonOf(tab, Loc.Get(K.ViewModeShowHex)));
        Assert.NotNull(_w.Active!.Hex);
    }

    [AvaloniaFact]
    public async Task The_picture_is_read_again_when_the_file_changes_on_disk_and_nothing_is_asked()
    {
        string path = _temp.Bytes("live.png", Pictures.Png(64, 32));
        _w.OpenFile(path, null);
        var (tab, view) = await Shown();
        Assert.Equal(64, view.Info!.Value.Width);

        File.WriteAllBytes(path, Pictures.Png(200, 100));
        tab.Doc.Doc.CheckExternalChange();
        Dispatcher.UIThread.RunJobs();
        var (fresh, freshView) = await Shown();
        await Until(() => freshView.Info is not null, "the new picture");
        Assert.NotSame(view, freshView);
        Assert.Equal((200, 100), (freshView.Info!.Value.Width, freshView.Info.Value.Height));
        Assert.Contains("200 × 100 px", _w.StatusLeftText);
        Assert.Null(fresh.Bar);
        Assert.Empty(_dialogs.Asked);
        Assert.Equal(ViewMode.Image, fresh.Doc.Mode);
        Assert.Single(_w.Documents);

        File.WriteAllBytes(path, Pictures.Png(300, 300)[..30]); // a file that was cut while it was written
        fresh.Doc.Doc.CheckExternalChange();
        Dispatcher.UIThread.RunJobs();
        var (broken, brokenView) = await Shown();
        Assert.Equal(Loc.Get(K.ImageBroken), brokenView.Problem);
        Assert.Equal(3, broken.ModeBar!.Buttons.Count);
    }

    [AvaloniaFact]
    public async Task The_screen_reader_gets_the_mode_the_size_and_the_zoom_and_the_texts_follow_the_language()
    {
        var bytes = Pictures.Png(120, 80);
        var (tab, view) = await OpenPicture("name.png", bytes);
        string name = AutomationProperties.GetName(view);
        Assert.Contains("120 × 80 px", name);
        Assert.Contains("PNG", name);
        Assert.Contains("name.png", name);
        Assert.Contains(OpenDocument.FormatSize(bytes.Length), name);
        Assert.Contains("100 %", name);
        Assert.Equal(Loc.Get(K.ViewModeBar, ("mode", Loc.Get(K.ViewModeImage))), AutomationProperties.GetName(tab.ModeBar));
        Assert.Equal(Avalonia.Automation.Peers.AutomationControlType.Group, AutomationProperties.GetControlTypeOverride(tab.ModeBar!));
        Assert.All(tab.ModeBar!.Buttons, b => Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(b))));
        Assert.Contains("120 × 80 px", _w.AnnouncedText); // said when the picture opened

        view.ZoomBy(1);
        Assert.Contains("125", _w.AnnouncedText);
        Assert.Contains("125 %", AutomationProperties.GetName(view));

        Click(ButtonOf(tab, Loc.Get(K.ViewModeShowText)));
        Assert.Equal(Loc.Get(K.ViewModeBar, ("mode", Loc.Get(K.ViewModeText))), _w.AnnouncedText);

        Loc.SetLanguage("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["Show as picture", "Show as text", "Show as hex"], _w.Active!.ModeBar!.Buttons.Select(b => (string?)b.Content));
        Assert.Equal("View mode: Text", AutomationProperties.GetName(_w.Active.ModeBar));
        Click(ButtonOf(_w.Active, "Show as picture"));
        await Shown();
        Assert.StartsWith("Picture name.png:", AutomationProperties.GetName(_w.Active!.Custom!));
        Assert.StartsWith("Picture PNG", _w.StatusLeftText);
    }

    [AvaloniaFact]
    public async Task The_picture_its_message_and_the_markdown_tabs_pass_the_accessibility_gate_in_the_four_languages()
    {
        var run = new GateRun(_temp.Path, "pic.png", "cut.png", "page.md");
        void Walk(string scenario)
        {
            foreach (var lang in A11yAudit.Languages)
            {
                _app.SetLanguage(lang);
                Layout();
                run.Audit(scenario, lang, _w);
            }
            _app.SetLanguage(Loc.DefaultLanguage);
            Layout();
        }

        await OpenPicture("pic.png", Pictures.Png(120, 80));
        Walk("main:picture");
        await OpenPicture("cut.png", Pictures.Png()[..45]);
        Walk("main:picture-message");

        var page = OpenMarkdown("page.md", "# Title\n\ntext\n", "read");
        Layout();
        Walk("main:markdown-reading");
        Click(ButtonOf(page, Loc.Get(K.ViewModeEdit)));
        Layout();
        Walk("main:markdown-editing");
        run.AssertClean();
    }

    [AvaloniaFact]
    public async Task Keys_of_the_window_do_not_edit_a_picture_and_the_text_commands_leave_it_alone()
    {
        var (tab, view) = await OpenPicture("view-only.png", Pictures.Png());
        long revision = tab.Doc.Doc.Revision;
        view.Focus();
        TestApp.Type(_w, "abc");
        TestApp.Press(_w, Key.Back);
        TestApp.Press(_w, Key.Delete);
        TestApp.Press(_w, Key.A, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Null(_w.ActiveEditor);
        Assert.True(_w.FindBar.IsVisible == false);
    }

    // ---- Markdown ----

    private DocTab OpenMarkdown(string name, string text, string? mode = null)
    {
        if (mode is not null) _app.Workbench.Set("markdown.defaultMode", mode);
        return _w.OpenFile(_temp.File(name, text), null);
    }

    [AvaloniaFact]
    public void A_markdown_file_opens_in_reading_with_the_page_over_the_whole_tab_and_the_bar_offers_the_three_modes()
    {
        var tab = OpenMarkdown("a.md", "# Title\n\nSome **strong** words\n", "read");
        Layout();
        Assert.Equal(ViewMode.Read, tab.Doc.Mode);
        var page = Assert.IsType<MarkdownPreview>(tab.Custom);
        Assert.Null(tab.Surface);
        Assert.Contains("Title", TextOf(page));
        Assert.Contains("Some strong words", TextOf(page));
        Assert.True(page.Bounds.Width > 400 && page.Bounds.Height > 100, "the page fills the tab");
        Assert.Equal([Loc.Get(K.ViewModeRead), Loc.Get(K.ViewModeEdit), Loc.Get(K.ViewModeSource)], tab.ModeBar!.Buttons.Select(b => (string?)b.Content));
        Assert.True(tab.ModeBar.Buttons[0].IsChecked);
        Assert.Equal(Loc.Get(K.ViewModeBar, ("mode", Loc.Get(K.ViewModeRead))), AutomationProperties.GetName(tab.ModeBar));
        Assert.Contains("a.md", AutomationProperties.GetName(page));
        Assert.NotEqual("", _w.StatusLeftText);
    }

    [AvaloniaFact]
    public void Editing_shows_the_editor_with_the_page_beside_it_and_source_the_text_only_by_the_bar_the_commands_and_the_key()
    {
        var tab = OpenMarkdown("b.md", "# Title\n\ntext\n", "read");
        Click(ButtonOf(tab, Loc.Get(K.ViewModeEdit)));
        Layout();
        var edit = _w.Active!;
        Assert.Equal(ViewMode.Edit, edit.Doc.Mode);
        Assert.NotNull(edit.Surface);
        Assert.NotNull(edit.SidePreview);
        Assert.Contains("Title", TextOf(edit.SidePreview!));
        Assert.True(edit.SidePreview!.Bounds.Width > 100 && edit.Surface!.Bounds.Width > 100);
        Assert.True(edit.SidePreview.Bounds.X > edit.Surface.Bounds.X, "the page stands right of the editor");
        Assert.True(edit.ModeBar!.Buttons[1].IsChecked);

        Click(ButtonOf(edit, Loc.Get(K.ViewModeSource)));
        var source = _w.Active!;
        Assert.Equal(ViewMode.Text, source.Doc.Mode);
        Assert.NotNull(source.Surface);
        Assert.Null(source.SidePreview);
        Assert.IsNotType<MarkdownPreview>(source.Custom);
        Assert.True(source.ModeBar!.Buttons[2].IsChecked);

        // the commands and the key, one cycle: source goes to reading, then editing, then source
        Chord(Key.V);
        Assert.Equal(ViewMode.Read, _w.Active!.Doc.Mode);
        Chord(Key.V);
        Assert.Equal(ViewMode.Edit, _w.Active!.Doc.Mode);
        Chord(Key.V);
        Assert.Equal(ViewMode.Text, _w.Active!.Doc.Mode);
        Assert.Single(_w.Documents);

        var run = _app.Workbench.Commands.ExecuteAsync("nks.view.modeRead", new ShellContext(_w, forMenu: false)).AsTask();
        Dispatcher.UIThread.RunJobs();
        Assert.True(run.IsCompleted);
        Assert.Equal(ViewMode.Read, _w.Active!.Doc.Mode);
        _ = _app.Workbench.Commands.ExecuteAsync("nks.view.modeEdit", new ShellContext(_w, forMenu: false)).AsTask();
        Assert.Equal(ViewMode.Edit, _w.Active!.Doc.Mode);
        _ = _app.Workbench.Commands.ExecuteAsync("nks.view.modeSource", new ShellContext(_w, forMenu: false)).AsTask();
        Assert.Equal(ViewMode.Text, _w.Active!.Doc.Mode);
        Assert.Equal(Loc.Get(K.ViewModeBar, ("mode", Loc.Get(K.ViewModeSource))), _w.AnnouncedText);
    }

    [AvaloniaFact]
    public async Task Unsaved_text_shows_in_the_page_in_editing_and_in_reading_after_a_pause()
    {
        var tab = OpenMarkdown("c.md", "# First\n", "edit");
        Layout();
        Assert.NotNull(tab.SidePreview);
        tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "\nSome **new** words\n");
        Assert.True(tab.Doc.Doc.IsModified);
        await Until(() => TextOf(tab.SidePreview!).Contains("Some new words"), "the page to follow the edit");

        Click(ButtonOf(tab, Loc.Get(K.ViewModeRead)));
        Layout();
        var page = Assert.IsType<MarkdownPreview>(_w.Active!.Custom);
        Assert.Contains("Some new words", TextOf(page));
        Assert.True(_w.Active.Doc.Doc.IsModified); // nothing was saved by looking at it
        Assert.Equal("# First\n", File.ReadAllText(Path.Combine(_temp.Path, "c.md")));
    }

    [AvaloniaFact]
    public void Reading_does_not_edit_the_hidden_text_the_editor_commands_have_nothing_to_act_on()
    {
        var tab = OpenMarkdown("d.md", "# Keep\n", "read");
        long revision = tab.Doc.Doc.Revision;
        tab.View.Focus();
        TestApp.Type(_w, "xyz");
        TestApp.Press(_w, Key.A, TestApp.Mod);
        TestApp.Press(_w, Key.Delete);
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.Null(_w.ActiveEditor);
        Assert.False(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public void Changing_the_mode_keeps_the_place_on_the_page_and_the_selection_of_the_editor()
    {
        string text = Lines(400);
        var tab = OpenMarkdown("long.md", text, "edit");
        Layout();
        var editor = tab.Surface!;
        int selStart = text.IndexOf("Paragraph 40\n", StringComparison.Ordinal);
        editor.SetSelections([new Sel(selStart, selStart + 12)]);
        MarkdownPreview.ScrollEditorTo(editor, 300);
        Layout();
        double before = MainWindow.TopLine(editor);
        Assert.InRange(before, 299, 301);

        Click(ButtonOf(tab, Loc.Get(K.ViewModeRead)));
        Layout();
        Layout();
        var page = Assert.IsType<MarkdownPreview>(_w.Active!.Custom);
        Assert.InRange(page.TopLine, 296, 304);

        page.ScrollToLine(120);
        Layout();
        Layout();
        Assert.InRange(page.TopLine, 117, 123);
        Click(ButtonOf(_w.Active, Loc.Get(K.ViewModeSource)));
        Layout();
        Layout();
        var source = _w.Active!.Surface!;
        Assert.InRange(MainWindow.TopLine(source), 116, 124);
        var selection = source.GetSelections()[0];
        Assert.Equal((selStart, selStart + 12), (selection.Start, selection.End));
        Assert.Equal("Paragraph 40", source.SelectedText);

        Click(ButtonOf(_w.Active, Loc.Get(K.ViewModeEdit)));
        Layout();
        Layout();
        Assert.Equal("Paragraph 40", _w.Active!.Surface!.SelectedText);
        Assert.InRange(MainWindow.TopLine(_w.Active.Surface), 116, 124);
    }

    [AvaloniaFact]
    public void The_editor_and_the_page_scroll_together_in_editing()
    {
        var tab = OpenMarkdown("sync.md", Lines(400), "edit");
        Layout();
        Layout();
        MarkdownPreview.ScrollEditorTo(tab.Surface!, 250);
        Layout();
        Layout();
        Assert.InRange(tab.SidePreview!.TopLine, 245, 255);

        System.Threading.Thread.Sleep(PluginLimitsPlaceholders.PreviewSyncQuietMs + 50); // the page that just followed the editor must not send it back at once
        tab.SidePreview.ScrollToLine(100);
        Layout();
        Layout();
        Assert.InRange(MainWindow.TopLine(tab.Surface!), 96, 104);
    }

    [AvaloniaFact]
    public async Task Save_works_from_reading_after_text_was_changed_in_editing()
    {
        var tab = OpenMarkdown("save.md", "# One\n", "edit");
        tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "two\n");
        Click(ButtonOf(tab, Loc.Get(K.ViewModeRead)));
        Assert.IsType<MarkdownPreview>(_w.Active!.Custom);
        Assert.True(await _w.SaveAsync(_w.Active));
        Assert.Equal("# One\ntwo\n", File.ReadAllText(Path.Combine(_temp.Path, "save.md")));
        Assert.False(_w.Active.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public void The_mode_of_a_file_is_kept_for_the_session_and_the_setting_gives_the_default_to_files_opened_afterwards()
    {
        string a = _temp.File("keep.md", "# A\n");
        var tab = _w.OpenFile(a, null);
        Assert.Equal(ViewMode.Text, tab.Doc.Mode); // the harness default is source
        Click(ButtonOf(tab, Loc.Get(K.ViewModeRead)));
        Assert.Equal(ViewMode.Read, _w.Active!.Doc.Mode);
        Click(ButtonOf(_w.Active, Loc.Get(K.ViewModeEdit)));
        _w.CloseTab(_w.Active!);
        Assert.Empty(_w.Documents);

        Assert.Equal(ViewMode.Edit, _w.OpenFile(a, null).Doc.Mode); // the same file comes back as it was left
        Assert.Equal(ViewMode.Text, _w.OpenFile(_temp.File("other.md", "# B\n"), null).Doc.Mode); // another file has the default

        _app.Workbench.Set("markdown.defaultMode", "read");
        Assert.Equal(ViewMode.Read, _w.OpenFile(_temp.File("r.md", "# C\n"), null).Doc.Mode);
        _app.Workbench.Set("markdown.defaultMode", "edit");
        var edit = _w.OpenFile(_temp.File("e.md", "# D\n"), null);
        Assert.Equal(ViewMode.Edit, edit.Doc.Mode);
        Assert.NotNull(edit.SidePreview);
        Assert.Equal(ViewMode.Edit, _w.OpenFile(a, null).Doc.Mode); // the open tab of the file is shown, not made again
    }

    [AvaloniaFact]
    public void The_default_of_the_product_is_reading_and_a_file_that_is_not_markdown_has_no_modes()
    {
        Assert.Equal("read", DefaultMode());
        var plain = _w.OpenFile(_temp.File("plain.txt", "text"), null);
        Assert.Null(plain.ModeBar);
        Assert.Equal(ViewMode.Text, plain.Doc.Mode);
        var cs = _w.OpenFile(_temp.File("code.cs", "class A {}"), null);
        Assert.Null(cs.ModeBar);
    }

    private static string DefaultMode() => NksHub.Textida.Workbench.WorkbenchHost.CreateBuiltin().Settings.Definitions.Single(d => d.Key == "markdown.defaultMode").Default?.ToString() ?? "";

    [AvaloniaFact]
    public void A_markdown_file_above_the_preview_limit_opens_as_source_with_a_message_and_reading_is_refused()
    {
        string big = string.Concat(Enumerable.Repeat(new string('a', 99) + "\n", PluginLimitsPlaceholders.PreviewLiveMaxChars / 100 + 200));
        Assert.True(big.Length > PluginLimitsPlaceholders.PreviewLiveMaxChars);
        var tab = OpenMarkdown("big.md", big, "read");
        string message = Loc.Get(K.ViewModeUnavailable, ("limit", PluginLimitsPlaceholders.PreviewLiveMaxChars));
        Assert.Equal(ViewMode.Text, tab.Doc.Mode);
        Assert.NotNull(tab.Surface);
        Assert.Equal(message, _w.StatusMessageText);
        Assert.Equal(message, _w.AnnouncedText);

        Click(ButtonOf(tab, Loc.Get(K.ViewModeRead)));
        Assert.Equal(ViewMode.Text, _w.Active!.Doc.Mode);
        Assert.Equal(message, _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void A_markdown_file_opened_as_a_large_file_is_source_only_with_the_message_and_no_bar()
    {
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        string big = string.Concat(Enumerable.Repeat(new string('b', 99) + "\n", 11_000));
        var tab = OpenMarkdown("paged.md", big, "read");
        Assert.True(tab.Doc.IsLarge);
        Assert.NotNull(tab.Paged);
        Assert.Equal(ViewMode.Text, tab.Doc.Mode);
        Assert.Null(tab.ModeBar);
        Assert.Equal(Loc.Get(K.ViewModeUnavailable, ("limit", PluginLimitsPlaceholders.PreviewLiveMaxChars)), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public void A_file_that_stops_being_markdown_loses_the_page_and_a_new_markdown_document_gets_the_bar()
    {
        var tab = _w.NewDocument();
        Assert.Null(tab.ModeBar);
        tab.Doc.ChooseLanguage(Editing.LanguageModes.ById("markdown"));
        Assert.Equal(3, _w.Active!.ModeBar!.Buttons.Count);
        Click(ButtonOf(_w.Active, Loc.Get(K.ViewModeEdit)));
        Assert.NotNull(_w.Active!.SidePreview);
        _w.Active.Doc.ChooseLanguage(Editing.LanguageModes.ById("csharp"));
        Assert.Equal(ViewMode.Text, _w.Active!.Doc.Mode);
        Assert.Null(_w.Active.SidePreview);
        Assert.Null(_w.Active.ModeBar);
    }

    [AvaloniaFact]
    public void Reading_runs_no_script_and_fetches_nothing_from_the_web_without_consent()
    {
        var tab = OpenMarkdown("safe.md", "![chart](https://remote.example/a.png) and ![local](file:///C:/private/a.png)\n\n<script>alert(1)</script>\n\n[open](file:///C:/private/a.txt)\n", "read");
        Layout();
        var page = (MarkdownPreview)tab.Custom!;
        string text = TextOf(page);
        Assert.Contains("<script>alert(1)</script>", text);
        Assert.Contains(Loc.Get(K.PreviewImageRemote, ("host", "remote.example"), ("alt", "chart")), text);
        Assert.Contains(Loc.Get(K.PreviewPanelImage, ("alt", "local")), text);
        Assert.DoesNotContain(Tree(page), c => c is Image);
        Assert.Single(Tree(page).OfType<Button>()); // the question about the host, and no button for a link to a local file
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public void A_line_to_go_to_opens_a_markdown_file_as_source_and_moves_a_file_in_reading_to_source()
    {
        _app.Workbench.Set("markdown.defaultMode", "read");
        string path = _temp.File("jump.md", "one\n\ntwo\n\nthree\n");
        var tab = _w.OpenFile(path, 3);
        Assert.Equal(ViewMode.Text, tab.Doc.Mode);
        Assert.Equal(3, tab.Editor!.Document.GetLineByOffset(tab.Editor.CaretOffset).LineNumber);

        string other = _temp.File("jump2.md", "a\n\nb\n\nc\n");
        var reading = _w.OpenFile(other, null);
        Assert.Equal(ViewMode.Read, reading.Doc.Mode);
        var jumped = _w.OpenFile(other, 5);
        Assert.Equal(ViewMode.Text, jumped.Doc.Mode);
        Assert.Equal(5, jumped.Editor!.Document.GetLineByOffset(jumped.Editor.CaretOffset).LineNumber);
        Assert.Equal(2, _w.Documents.Count);
    }

    [AvaloniaFact]
    public void Hex_returns_a_markdown_file_to_the_mode_it_came_from()
    {
        var tab = OpenMarkdown("hexback.md", "# Title\n", "read");
        Chord(Key.H);
        Assert.Equal(ViewMode.Hex, tab.Doc.Mode);
        Assert.NotNull(_w.Active!.Hex);
        Assert.Equal(3, _w.Active.ModeBar!.Buttons.Count);
        Assert.All(_w.Active.ModeBar.Buttons, b => Assert.False(b.IsChecked));
        Chord(Key.H);
        Assert.Equal(ViewMode.Read, tab.Doc.Mode);
        Assert.IsType<MarkdownPreview>(_w.Active!.Custom);
        Chord(Key.H);
        Chord(Key.V); // from hex the cycle goes to the first mode of the bar
        Assert.Equal(ViewMode.Read, tab.Doc.Mode);
    }

    [AvaloniaFact]
    public void A_split_of_the_document_shows_the_same_mode_in_both_tabs_and_the_menu_lists_the_modes()
    {
        var tab = OpenMarkdown("split.md", "# Split\n", "edit");
        var second = _w.SplitView()!;
        Assert.Equal(ViewMode.Edit, second.Doc.Mode);
        Assert.NotNull(second.SidePreview);
        Click(ButtonOf(second, Loc.Get(K.ViewModeRead)));
        Assert.All(_w.Tabs, t => Assert.IsType<MarkdownPreview>(t.Custom));
        Assert.Equal(2, _w.Tabs.Count());

        var strings = _app.Workbench.Strings;
        var modes = _w.MenuModel.Single(g => g.Label == strings.Get("menu.view")).Items.OfType<MenuGroup>().Single(g => g.Label == strings.Get("submenu.mode"));
        var ids = modes.Items.OfType<MenuCommand>().Select(c => c.CommandId).ToList();
        Assert.Equal(["nks.view.modeRead", "nks.view.modeEdit", "nks.view.modeSource"], ids.TakeLast(3));
        Assert.Contains("nks.view.cycleMode", ids);
    }
}
