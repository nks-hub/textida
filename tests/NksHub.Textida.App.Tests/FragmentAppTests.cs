using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.9.1 through the running window: the fragment tab, the write-back with escaping, the conflict question and the detach on closing the file.</summary>
[Collection("LocState")]
public class FragmentAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public FragmentAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        Loc.SetLanguage("en");
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private (DocTab Parent, DocTab Fragment) OpenFragment(string file, string text, string marker, string language)
    {
        var parent = _w.OpenFile(_temp.File(file, text), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        parent.Surface!.SetSelections([Sel.At(text.IndexOf(marker, StringComparison.Ordinal) + 1)]);
        Assert.Equal(CommandOutcome.Handled, _w.EditStringAs(language));
        Dispatcher.UIThread.RunJobs();
        return (parent, _w.Active!);
    }

    [AvaloniaFact]
    public void Json_in_a_php_string_opens_in_a_tab_and_returns_escaped()
    {
        var (parent, fragment) = OpenFragment("a.php", "<?php\n$j = '{\"a\":\"it\\'s\"}';\n", "\"a\"", "json");
        Assert.NotSame(parent, fragment);
        Assert.Equal("json", fragment.Doc.Language.Id);
        Assert.Equal("{\"a\":\"it's\"}", fragment.Surface!.Document.Text);
        Assert.False(fragment.Doc.Doc.IsModified);
        Assert.Contains("JSON from a.php, line 2", fragment.Doc.Title);

        fragment.Surface.Document.Text = "{\"a\":\"it's\",\"b\":\"x\\\\y\"}";
        _w.SelectTab(fragment);
        Assert.Equal(CommandOutcome.Handled, _w.WriteBackFragment());
        Assert.Equal("<?php\n$j = '{\"a\":\"it\\'s\",\"b\":\"x\\\\\\\\y\"}';\n", parent.Surface!.Document.Text);
        Assert.False(fragment.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public async Task A_change_inside_the_string_asks_before_overwriting()
    {
        var (parent, fragment) = OpenFragment("a.php", "<?php\n$j = \"abc\";\n", "abc", "sql");
        parent.Surface!.Document.Insert(parent.Surface.Document.Text.IndexOf("abc", StringComparison.Ordinal), "Z");
        fragment.Surface!.Document.Text = "select 1";

        _dialogs.Answers.Enqueue(1);
        await _w.WriteBackFragmentAsync(fragment.Doc, fragment.Doc.Fragment!, "select 1");
        Assert.Contains("Zabc", parent.Surface.Document.Text);
        Assert.Single(_dialogs.Asked);

        _dialogs.Answers.Enqueue(0);
        await _w.WriteBackFragmentAsync(fragment.Doc, fragment.Doc.Fragment!, "select 1");
        Assert.Equal("<?php\n$j = \"select 1\";\n", parent.Surface.Document.Text);
    }

    [AvaloniaFact]
    public async Task Closing_the_file_detaches_the_fragment_and_it_writes_nothing()
    {
        var (parent, fragment) = OpenFragment("a.js", "var a = 'x';\n", "x", "html");
        await _w.CloseTabAsync(parent);
        Assert.Equal(CommandOutcome.Handled, _w.WriteBackFragment());
        Dispatcher.UIThread.RunJobs();
        Assert.True(fragment.Doc.Fragment!.IsDetached);
        Assert.Contains("detached", fragment.Doc.Title);
    }

    [AvaloniaFact]
    public void A_template_with_an_expression_and_a_caret_outside_a_string_are_refused()
    {
        var parent = _w.OpenFile(_temp.File("t.js", "var a = `hi ${n}`;\nvar b = 1;\n"), null);
        _w.UpdateLayout();
        parent.Surface!.SetSelections([Sel.At(11)]);
        _w.EditStringAs("json");
        Assert.Same(parent, _w.Active);
        parent.Surface.SetSelections([Sel.At(parent.Surface.Document.Text.IndexOf("= 1", StringComparison.Ordinal))]);
        _w.EditStringAs("json");
        Assert.Same(parent, _w.Active);
    }
}
