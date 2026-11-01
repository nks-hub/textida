using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.8.3: the prototype of plugin code in the editor, run by the real Jint engine, bounded and off by default.</summary>
public class InEditorScriptTests
{
    [Fact]
    public void The_transform_function_gets_the_text_and_returns_the_new_one_and_log_reaches_the_caller()
    {
        var lines = new List<string>();
        string result = InEditorScript.Transform("function transform(t) { log('got ' + t.length); return t.toUpperCase() + '!'; }", "abc", lines.Add);
        Assert.Equal("ABC!", result);
        Assert.Equal(["got 3"], lines);
        Assert.Equal("ěšč", InEditorScript.Transform("function transform(t) { return t.split('').reverse().join(''); }", "čšě", _ => { }));
    }

    [Theory]
    [InlineData("function transform(t) { while (true) {} }", "ran")]
    [InlineData("function transform(t) { var a = []; while (true) { a.push(new Array(100000).join('x')); } }", "")]
    [InlineData("function transform(t) { return transform(t); }", "recursed")]
    [InlineData("var x = 1;", "defines no function")]
    [InlineData("function transform(t) { return 5; }", "must return a string")]
    [InlineData("function transform(t) { throw new Error('boom'); }", "boom")]
    [InlineData("function transform( {", "")]
    public void A_script_that_does_not_behave_is_stopped_with_a_reason(string script, string expected)
    {
        var error = Assert.Throws<ScriptException>(() => InEditorScript.Transform(script, "x", _ => { }, TimeSpan.FromMilliseconds(700)));
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void A_script_has_no_way_to_the_system()
    {
        foreach (string probe in new[] { "System.IO.File.ReadAllText('x')", "require('fs')", "process.env.PATH", "importNative('x')", "fetch('http://example.test')", "new XMLHttpRequest()" })
        {
            var error = Assert.Throws<ScriptException>(() => InEditorScript.Transform($"function transform(t) {{ return String({probe}); }}", "x", _ => { }));
            Assert.Contains("not defined", error.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}

[Collection("LocState")]
public class ScriptingAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly Shell.MainWindow _w;

    public ScriptingAppTests()
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

    [AvaloniaFact]
    public async Task The_command_is_off_by_default_and_runs_nothing()
    {
        var tab = _w.OpenFile(_temp.File("a.txt", "abc\n"), null);
        string script = _temp.File("up.js", "function transform(t) { return t.toUpperCase(); }");
        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(script);
        await _w.RunEditorScriptAsync();
        Assert.Equal(Loc.Get(K.ScriptingOff), _w.StatusMessageText);
        Assert.Equal("abc\n", tab.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public async Task With_the_setting_on_the_result_is_a_change_in_the_review_that_the_user_applies_or_not()
    {
        _app.Workbench.Set("scripting.enabled", true);
        var tab = _w.OpenFile(_temp.File("a.txt", "hello world\n"), null);
        tab.Editor!.Select(0, 5);
        string script = _temp.File("up.js", "function transform(t) { log('in ' + t); return t.toUpperCase(); }");
        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(script);

        var run = _w.RunEditorScriptAsync();
        for (int i = 0; i < 300 && _w.ResultsView?.Rows.OfType<SearchResultsView.HunkRow>().Any() != true; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(15); }
        Assert.Equal("hello world\n", tab.Editor.Document.Text); // nothing is written while the user looks
        _w.ApplyPlan();
        await run;
        Assert.Equal("HELLO world\n", tab.Editor.Document.Text);
        Assert.Contains(_app.Output.Read(OutputChannel.Tasks), l => l.Source == "script" && l.Text == "in hello");
    }

    [AvaloniaFact]
    public async Task A_script_that_runs_too_long_is_ended_and_the_text_is_untouched()
    {
        _app.Workbench.Set("scripting.enabled", true);
        _w.ScriptTimeout = TimeSpan.FromMilliseconds(500);
        var tab = _w.OpenFile(_temp.File("a.txt", "keep\n"), null);
        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(_temp.File("loop.js", "function transform(t) { while (true) {} }"));
        await _w.RunEditorScriptAsync();
        Assert.Equal("keep\n", tab.Editor!.Document.Text);
        Assert.Contains(Loc.Get(K.ScriptingFailed, ("reason", "")).Split(':')[0], _w.StatusMessageText);
    }
}
