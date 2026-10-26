using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using AvaloniaEdit;
using Avalonia.Markup.Xaml.Styling;

[assembly: AvaloniaTestApplication(typeof(NksHub.Textida.Ime.Tests.TestAppBuilder))]

namespace NksHub.Textida.Ime.Tests;

public sealed class ImeTestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AvaloniaEdit")) { Source = new Uri("avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml") });
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ImeTestApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// What the platform input layer sees from the editor surface. Avalonia.Win32 (IMM32) and Avalonia.Native (NSTextInputClient)
/// ask the focused control for a <see cref="TextInputMethodClient"/> and decide from <c>SupportsPreedit</c> who draws the
/// composition text. A headless run cannot start a real IME, so these tests drive the client exactly as those layers do:
/// preedit updates, commit through TextInput, cancel, and caret-follow for the position of the system candidate window.
/// </summary>
public class ImeContractTests(Xunit.ITestOutputHelper output)
{
    private static (Window Window, TextEditor Editor, TextInputMethodClient Client) Open(string text = "")
    {
        var editor = new TextEditor { Text = text };
        var window = new Window { Width = 600, Height = 300, Content = editor };
        window.Show();
        editor.TextArea.Focus();
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        editor.TextArea.RaiseEvent(args);
        Assert.NotNull(args.Client);
        return (window, editor, args.Client!);
    }

    [AvaloniaFact]
    public void Editor_surface_hands_out_a_client_and_reports_no_inline_preedit()
    {
        var (window, editor, client) = Open("abc");
        output.WriteLine($"AvaloniaEdit TextArea client: {client.GetType().Name} SupportsPreedit={client.SupportsPreedit} SupportsSurroundingText={client.SupportsSurroundingText}");
        Assert.False(client.SupportsPreedit);
        Assert.True(client.SupportsSurroundingText);
        window.Close();
    }

    [AvaloniaFact]
    public void Textbox_client_of_the_current_app_for_comparison()
    {
        var box = new TextBox { Text = "abc" };
        var window = new Window { Width = 300, Height = 100, Content = box };
        window.Show();
        box.Focus();
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        box.RaiseEvent(args);
        output.WriteLine($"TextBox client: {args.Client?.GetType().Name} SupportsPreedit={args.Client?.SupportsPreedit}");
        Assert.NotNull(args.Client);
        window.Close();
    }

    [AvaloniaFact]
    public void Preedit_updates_do_not_touch_the_document()
    {
        var (window, editor, client) = Open("abc");
        editor.CaretOffset = 3;
        client.SetPreeditText("ni");
        client.SetPreeditText("nih");
        Assert.Equal("abc", editor.Text);
        client.SetPreeditText(null);
        Assert.Equal("abc", editor.Text);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData("é")]            // Czech/French dead-key composition result
    [InlineData("日本")]      // Japanese conversion result
    [InlineData("你好")]      // Chinese pinyin conversion result
    public void Commit_after_composition_inserts_once_and_undoes_in_one_step(string committed)
    {
        var (window, editor, client) = Open("ab");
        editor.CaretOffset = 1;
        client.SetPreeditText("x");
        client.SetPreeditText("xy");
        client.SetPreeditText(null);
        window.KeyTextInput(committed);
        Assert.Equal("a" + committed + "b", editor.Text);
        Assert.Equal(1 + committed.Length, editor.CaretOffset);
        editor.Undo();
        Assert.Equal("ab", editor.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Cancelled_composition_leaves_no_text()
    {
        var (window, editor, client) = Open("ab");
        editor.CaretOffset = 2;
        client.SetPreeditText("ni");
        client.SetPreeditText(null);
        Assert.Equal("ab", editor.Text);
        Assert.Equal(2, editor.CaretOffset);
        window.Close();
    }

    [AvaloniaFact]
    public void Commit_replaces_the_selection_once()
    {
        var (window, editor, client) = Open("hello world");
        editor.Select(6, 5);
        client.SetPreeditText("sh");
        window.KeyTextInput("世界");
        Assert.Equal("hello 世界", editor.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void Cursor_rectangle_follows_the_caret_so_the_system_window_lands_at_the_composition()
    {
        var (window, editor, client) = Open("first line\nsecond line");
        int changes = 0;
        client.CursorRectangleChanged += (_, _) => changes++;
        editor.CaretOffset = 0;
        var atStart = client.CursorRectangle;
        editor.CaretOffset = 5;
        var moved = client.CursorRectangle;
        editor.CaretOffset = 15;
        var nextLine = client.CursorRectangle;
        output.WriteLine($"CursorRectangle: start={atStart} col5={moved} line2={nextLine}; CursorRectangleChanged raised {changes}x");
        Assert.True(moved.X > atStart.X);
        Assert.True(nextLine.Y > moved.Y);
        Assert.True(changes >= 2);
        window.Close();
    }

    [AvaloniaFact]
    public void Surrounding_text_and_selection_are_available_for_reconversion()
    {
        var (window, editor, client) = Open("first line\nsecond line");
        editor.CaretOffset = 14;
        output.WriteLine($"SurroundingText='{client.SurroundingText}' Selection={client.Selection}");
        Assert.Contains("second", client.SurroundingText);
        window.Close();
    }

    /// <summary>Wraps the stock client and turns preedit on, which is the whole switch the platform layers look at.</summary>
    private sealed class PreeditClient : TextInputMethodClient
    {
        private readonly TextInputMethodClient _inner;
        public string? Preedit { get; private set; }
        public int? PreeditCursor { get; private set; }

        public PreeditClient(TextInputMethodClient inner)
        {
            _inner = inner;
            inner.CursorRectangleChanged += (_, e) => RaiseCursorRectangleChanged();
            inner.SurroundingTextChanged += (_, e) => RaiseSurroundingTextChanged();
            inner.SelectionChanged += (_, e) => RaiseSelectionChanged();
            inner.TextViewVisualChanged += (_, e) => RaiseTextViewVisualChanged();
        }

        public override Visual TextViewVisual => _inner.TextViewVisual;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => _inner.SupportsSurroundingText;
        public override string SurroundingText => _inner.SurroundingText;
        public override Rect CursorRectangle => _inner.CursorRectangle;
        public override TextSelection Selection { get => _inner.Selection; set => _inner.Selection = value; }
        public override void SetPreeditText(string? preeditText) => Preedit = preeditText;
        public override void SetPreeditText(string? preeditText, int? cursorPos) { Preedit = preeditText; PreeditCursor = cursorPos; }
    }

    [AvaloniaFact]
    public void A_handler_on_the_text_area_can_replace_the_client_to_switch_preedit_on()
    {
        var (window, editor, stock) = Open("abc");
        editor.TextArea.AddHandler(InputElement.TextInputMethodClientRequestedEvent,
            (object? _, TextInputMethodClientRequestedEventArgs e) => { if (e.Client is { } inner and not PreeditClient) e.Client = new PreeditClient(inner); },
            RoutingStrategies.Bubble, handledEventsToo: true);
        var args = new TextInputMethodClientRequestedEventArgs { RoutedEvent = InputElement.TextInputMethodClientRequestedEvent };
        editor.TextArea.RaiseEvent(args);
        var client = Assert.IsType<PreeditClient>(args.Client);
        client.SetPreeditText("ni", 2);
        Assert.True(client.SupportsPreedit);
        Assert.Equal("ni", client.Preedit);
        Assert.Equal("abc", editor.Text);
        window.KeyTextInput("你");
        Assert.Equal("你abc", editor.Text);
        window.Close();
    }
}
