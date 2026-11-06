using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
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
        string page = source + "\n" + string.Join('\n', toc);
        var doc = MarkdownReaderModel.Build(page);
        Assert.Equal(["a", "a-1", "a-1-1"], doc.Targets.Keys);
        Assert.Equal(headings.Select(heading => heading.Line), doc.Targets.Values);
        using var host = new ReaderHost(page, 600, 500);
        Assert.Equal(3, host.Doc.Links.Count);
        for (int i = 0; i < host.Doc.Links.Count; i++) host.Reader.ActivateLink(i);
        Assert.Equal(headings.Select(heading => heading.Line), host.Jumps);
        Assert.Empty(host.Opened);
    }

    [AvaloniaFact]
    public async Task An_unchanged_paragraph_keeps_its_selection_after_refresh_and_an_edit_before_it()
    {
        var tab = window.OpenFile(temp.File("selection.md", "# Heading\n\nKeep this phrase\n\nLast\n"), null);
        window.SetViewMode(tab.Doc, ViewMode.Edit);
        tab = window.Active!;
        var page = tab.SidePreview!;
        await Ready(page);
        var reader = page.Reader!;
        int Phrase() => reader.Document.PlainText.IndexOf("Keep this phrase", StringComparison.Ordinal);
        reader.Select(Phrase() + 5, Phrase() + 9);

        page.Refresh(force: true);
        await Ready(page);
        Assert.Same(reader, page.Reader);
        Assert.Equal("this", reader.SelectedText());

        tab.Editor!.Document.Insert(0, "Introduction\n\n");
        page.Refresh();
        await Ready(page);
        Assert.Equal("this", reader.SelectedText());
        Assert.Equal((Phrase() + 5, Phrase() + 9), reader.SelectionRange);

        int phrase = tab.Editor.Document.Text.IndexOf("Keep this phrase", StringComparison.Ordinal);
        tab.Editor.Document.Replace(phrase, "Keep this phrase".Length, "A replaced paragraph");
        page.Refresh();
        await Ready(page);
        Assert.False(reader.HasSelection);
    }

    [AvaloniaFact]
    public async Task In_editing_the_copy_key_on_the_page_copies_its_selection_and_not_the_editors()
    {
        var tab = window.OpenFile(temp.File("copy.md", "# Heading\n\nKeep this phrase\n"), null);
        window.SetViewMode(tab.Doc, ViewMode.Edit);
        tab = window.Active!;
        var page = tab.SidePreview!;
        await Ready(page);
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.Select(0, 9);
        var reader = page.Reader!;
        reader.Focus();
        Assert.True(reader.IsFocused);
        int at = reader.Document.PlainText.IndexOf("this", StringComparison.Ordinal);
        reader.Select(at, at + 4);
        TestApp.Press(window, Avalonia.Input.Key.C, OperatingSystem.IsMacOS() ? Avalonia.Input.RawInputModifiers.Meta : Avalonia.Input.RawInputModifiers.Control);
        Assert.Equal("this", await window.Clipboard!.TryGetTextAsync());
        TestApp.Press(window, Avalonia.Input.Key.A, OperatingSystem.IsMacOS() ? Avalonia.Input.RawInputModifiers.Meta : Avalonia.Input.RawInputModifiers.Control);
        Assert.Equal((0, reader.Document.Length), reader.SelectionRange);
        Assert.Equal("# Heading", tab.Editor.SelectedText);
    }

    [AvaloniaFact]
    public async Task Replaced_or_closed_previews_cannot_be_restored_by_a_late_parse()
    {
        var tab = window.NewDocument();
        tab.Doc.ChooseLanguage(LanguageModes.ById("markdown"));
        tab.Editor!.Document.Text = string.Concat(Enumerable.Repeat("Old *content*\n\n", 800));
        window.ShowLivePreview();
        Task old = window.PreviewRenderTask;
        tab.Editor.Document.Text = "# Current\n";
        window.RefreshLive();
        await window.PreviewRenderTask;
        await old;
        var reader = Assert.IsType<MarkdownReader>(window.PreviewBody);
        Assert.Equal("Current", reader.Document.PlainText);

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
