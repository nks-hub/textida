using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.6.2 in the app: the values of a .env file are hidden in the view, the minimap, the previews and the clipboard, and the bytes of the file never change.</summary>
[Collection("LocState")]
public class SecretsViewTests : IDisposable
{
    private const string Env = "# keys\nAPI_KEY=hunter2\nOTHER=\"a b\"\nPLAIN=x\n";
    private const string Mask = SecretValues.Placeholder;

    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public SecretsViewTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
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

    private DocTab Open(string name, string content = Env)
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    /// <summary>The placeholders the view builds (the only plain formatted-text elements the editor has), as (document line, length of the text they stand for).</summary>
    private static List<(int Line, int Length)> Placeholders(DocTab tab)
    {
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        return [.. view.VisualLines.SelectMany(l => l.Elements.Where(e => e.GetType() == typeof(FormattedTextElement)).Select(e => (l.FirstDocumentLine.LineNumber, e.DocumentLength)))];
    }

    private static string Bytes(DocTab tab) => Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength)));

    private Task<CommandOutcome> RunAsync(string command) => _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();

    private static async Task<string?> ClipboardAfterAsync(MainWindow w, string before)
    {
        var sw = Stopwatch.StartNew();
        string? now;
        while ((now = await w.Clipboard!.TryGetTextAsync()) == before && sw.ElapsedMilliseconds < 3000)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        return now;
    }

    [AvaloniaFact]
    public void Values_are_drawn_as_the_placeholder_and_the_text_and_bytes_stay_as_they_are()
    {
        var tab = Open(".env");
        Assert.Equal([(2, 7), (3, 5), (4, 1)], Placeholders(tab));
        Assert.Equal(Env, tab.Editor!.Document.Text);
        Assert.Equal(Env, Bytes(tab));
        Assert.False(tab.Doc.Doc.IsModified);
    }

    [AvaloniaFact]
    public void Only_variable_files_are_hidden_and_the_name_decides_again_after_save_as()
    {
        Assert.Empty(Placeholders(Open("notes.txt")));
        Assert.Equal(3, Placeholders(Open("prod.env")).Count);
        Assert.Equal(3, Placeholders(Open(".env.local")).Count);
    }

    [AvaloniaFact]
    public async Task The_switch_shows_and_hides_the_values_of_the_tab_and_resting_on_one_shows_just_that_one()
    {
        var tab = Open(".env");
        var surface = tab.Editor!;
        Assert.False(surface.SecretsRevealed);

        surface.ShowSecretAt(Env.IndexOf("hunter2", StringComparison.Ordinal) + 2);
        Assert.Equal([(3, 5), (4, 1)], Placeholders(tab));
        surface.ShowSecretAt(Env.IndexOf("x\n", StringComparison.Ordinal));
        Assert.Equal([(2, 7), (3, 5)], Placeholders(tab));
        surface.ShowSecretAt(1); // the pointer is on a key: nothing is shown
        Assert.Equal(3, Placeholders(tab).Count);

        Assert.Equal(CommandOutcome.Handled, await RunAsync("nks.view.toggleSecrets"));
        Assert.True(surface.SecretsRevealed);
        Assert.Empty(Placeholders(tab));
        Assert.Equal(CommandOutcome.Handled, await RunAsync("nks.view.toggleSecrets"));
        Assert.Equal(3, Placeholders(tab).Count);
        Assert.Equal(Env, Bytes(tab));
    }

    [AvaloniaFact]
    public void The_minimap_is_told_the_hidden_lines_and_never_the_values()
    {
        var tab = Open(".env");
        var host = (IMinimapHost)tab.Editor!;
        Assert.Equal(["# keys", "API_KEY=" + Mask, "OTHER=" + Mask, "PLAIN=" + Mask], Enumerable.Range(0, 4).Select(i => host.LineText(i)));
        int raised = 0;
        host.ContentChanged += () => raised++;
        tab.Editor!.ToggleSecrets();
        Assert.Equal(1, raised);
        Assert.Equal("API_KEY=hunter2", host.LineText(1));
    }

    [AvaloniaFact]
    public async Task Copy_puts_the_placeholder_on_the_clipboard_and_cut_deletes_no_value()
    {
        var tab = Open(".env");
        var surface = tab.Editor!;
        surface.TextArea.Focus();
        await _w.Clipboard!.SetTextAsync("before");

        surface.Select(0, Env.Length);
        Assert.Equal(CommandOutcome.Handled, await RunAsync("nks.edit.copy"));
        Assert.Equal($"# keys\nAPI_KEY={Mask}\nOTHER={Mask}\nPLAIN={Mask}\n", await ClipboardAfterAsync(_w, "before"));

        await _w.Clipboard.SetTextAsync("before");
        Assert.Equal(CommandOutcome.Handled, await RunAsync("nks.edit.cut"));
        Assert.Equal($"# keys\nAPI_KEY={Mask}\nOTHER={Mask}\nPLAIN={Mask}\n", await ClipboardAfterAsync(_w, "before"));
        Assert.Equal(Env, surface.Document.Text);

        // the keyboard reaches the same filter
        await _w.Clipboard.SetTextAsync("before");
        TestApp.Press(_w, Key.C, TestApp.Mod);
        Assert.Equal($"# keys\nAPI_KEY={Mask}\nOTHER={Mask}\nPLAIN={Mask}\n", await ClipboardAfterAsync(_w, "before"));

        // a selection that holds no value is copied as it is
        await _w.Clipboard.SetTextAsync("before");
        surface.Select(0, 6);
        await RunAsync("nks.edit.copy");
        Assert.Equal("# keys", await ClipboardAfterAsync(_w, "before"));
        Assert.DoesNotContain(_app.ClipboardHistory.Items, item => item.Contains("hunter2", StringComparison.Ordinal));

        // once the user shows the values, they are copied too
        surface.ToggleSecrets();
        await _w.Clipboard.SetTextAsync("before");
        surface.Select(Env.IndexOf("hunter2", StringComparison.Ordinal), 7);
        await RunAsync("nks.edit.copy");
        Assert.Equal("hunter2", await ClipboardAfterAsync(_w, "before"));
    }

    [AvaloniaFact]
    public void The_previews_of_go_to_anything_show_the_placeholder()
    {
        var env = _temp.File(".env", Env);
        var plain = _temp.File("notes.txt", Env);
        Assert.Equal($"# keys\nAPI_KEY={Mask}\nOTHER={Mask}\nPLAIN={Mask}", MainWindow.ReadPreview(env));
        Assert.Contains("hunter2", MainWindow.ReadPreview(plain));

        Open(".env");
        _w.ShowQuickOpen();
        Assert.Equal($"# keys\nAPI_KEY={Mask}\nOTHER={Mask}\nPLAIN={Mask}", _w.QuickResults.First(r => r.Title == ".env").Preview!());
    }

    [AvaloniaFact]
    public async Task A_built_in_command_reads_the_values_only_after_the_user_allows_it()
    {
        var tab = Open(".env");
        var id = tab.Doc.Doc.Id;
        var grant = (Func<bool>)(() => _app.Plugins.Permissions.IsGranted(PluginServices.TextToolsPluginId, DocumentPermissions.SecretsReveal, GrantScope.OfDocument(id)));

        _dialogs.Answers.Enqueue(1); // Cancel
        await RunAsync("nks.tools.statistics");
        await Until(() => _dialogs.Asked.Count == 1);
        await Task.Delay(100);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Loc.Get(K.PluginSecretsTitle), _dialogs.Asked[0].Title);
        Assert.Contains(".env", _dialogs.Asked[0].Message);
        Assert.False(_app.Plugins.IsBusy);
        Assert.False(grant());
        Assert.DoesNotContain(_w.Notices, n => n.Message.Contains("Bajty"));

        _dialogs.Answers.Enqueue(0); // OK
        await RunAsync("nks.tools.statistics");
        await Until(() => _w.Notices.Any(n => n.Message.Contains("Bajty: " + Env.Length)));
        await Until(() => !_app.Plugins.IsBusy);
        Assert.False(grant()); // the permission lasts for one run
        Assert.Equal(Env, Bytes(tab));
    }

    private static async Task Until(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
