using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class DocumentDataModeTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly App app;
    private readonly MainWindow window;

    public DocumentDataModeTests()
    {
        app = TestApp.Prepare(temporary, new ScriptedDialogs());
        window = app.CreateWindow(null);
        window.Width = 1200; window.Height = 800;
        window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temporary.Dispose();
    }

    private string Write(string name, string text) => temporary.Bytes(name, System.Text.Encoding.UTF8.GetBytes(text));

    private async Task Drain(StructureView view)
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Task current = view.Working;
            if (current.IsCompleted)
            {
                await current;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                if (ReferenceEquals(current, view.Working)) return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("The document data view did not finish its pending work.");
    }

    [AvaloniaTheory]
    [InlineData("json", "{\"name\":\"Demo\",\"items\":[{\"id\":1},{\"id\":2}]}")]
    [InlineData("jsonc", "/* demo */ {\"name\":\"Demo\",\"items\":[1,2]}")]
    [InlineData("xml", "<root><name>Demo</name><items><item id=\"1\"/><item id=\"2\"/></items></root>")]
    [InlineData("yaml", "name: Demo\nitems:\n  - id: 1\n  - id: 2\n")]
    [InlineData("neon", "name: Demo\nitems:\n  - id: 1\n  - id: 2\n")]
    public async Task Data_modes_share_the_document_and_analysis_without_losing_unsaved_text(string extension, string content)
    {
        var tab = window.OpenFile(Write("sample." + extension, content), null);
        tab.Surface!.Document.Insert(tab.Surface.Document.TextLength, " ");
        var document = tab.Doc.Doc;
        long revision = document.Revision;
        string expected = tab.Surface.Document.Text;
        Assert.True(document.IsModified);
        Assert.Contains(ViewMode.Tree, MainWindow.ModesOf(tab.Doc));
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Tree));
        Assert.Same(tab, window.Active);
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Drain(view);
        Task parsing = view.Parsing;
        for (int depth = 0; depth < 3; depth++)
        {
            foreach (var node in view.Tree.VisibleNodes.ToArray())
                if (node.HasChildren) node.IsExpanded = true;
            await Drain(view);
        }
        Assert.Contains(view.Tree.VisibleNodes, node => node.Value.Contains("Demo", StringComparison.Ordinal));
        Assert.True(tab.StructureShown);
        Assert.True(view.Bounds.Width > window.ClientSize.Width * 0.65);
        Assert.False(tab.View.IsVisible);
        foreach (var mode in new[] { ViewMode.Graph, ViewMode.Table, ViewMode.StructureSplit, ViewMode.Text, ViewMode.Tree })
        {
            Assert.True(window.SetViewMode(tab.Doc, mode));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Same(view, tab.Structure);
            Assert.Same(parsing, view.Parsing);
            Assert.Same(document, tab.Doc.Doc);
            Assert.Equal(revision, document.Revision);
            Assert.Equal(expected, tab.Surface.Document.Text);
            Assert.True(document.IsModified);
            Assert.Equal(mode == ViewMode.Text || mode == ViewMode.StructureSplit, tab.View.IsVisible);
        }
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Text));
        document.Undo();
        Assert.Equal(content, tab.Surface.Document.Text);
    }

    [AvaloniaTheory]
    [InlineData("csv", "name,value\nDemo,1\n")]
    [InlineData("tsv", "name\tvalue\nDemo\t1\n")]
    public void Delimited_data_uses_the_existing_editable_table_and_source_switcher(string extension, string content)
    {
        var tab = window.OpenFile(Write("sample." + extension, content), null);
        var document = tab.Doc.Doc;
        Assert.Equal(new[] { ViewMode.Text, ViewMode.Table }, MainWindow.ModesOf(tab.Doc));
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Table));
        Assert.True(tab.TableShown);
        Assert.NotNull(tab.Table);
        Assert.Same(document, tab.Doc.Doc);
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Text));
        Assert.False(tab.TableShown);
        Assert.True(tab.View.IsVisible);
        Assert.Equal(content, tab.Surface!.Document.Text);
    }

    [AvaloniaFact]
    public async Task Split_routes_text_commands_to_the_focused_source_and_keeps_undo()
    {
        const string text = "{\"name\":\"Demo\"}";
        var tab = window.OpenFile(Write("sample.json", text), null);
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.StructureSplit));
        await Drain(tab.Structure!);
        Assert.True(tab.Surface!.TextArea.Focus());
        Dispatcher.UIThread.RunJobs();
        var context = new ShellContext(window, forMenu: true);
        Assert.True(tab.ModeBar!.Buttons[0].Focus());
        Assert.False(tab.View.IsKeyboardFocusWithin);
        Assert.Equal("false", context.Get("readOnly"));
        await app.Workbench.Commands.ExecuteAsync("nks.edit.selectAll", context);
        Assert.True(tab.Surface.HasSelection);
        await app.Workbench.Commands.ExecuteAsync("nks.edit.cut", context);
        Assert.Equal("", tab.Surface.Document.Text);
        tab.Doc.Doc.Undo();
        Assert.Equal(text, tab.Surface.Document.Text);
        Assert.True(tab.Structure!.FocusContent());
        Assert.Equal("true", context.Get("readOnly"));
    }

    [AvaloniaFact]
    public async Task Manual_structured_language_wins_over_a_delimited_file_extension()
    {
        var tab = window.OpenFile(Write("sample.csv", "{\"name\":\"Demo\"}"), null);
        tab.Doc.ChooseLanguage(LanguageModes.ById("json"));
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Tree));
        await Drain(tab.Structure!);
        Assert.True(tab.StructureShown);
        Assert.False(tab.TableShown);
        tab.Doc.ChooseLanguage(LanguageModes.ById("text"));
        Assert.Equal(ViewMode.Text, tab.Doc.Mode);
        Assert.True(tab.View.IsVisible);
        Assert.False(tab.StructureShown);
    }

    [AvaloniaFact]
    public void Source_button_leaves_a_legacy_pretty_overlay_even_when_source_mode_is_current()
    {
        const string text = "{\"name\":\"Demo\",\"last\":42}";
        var tab = window.OpenFile(Write("sample.json", text), null);
        window.SetJsonPretty(tab, true);
        long offset = text.IndexOf("42", StringComparison.Ordinal);
        tab.Json!.RevealOffset(offset, offset);
        Assert.Equal(ViewMode.Text, tab.Doc.Mode);
        Assert.True(tab.JsonShown);
        tab.ModeBar!.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(tab.JsonShown);
        Assert.True(tab.View.IsVisible);
        Assert.Equal((int)offset, tab.Surface!.CaretOffset);
    }
}
