using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureGraphThemeTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly App app;
    private readonly MainWindow window;

    public StructureGraphThemeTests()
    {
        app = TestApp.Prepare(temporary, new ScriptedDialogs());
        window = app.CreateWindow(null);
        window.Width = 1000; window.Height = 700;
        window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        temporary.Dispose();
    }

    [AvaloniaTheory]
    [InlineData("json", "{\"alpha\":17}")]
    [InlineData("jsonc", "{/* comment */\"alpha\":17}")]
    [InlineData("xml", "<root><alpha>17</alpha></root>")]
    [InlineData("yaml", "alpha: 17\n")]
    [InlineData("neon", "alpha: 17\n")]
    public async Task Graph_draws_the_current_theme_for_canvas_cards_text_and_selection_after_switching_themes(string extension, string text)
    {
        window.RequestedThemeVariant = ThemeVariant.Dark;
        var tab = window.OpenFile(temporary.File("sample." + extension, text), null);
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Graph));
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Drain(view);
        var graph = view.Graph;
        var node = view.Tree.VisibleNodes.First(item => item.Kind == StructureTreeNodeKind.Normal);

        foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = variant;
            await Drain(view);
            Assert.Equal(variant, graph.ActualThemeVariant);
            Color background = ResourceColor(graph, "SystemControlBackgroundAltHighBrush", variant);
            Color foreground = ResourceColor(graph, "Fg", variant);
            Assert.Equal(foreground, ColorOf(TextElement.GetForeground(graph)));

            view.Tree.SelectedNode = null;
            var drawings = Draw(graph);
            var fills = drawings.OfType<GeometryDrawing>().Where(item => item.Brush is not null).ToArray();
            Assert.Equal(graph.DrawnCards + 1, fills.Length);
            Assert.All(fills, item => Assert.Equal(background, ColorOf(item.Brush)));
            AssertTextColor(drawings, foreground);

            view.Tree.SelectedNode = node;
            drawings = Draw(graph);
            fills = drawings.OfType<GeometryDrawing>().Where(item => item.Brush is not null).ToArray();
            Assert.Equal(background, ColorOf(fills[0].Brush));
            var selected = Assert.Single(fills.Skip(1), item => ColorOf(item.Brush) == Color.Parse("#3080A8FF"));
            Assert.Equal(foreground, ColorOf(selected.Pen!.Brush));
            Assert.All(fills.Skip(1).Where(item => !ReferenceEquals(item, selected)), item => Assert.Equal(background, ColorOf(item.Brush)));
            AssertTextColor(drawings, foreground);
        }
    }

    private async Task Drain(StructureView view)
    {
        await StructureLayoutTests.Drain(view, window);
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var current = view.Graph.LayoutWork;
            try { await current; } catch (OperationCanceledException) { }
            StructureLayoutTests.Layout(window);
            if (ReferenceEquals(current, view.Graph.LayoutWork) && view.Graph.LoadedCards > 0) return;
        }
        Assert.Fail("The graph layout did not settle.");
    }

    private static Drawing[] Draw(StructureGraph graph)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) graph.Render(context);
        return Flatten(group).ToArray();
    }

    private static IEnumerable<Drawing> Flatten(Drawing drawing)
        => drawing is DrawingGroup group ? group.Children.SelectMany(Flatten) : [drawing];

    private static void AssertTextColor(Drawing[] drawings, Color expected)
    {
        var text = drawings.OfType<GlyphRunDrawing>().ToArray();
        Assert.NotEmpty(text);
        Assert.All(text, item => Assert.Equal(expected, ColorOf(item.Foreground)));
    }

    private static Color ResourceColor(Control control, string key, ThemeVariant variant)
    {
        Assert.True(control.TryFindResource(key, variant, out var value), key);
        return ColorOf(Assert.IsAssignableFrom<IBrush>(value));
    }

    private static Color ColorOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
