using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.PluginHost;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.4.1 in the app: the outline of code and markup through the Code Explorer plugin, bound to a revision, refreshed after an edit, the same bytes from a click and from <c>@</c>.</summary>
[Collection("LocState")]
public class CodeExplorerAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public CodeExplorerAppTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
        _w.Width = 1000;
        _w.Height = 700;
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private DocTab Open(string name, string content)
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        return tab;
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static async Task<T> Pump<T>(Task<T> task)
    {
        await Until(() => task.IsCompleted, "a call");
        return await task;
    }

    private string[] Names(DocTab tab, out bool current) => [.. _app.Plugins.TabSymbols(tab, out current).Select(s => s.Name)];

    private async Task<string[]> IndexedAsync(DocTab tab)
    {
        Assert.True(await Pump(_app.Plugins.IndexSymbolsAsync(_w, tab, reveal: false)));
        var names = Names(tab, out bool current);
        Assert.True(current);
        return names;
    }

    private static ByteRange NameBytes(DocTab tab, string name)
    {
        var text = Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));
        int at = text.IndexOf(name, StringComparison.Ordinal);
        long start = Encoding.UTF8.GetByteCount(text[..at]);
        return new ByteRange(start, start + Encoding.UTF8.GetByteCount(name));
    }

    [AvaloniaFact]
    public async Task The_outline_of_html_php_css_csharp_and_markdown_belongs_to_the_revision_it_was_read_for()
    {
        var html = Open("a.html", "<body><h1>Welcome</h1><div id=\"app\"><h2>Čeština</h2></div></body>");
        Assert.Equal(["Welcome", "div#app", "Čeština"], await IndexedAsync(html));

        var php = Open("a.php", "<?php\nnamespace App;\nclass Box { public function area() {} }\nfunction helper() {}\n");
        Assert.Equal(["App", "Box", "area()", "helper()"], await IndexedAsync(php));

        var css = Open("a.css", "@media print { .a { color: red } }\nbody { margin: 0 }\n");
        Assert.Equal(["@media print", ".a", "body"], await IndexedAsync(css));

        var cs = Open("a.cs", "namespace N;\npublic class Box\n{\n    public int Size { get; set; }\n    public void Run(int a) { }\n}\n");
        Assert.Equal(["N", "Box", "Size", "Run(int a)"], await IndexedAsync(cs));

        var md = Open("a.md", "# Title\n## Part\ntext\n");
        Assert.Equal(["Title", "Part"], await IndexedAsync(md));

        // the set is the one of the revision the document has; an edit makes it outdated, and a plain text file has none
        Assert.Empty(_app.Plugins.TabSymbols(Open("a.txt", "# not markdown"), out _));
        Assert.False(await Pump(_app.Plugins.IndexSymbolsAsync(_w, Open("b.txt", "x"), reveal: false)));
    }

    [AvaloniaFact]
    public async Task After_an_edit_the_outline_is_outdated_for_a_moment_and_then_read_again()
    {
        var tab = Open("a.md", "# One\n## Two\n");
        Assert.Equal(["One", "Two"], await IndexedAsync(tab));

        tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "# Three\n");
        Names(tab, out bool current);
        Assert.False(current); // the old set stays, marked as outdated, until the new one arrives
        await Until(() => Names(tab, out bool now) is ["One", "Two", "Three"] && now, "the refreshed outline");

        // the old set is carried over the edit: a symbol keeps its place when text is inserted before it
        tab.Editor.Document.Insert(0, "intro\n");
        var two = _app.Plugins.TabSymbols(tab, out current).Single(s => s.Name == "Two");
        Assert.False(current);
        Assert.True(_app.Plugins.RevealTabSymbol(tab, two.Id));
        Assert.Equal("Two", tab.Editor.SelectedText);
        await Until(() => Names(tab, out bool now) is ["One", "Two", "Three"] && now, "the outline after the second edit");
    }

    [AvaloniaFact]
    public async Task An_answer_for_an_older_revision_is_never_shown_as_current()
    {
        var tab = Open("a.cs", "class A { }\n");
        var late = _app.Plugins.IndexSymbolsAsync(_w, tab, reveal: false);
        tab.Editor!.Document.Insert(0, "class Added { }\n"); // the document moves on while the plugin reads it
        await Pump(late);

        // whenever a set is current it is the one of the text the document has now
        await Until(() =>
        {
            var names = Names(tab, out bool current);
            if (current) Assert.Equal(["Added", "A"], names);
            return current;
        }, "an outline of the current text");
        Assert.Equal(["Added", "A"], Names(tab, out _));
    }

    [AvaloniaFact]
    public async Task A_click_in_the_outline_and_Go_to_Anything_select_the_same_bytes_of_the_name()
    {
        var tab = Open("a.cs", "namespace N;\npublic class Box\n{\n    public void Run(int a) { }\n    public void Stop() { }\n}\n");
        var run = NameBytes(tab, "Run");

        _w.ShowQuickOpen("@Run");
        await Until(() => _w.QuickResults.Any(r => r.Title == "Run(int a)"), "the symbols");
        Assert.Equal(["Run(int a)"], _w.QuickResults.Select(r => r.Title));
        _w.RunPaletteSelection();
        Assert.Equal(run, tab.Editor!.ByteSelections()!.Items[0].ToRange());
        Assert.Equal("Run", tab.Editor.SelectedText);

        tab.Editor.TextArea.ClearSelection();
        tab.Editor.CaretOffset = 0;
        string id = _app.Plugins.TabSymbols(tab, out bool current).Single(s => s.Name == "Run(int a)").Id;
        Assert.True(current);
        Assert.True(_app.Plugins.RevealSymbol(PluginServices.CodeExplorerPluginId, PluginServices.SymbolsPanelPrefix + tab.Doc.Doc.Id.ToString("N"), id));
        Assert.Equal(run, tab.Editor.ByteSelections()!.Items[0].ToRange());

        // Go to Anything did not open the outline; the command does
        Assert.False(_w.PluginPanelOpen);
        var command = _app.Workbench.Commands.ExecuteAsync("nks.tools.showOutline", new ShellContext(_w, forMenu: false)).AsTask();
        await Until(() => command.IsCompleted && !_app.Plugins.IsBusy, "the command");
        Assert.Equal(CommandOutcome.Handled, await command);
        Assert.True(_w.PluginPanelOpen);
        Assert.Equal(Loc.Get(K.PluginSymbolsTitle), _w.PluginPanelTitle);
    }

    [AvaloniaFact]
    public async Task The_outline_marks_the_item_the_caret_is_in_moves_the_mark_with_the_caret_and_keeps_it_over_an_edit()
    {
        var tab = Open("a.cs", "namespace N;\npublic class Box\n{\n    public void Run(int a) { }\n    public void Stop() { }\n}\n");
        Assert.Equal(["N", "Box", "Run(int a)", "Stop()"], await IndexedAsync(tab));
        var panel = PluginServices.SymbolsPanelPrefix + tab.Doc.Doc.Id.ToString("N");
        string? Marked() => _app.Plugins.Ui.Panels.Single(p => p.Model.Id == panel).Model.Current;
        string IdOf(string name) => _app.Plugins.TabSymbols(tab, out _).Single(s => s.Name == name).Id;
        var text = tab.Editor!.Document;

        // the caret rests in Stop(): once it has rested, the outline marks that item
        tab.Editor.CaretOffset = text.Text.IndexOf("Stop", StringComparison.Ordinal) + 2;
        await Until(() => Marked() == IdOf("Stop()"), "the mark on Stop");
        tab.Editor.CaretOffset = text.Text.IndexOf("Run", StringComparison.Ordinal);
        await Until(() => Marked() == IdOf("Run(int a)"), "the mark on Run");
        tab.Editor.CaretOffset = 0; // before the first symbol there is no current item
        await Until(() => Marked() is null, "the mark going");

        // an edit makes the set outdated and then new; the mark stays with the symbol the caret is in
        tab.Editor.CaretOffset = text.Text.IndexOf("Stop", StringComparison.Ordinal) + 2;
        await Until(() => Marked() == IdOf("Stop()"), "the mark on Stop again");
        text.Insert(0, "// first\n");
        await Until(() => Names(tab, out bool now) is ["N", "Box", "Run(int a)", "Stop()"] && now && Marked() == IdOf("Stop()"), "the mark after the new outline");
        Assert.Equal("Stop", text.GetText(tab.Editor.CaretOffset - 2, 4));
    }

    [AvaloniaFact]
    public async Task The_outline_survives_a_restart_of_the_plugin_process()
    {
        var tab = Open("a.php", "<?php\nclass Box { public function area() {} }\n");
        Assert.Equal(["Box", "area()"], await IndexedAsync(tab));
        var before = Process.GetProcessesByName("NksHub.Textida.Plugins.CodeExplorer").Select(p => p.Id).ToHashSet();
        Assert.NotEmpty(before);

        foreach (var pid in before)
        {
            try { Process.GetProcessById(pid).Kill(); }
            catch (ArgumentException) { } // already gone
        }
        await Until(() => Process.GetProcessesByName("NksHub.Textida.Plugins.CodeExplorer").Any(p => !before.Contains(p.Id)), "the new process");
        await Until(() => Names(tab, out bool current) is ["Box", "area()"] && current, "the outline after the restart");

        var area = _app.Plugins.TabSymbols(tab, out _).Single(s => s.Name == "area()");
        Assert.True(_app.Plugins.RevealTabSymbol(tab, area.Id));
        Assert.Equal("area", tab.Editor!.SelectedText);
    }
}
