using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

public sealed class SyntaxColorizerTests(SyntaxTestSession session) : IClassFixture<SyntaxTestSession>
{
    [Fact]
    public Task Tokens_color_UTF16_columns_after_tabs_and_surrogates_without_changing_selection() => session.Run(() =>
    {
        var editor = new TextEditor { Text = "\t😀 \"text\" // č", Foreground = Brushes.Black };
        var view = editor.TextArea.TextView;
        using var colorizer = Attach(view);
        var version = editor.Document.Version;
        editor.Select(1, 7);
        var selected = editor.SelectedText;
        Assert.True(colorizer.SetTokens(version, Tokens(editor.Document)));
        var line = Render(view);

        Assert.Equal(Color.Parse("#0451A5"), ForegroundAt(line, 5));
        Assert.Equal(Color.Parse("#008000"), ForegroundAt(line, 12));
        Assert.Equal(Colors.Black, ForegroundAt(line, 1));
        Assert.Equal(selected, editor.SelectedText);
        Assert.Equal(1, editor.SelectionStart);
        Assert.Equal(7, editor.SelectionLength);
        Assert.Equal(0, version.CompareAge(editor.Document.Version));
        Assert.All(line.Elements, element => Assert.Null(element.TextRunProperties.BackgroundBrush));
    });

    [Fact]
    public Task Editing_keeps_clamped_colors_and_rejects_an_outdated_token_result() => session.Run(() =>
    {
        var view = CreateView(new TextDocument("\"old\""));
        using var colorizer = Attach(view);
        var version = view.Document.Version;
        var tokens = Tokens(view.Document);
        Assert.True(colorizer.SetTokens(version, tokens));
        Assert.Equal(Color.Parse("#0451A5"), ForegroundAt(Render(view), 1));

        view.Document.Text = "ab";
        Assert.False(colorizer.SetTokens(version, tokens));
        Assert.Equal(Color.Parse("#0451A5"), ForegroundAt(Render(view), 1));
        colorizer.ClearTokens();
        Assert.Equal(Colors.Black, ForegroundAt(Render(view), 1));
    });

    [Fact]
    public Task Rebinding_rejects_the_old_document_and_detaches_its_change_event() => session.Run(() =>
    {
        var previous = new TextDocument("\"old\"");
        var current = new TextDocument("\"new\"");
        var view = CreateView(previous);
        using var colorizer = Attach(view);
        var previousTokens = Tokens(previous);
        view.Document = current;
        colorizer.BindDocument(current);
        Assert.False(colorizer.SetTokens(previous.Version, previousTokens));
        Assert.True(colorizer.SetTokens(current.Version, Tokens(current)));
        previous.Text = "changed";
        Assert.Equal(Color.Parse("#0451A5"), ForegroundAt(Render(view), 1));
    });

    [Fact]
    public Task Theme_and_language_changes_redraw_prepared_lines_and_dispose_removes_the_transformer() => session.Run(() =>
    {
        var view = CreateView(new TextDocument("\"text\""));
        var colorizer = Attach(view);
        Assert.True(colorizer.SetTokens(view.Document.Version, Tokens(view.Document)));
        Assert.Equal(Color.Parse("#0451A5"), ForegroundAt(Render(view), 1));
        colorizer.Palette = SyntaxPalette.Dark;
        Assert.Equal(Color.Parse("#CE9178"), ForegroundAt(Render(view), 1));
        colorizer.ClearTokens();
        Assert.Equal(Colors.Black, ForegroundAt(Render(view), 1));
        Assert.True(colorizer.SetTokens(view.Document.Version, Tokens(view.Document)));
        colorizer.Dispose();
        colorizer.Dispose();
        Assert.DoesNotContain(colorizer, view.LineTransformers);
        Assert.Equal(Colors.Black, ForegroundAt(Render(view), 1));
        view.Document.Text = "after dispose";
    });

    [Fact]
    public Task Tokens_are_copied_and_invalid_ranges_do_not_replace_the_current_viewport() => session.Run(() =>
    {
        var view = CreateView(new TextDocument("abc"));
        using var colorizer = Attach(view);
        var state = BuiltinGrammars.Tokenizer("csharp").InitialState;
        var mutable = new List<Token> { new(0, 3, "string.escape.custom") };
        Assert.True(colorizer.SetTokens(view.Document.Version, [new(0, mutable, state, state)]));
        mutable.Clear();
        Assert.Equal(Color.Parse("#0451A5"), ForegroundAt(Render(view), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => colorizer.SetTokens(view.Document.Version,
            [new(0, [new(2, int.MaxValue, "comment")], state, state)]));
        Assert.Equal(Color.Parse("#0451A5"), ForegroundAt(Render(view), 1));
        Assert.Null(SyntaxPalette.Light.GetBrush("unknown.token"));
    });

    private static SyntaxColorizer Attach(TextView view)
    {
        var colorizer = new SyntaxColorizer(view);
        colorizer.BindDocument(view.Document);
        view.LineTransformers.Add(colorizer);
        return colorizer;
    }

    private static TextView CreateView(TextDocument document)
    {
        var view = new TextView { Document = document };
        view.SetValue(Avalonia.Controls.Documents.TextElement.ForegroundProperty, Brushes.Black);
        return view;
    }

    private static IReadOnlyList<LineTokens> Tokens(TextDocument document) =>
        new IncrementalTokenizer(BuiltinGrammars.Tokenizer("csharp"), new Lines(document)).TokenizeViewport(0, document.LineCount);

    private static VisualLine Render(TextView view)
    {
        view.Measure(new Size(800, 300));
        view.Arrange(new Rect(0, 0, 800, 300));
        view.EnsureVisualLines();
        return view.GetVisualLine(1)!;
    }

    private static Color ForegroundAt(VisualLine line, int column) =>
        ((ISolidColorBrush)line.Elements.Single(element =>
            element.RelativeTextOffset <= column && column < element.RelativeTextOffset + element.DocumentLength)
            .TextRunProperties.ForegroundBrush!).Color;

    private sealed class Lines(TextDocument document) : ILineSource
    {
        public long LineCount => document.LineCount;
        public string GetLine(long line)
        {
            var span = document.GetLineByNumber((int)line + 1);
            return document.GetText(span.Offset, span.Length);
        }
    }
}

public sealed class SyntaxTestSession : IDisposable
{
    private readonly HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(SyntaxTestApplication));
    public Task Run(Action action) => session.Dispatch(action, CancellationToken.None);
    public void Dispose() => session.Dispose();
}

public static class SyntaxTestApplication
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
