using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class MarkdownStateTests : IDisposable
{
    private readonly TempDir temp = new();
    private readonly App app;
    private readonly MainWindow window;

    public MarkdownStateTests()
    {
        app = TestApp.Prepare(temp, new ScriptedDialogs());
        window = app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        temp.Dispose();
    }

    private static IEnumerable<Control> Tree(Control body) => new[] { body }.Concat(body.GetLogicalDescendants().OfType<Control>());
    private static string Text(TextBlock block) => block.Inlines is { Count: > 0 } inlines
        ? string.Concat(inlines.OfType<Run>().Select(run => run.Text)) : block.Text ?? "";

    private async Task Ready(MarkdownPreview page)
    {
        await page.RenderTask;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public void Table_of_contents_fragments_use_the_same_unique_targets_as_the_rendered_headings()
    {
        const string source = "# A\n\n# A\n\n# A-1\n";
        var headings = MarkdownTools.Headings(source.Split('\n'));
        var toc = MarkdownTools.TocLines(headings);
        var plan = PreviewRenderer.PrepareMarkdown(source + "\n" + string.Join('\n', toc));
        Assert.Equal(["a", "a-1", "a-1-1"], plan.Targets.Keys);
        Assert.Equal(headings.Select(heading => heading.Line), plan.Targets.Values);
        var jumped = new List<int>();
        var external = new List<Uri>();
        var body = PreviewRenderer.RenderMarkdown(plan, new PreviewRenderer.Ctx(new FontFamily("Consolas"), external.Add, JumpToLine: jumped.Add));
        var host = new Window { Width = 600, Height = 500, Content = body };
        try
        {
            host.Show();
            host.UpdateLayout();
            var links = Tree(body).OfType<Button>().ToArray();
            Assert.Equal(3, links.Length);
            foreach (var link in links) link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(headings.Select(heading => heading.Line), jumped);
            Assert.Empty(external);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public async Task An_unchanged_paragraph_keeps_its_selection_after_refresh_and_an_edit_before_it()
    {
        var tab = window.OpenFile(temp.File("selection.md", "# Heading\n\nKeep this phrase\n\nLast\n"), null);
        window.SetViewMode(tab.Doc, ViewMode.Edit);
        tab = window.Active!;
        var page = tab.SidePreview!;
        await Ready(page);
        SelectableTextBlock Paragraph() => Assert.Single(Tree(page).OfType<SelectableTextBlock>(), block => Text(block) == "Keep this phrase");
        var selected = Paragraph();
        selected.SelectionStart = 5;
        selected.SelectionEnd = 9;

        page.Refresh(force: true);
        await Ready(page);
        Assert.Equal((5, 9), (Paragraph().SelectionStart, Paragraph().SelectionEnd));
        Assert.NotSame(selected, Paragraph());

        tab.Editor!.Document.Insert(0, "Introduction\n\n");
        page.Refresh();
        await Ready(page);
        Assert.Equal((5, 9), (Paragraph().SelectionStart, Paragraph().SelectionEnd));

        int phrase = tab.Editor.Document.Text.IndexOf("Keep this phrase", StringComparison.Ordinal);
        tab.Editor.Document.Replace(phrase, "Keep this phrase".Length, "A replaced paragraph");
        page.Refresh();
        await Ready(page);
        Assert.All(Tree(page).OfType<SelectableTextBlock>(), block => Assert.Equal(block.SelectionStart, block.SelectionEnd));
    }

    [AvaloniaFact]
    public async Task Replaced_or_closed_previews_cannot_be_restored_by_a_late_parse()
    {
        var tab = window.NewDocument();
        tab.Doc.ChooseLanguage(LanguageModes.ById("markdown"));
        tab.Editor!.Document.Text = string.Concat(Enumerable.Repeat("Old *content*\n\n", 800));
        window.ShowLivePreview(outline: false);
        Task old = window.PreviewRenderTask;
        tab.Editor.Document.Text = "# Current\n";
        window.RefreshLive();
        await window.PreviewRenderTask;
        await old;
        Assert.Contains(Tree(window.PreviewBody!).OfType<TextBlock>(), block => Text(block) == "Current");
        Assert.DoesNotContain(Tree(window.PreviewBody!).OfType<TextBlock>(), block => Text(block).Contains("Old", StringComparison.Ordinal));

        tab.Editor.Document.Text = string.Concat(Enumerable.Repeat("Closing *content*\n\n", 800));
        window.RefreshLive();
        Task closing = window.PreviewRenderTask;
        window.ClosePreview();
        await closing;
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.PreviewPanelOpen);
        Assert.Null(window.PreviewBody);
        Assert.Null(window.LivePreviewTab);
    }
}
