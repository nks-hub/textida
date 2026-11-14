using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class PagedLongLineFormattingTests(ITestOutputHelper output)
{
    [AvaloniaFact]
    public void Selection_and_caret_redraws_of_the_same_long_line_allocate_at_most_250_kb_per_render()
    {
        bool tracing = TypingTrace.Enabled;
        try
        {
            TypingTrace.Enabled = false;
            using var host = new Host();
            for (int i = 0; i < 4; i++) { host.Paged.SelectBytes(i + 1, i + 2); host.Draw(); }
            TypingTrace.Reset();
            TypingTrace.Enabled = true;
            const int draws = 10;
            long revision = host.Tab.Doc.Doc.Revision;
            for (int i = 0; i < draws; i++)
            {
                long before = host.Paged.RenderedAt;
                host.Paged.SelectBytes(2 + i, 3 + i);
                host.Draw();
                Assert.True(host.Paged.RenderedAt > before);
            }
            string summary = TypingTrace.Summary();
            output.WriteLine(summary);
            var render = Regex.Match(summary, @"(?m)^paged\.render\s+n=\s*(\d+)[^\r\n]*\balloc=(\d+)");
            Assert.True(render.Success, "The actual paged render span was not recorded.");
            Assert.True(int.Parse(render.Groups[1].Value, CultureInfo.InvariantCulture) >= draws);
            long allocated = long.Parse(render.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.InRange(allocated, 0, 250_000);
            Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
        }
        finally { TypingTrace.Enabled = tracing; TypingTrace.Reset(); }
    }

    [AvaloniaFact]
    public void Replacing_or_clearing_tokens_and_changing_theme_updates_the_drawn_long_line()
    {
        using var host = new Host();
        var pink = Color.Parse("#D412A7");
        var blue = Color.Parse("#1279D4");
        host.App.Workbench.Set(EditorThemes.ColorsSetting, "keyword=#D412A7");
        host.Tokens([new Token(0, 24, "keyword")]);
        Assert.True(Count(host.Frame(), pink) > 10);

        host.Tokens([new Token(0, 24, "number")]);
        Assert.Equal(0, Count(host.Frame(), pink));
        host.Tokens([new Token(0, 24, "keyword")]);
        Assert.True(Count(host.Frame(), pink) > 10);
        host.ClearTokens();
        Assert.Null(host.Paged.TokensOf(0));
        Assert.Equal(0, Count(host.Frame(), pink));

        host.Tokens([new Token(0, 24, "keyword")]);
        Assert.True(Count(host.Frame(), pink) > 10);
        host.App.Workbench.Set(EditorThemes.ColorsSetting, "keyword=#1279D4");
        var recolored = host.Frame();
        Assert.Equal(0, Count(recolored, pink));
        Assert.True(Count(recolored, blue) > 10);
    }

    [AvaloniaFact]
    public void Font_size_and_document_revision_change_the_rendered_prefix()
    {
        using var host = new Host();
        byte[] original = host.Frame();
        host.App.Workbench.Set("editor.fontSize", JsonValue.Create(24));
        byte[] enlarged = host.Frame();
        Assert.False(original.AsSpan().SequenceEqual(enlarged));

        long revision = host.Tab.Doc.Doc.Revision;
        host.Paged.SelectBytes(0, 24);
        Assert.True(host.Paged.PasteText("Changed prefix"));
        host.Paged.SelectBytes(0, 0);
        Assert.True(host.Tab.Doc.Doc.Revision > revision);
        Assert.StartsWith("Changed prefix", host.Paged.DisplayOf(0));
        Assert.False(enlarged.AsSpan().SequenceEqual(host.Frame()));
    }

    [AvaloniaFact]
    public void Preedit_uses_the_original_composition_path_and_clearing_it_restores_the_same_text()
    {
        using var host = new Host();
        var requested = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        host.Paged.RaiseEvent(requested);
        var client = Assert.IsAssignableFrom<TextInputMethodClient>(requested.Client);
        byte[] original = host.Frame();
        long revision = host.Tab.Doc.Doc.Revision;

        client.SetPreeditText("Ž");
        Assert.False(original.AsSpan().SequenceEqual(host.Frame()));
        client.SetPreeditText(null);
        Assert.True(original.AsSpan().SequenceEqual(host.Frame()));
        Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
    }

    [AvaloniaFact]
    public void Tabs_and_visible_whitespace_keep_their_display_columns_and_selection_bytes()
    {
        using var host = new Host("a\tb " + new string('x', 2 << 20));
        host.Paged.ApplyOptions(EditorOptions.From(host.App.Workbench.Settings) with { TabSize = 4 });
        host.Draw();
        Assert.StartsWith("a   b ", host.Paged.DisplayOf(0));
        host.Paged.SelectBytes(1, 2);
        Assert.Equal("\t", host.Paged.SelectedText());
        host.Paged.SelectBytes(0, 0);
        byte[] plain = host.Frame();
        host.Paged.ApplyOptions(EditorOptions.From(host.App.Workbench.Settings) with { TabSize = 4, ShowWhitespace = true });
        Assert.False(plain.AsSpan().SequenceEqual(host.Frame()));
        host.Paged.ApplyOptions(EditorOptions.From(host.App.Workbench.Settings) with { TabSize = 4 });
        Assert.True(plain.AsSpan().SequenceEqual(host.Frame()));
        Assert.StartsWith("a   b ", host.Paged.DisplayOf(0));
    }

    private static int Count(byte[] pixels, Color color)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] == color.R && pixels[i + 1] == color.G && pixels[i + 2] == color.B) count++;
        return count;
    }

    private sealed class Host : IDisposable
    {
        private readonly TempDir temp = new();
        private readonly MainWindow window;
        public App App { get; }
        public DocTab Tab { get; }
        public PagedTextView Paged => Tab.Paged!;

        public Host(string? text = null)
        {
            App = TestApp.Prepare(temp);
            App.History.Policy = new Core.History.HistoryPolicy { Enabled = false };
            App.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
            window = App.CreateWindow(null);
            window.Width = 900; window.Height = 500;
            Tab = window.OpenFile(temp.Bytes("long.txt", Encoding.UTF8.GetBytes((text ?? new string('x', 2 << 20)) + "\nnext\n")), null);
            Assert.NotNull(Tab.Paged);
            Assert.True(Tab.Doc.IsLarge);
            for (int i = 0; i < 400 && !Paged.Index.Complete; i++) { Thread.Sleep(10); Dispatcher.UIThread.RunJobs(); }
            Assert.True(Paged.Index.Complete);
            Paged.Focus();
            Draw();
        }

        public void Draw()
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
        }

        public byte[] Frame()
        {
            Draw();
            using var frame = Assert.IsType<WriteableBitmap>(window.CaptureRenderedFrame());
            using var buffer = frame.Lock();
            var origin = Paged.TranslatePoint(default, window)!.Value;
            double scale = frame.PixelSize.Width / frame.Size.Width;
            int x = (int)(origin.X * scale), y = (int)(origin.Y * scale);
            int width = Math.Min((int)(Paged.Bounds.Width * scale), buffer.Size.Width - x);
            int height = Math.Min((int)(Paged.Bounds.Height * scale), buffer.Size.Height - y);
            var pixels = new byte[width * height * 4];
            for (int row = 0; row < height; row++)
                Marshal.Copy(buffer.Address + (y + row) * buffer.RowBytes + x * 4, pixels, row * width * 4, width * 4);
            if (frame.Format == Avalonia.Platform.PixelFormat.Bgra8888)
                for (int at = 0; at < pixels.Length; at += 4) (pixels[at], pixels[at + 2]) = (pixels[at + 2], pixels[at]);
            return pixels;
        }

        public void Tokens(Token[] tokens)
        {
            var state = BuiltinGrammars.Tokenizer("json").InitialState;
            var doc = Tab.Doc.Doc;
            var viewport = new SyntaxViewport(doc.Id, doc.Revision, [new LineTokens(0, tokens, state, state)], SyntaxViewportStatus.Complete, 1);
            typeof(PagedTextView).GetMethod("ApplyTokens", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Paged, [viewport]);
        }

        public void ClearTokens() => typeof(PagedTextView).GetMethod("ClearTokens", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Paged, null);

        public void Dispose() { TestApp.Cleanup(App); Loc.SetLanguage(Loc.DefaultLanguage); temp.Dispose(); }
    }
}
