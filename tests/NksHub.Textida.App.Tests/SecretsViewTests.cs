using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Accessibility;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;
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

    [AvaloniaTheory]
    [InlineData("en", "[hidden value]")]
    [InlineData("cs", "[skrytá hodnota]")]
    [InlineData("sk", "[skrytá hodnota]")]
    [InlineData("de", "[verborgener Wert]")]
    public void The_screen_reader_text_hides_the_values_names_them_and_keeps_the_caret_consistent(string language, string marker)
    {
        Loc.SetLanguage(language);
        var tab = Open(".env");
        var editor = tab.Editor!;
        using var surface = new EditorTextSurface(editor);
        var changes = new List<TextChange>();
        surface.TextChanged += changes.Add;

        string shown = surface.GetText(0, surface.Length);
        Assert.Equal($"# keys\nAPI_KEY={marker}\nOTHER={marker}\nPLAIN={marker}\n", shown);
        Assert.DoesNotContain("hunter2", shown);
        Assert.DoesNotContain("a b", shown);
        Assert.Equal(5, surface.LineCount);
        Assert.Equal(Env, editor.Document.Text);

        // a caret in the document lands at the same place of the shown text
        int docKey = Env.IndexOf("OTHER", StringComparison.Ordinal);
        editor.CaretOffset = docKey + 3;
        Assert.Equal(shown.IndexOf("OTHER", StringComparison.Ordinal) + 3, surface.CaretOffset);
        editor.CaretOffset = Env.Length;
        Assert.Equal(shown.Length, surface.CaretOffset);

        // a selection over a whole value covers the whole marker
        int docValue = Env.IndexOf("hunter2", StringComparison.Ordinal);
        editor.Select(docValue - 8, 15);
        int shownKey = shown.IndexOf("API_KEY", StringComparison.Ordinal);
        Assert.Equal(new TextSpan(shownKey, 8 + marker.Length), surface.Selection);
        Assert.Equal("API_KEY=" + marker, surface.GetText(surface.Selection.Start, surface.Selection.Length));

        // a selection made through the automation maps back to the document
        surface.SetSelection(shownKey, 8 + marker.Length);
        Assert.Equal((docValue - 8, 15), (editor.SelectionStart, editor.SelectionLength));

        // the pointer resting on a value shows only that one, and the screen reader is told
        changes.Clear();
        editor.ShowSecretAt(docValue);
        Assert.Equal($"# keys\nAPI_KEY=hunter2\nOTHER={marker}\nPLAIN={marker}\n", surface.GetText(0, surface.Length));
        Assert.NotEmpty(changes);

        // the switch shows everything: the real text, and the caret still maps
        editor.ShowSecretAt(null);
        changes.Clear();
        editor.ToggleSecrets();
        Assert.Equal(Env, surface.GetText(0, surface.Length));
        Assert.NotEmpty(changes);
        editor.CaretOffset = docKey;
        Assert.Equal(docKey, surface.CaretOffset);

        editor.ToggleSecrets();
        Assert.DoesNotContain("hunter2", surface.GetText(0, surface.Length));
    }

    [AvaloniaFact]
    public void The_screen_reader_text_of_other_files_is_the_document_text()
    {
        var tab = Open("notes.txt");
        using var surface = new EditorTextSurface(tab.Editor!);
        Assert.Equal(Env, surface.GetText(0, surface.Length));
    }

    // ---- paged mode ----

    /// <summary>An .env of over a MiB, so that the tab (the threshold is 1 MiB here) shows it in paged mode.</summary>
    private static string BigEnv(string head = Env) => head + string.Concat(Enumerable.Range(0, 40_000).Select(i => $"FILLER_{i}=value-{i}-padding-padding\n"));

    private DocTab OpenPaged(string text, string name = ".env")
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(_temp.Bytes(name, Encoding.UTF8.GetBytes(text)), null);
        Assert.True(tab.Doc.IsLarge);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Paged!.Index.Complete);
        _w.UpdateLayout();
        tab.Paged.Focus();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    [AvaloniaFact]
    public async Task The_paged_view_draws_copies_and_reads_the_placeholder_and_the_switch_shows_the_values()
    {
        var tab = OpenPaged(BigEnv());
        var paged = tab.Paged!;
        var host = (IMinimapHost)paged;
        string hidden = $"# keys\nAPI_KEY={Mask}\nOTHER={Mask}\nPLAIN={Mask}\n";
        Assert.True(paged.SecretsHidden);
        Assert.Equal(["# keys", "API_KEY=" + Mask, "OTHER=" + Mask, "PLAIN=" + Mask, "FILLER_0=" + Mask], Enumerable.Range(0, 5).Select(i => paged.DisplayOf(i)));
        Assert.Equal(["API_KEY=" + Mask, "FILLER_0=" + Mask], new[] { host.LineText(1), host.LineText(4) });

        // the screen reader gets a bullet for each hidden character and never a value
        using (var surface = new PagedTextView.TextSurface(paged))
        {
            string read = surface.GetText(0, surface.Length);
            Assert.StartsWith("# keys\nAPI_KEY=•••••••\nOTHER=•••••\nPLAIN=•\n", read);
            Assert.DoesNotContain("hunter2", read);
        }

        // copy puts the placeholder on the clipboard and into its history; a cut that touches a value deletes nothing
        paged.SelectBytes(0, Env.Length);
        await _w.Clipboard!.SetTextAsync("before");
        await paged.CopyAsync(cut: false);
        Assert.Equal(hidden, await ClipboardAfterAsync(_w, "before"));
        long revision = tab.Doc.Doc.Revision;
        await _w.Clipboard.SetTextAsync("before");
        await paged.CopyAsync(cut: true);
        Assert.Equal(hidden, await ClipboardAfterAsync(_w, "before"));
        Assert.Equal(revision, tab.Doc.Doc.Revision);
        Assert.DoesNotContain(_app.ClipboardHistory.Items, item => item.Contains("hunter2", StringComparison.Ordinal));
        paged.SelectBytes(0, 6); // a selection that holds no value is copied as it is
        await _w.Clipboard.SetTextAsync("before");
        await paged.CopyAsync(cut: false);
        Assert.Equal("# keys", await ClipboardAfterAsync(_w, "before"));

        // the same switch as in the editor shows the values, in the view, the minimap and the clipboard
        Assert.Equal(CommandOutcome.Handled, await RunAsync("nks.view.toggleSecrets"));
        Assert.True(tab.SecretsRevealed);
        Assert.Equal("API_KEY=hunter2", paged.DisplayOf(1));
        Assert.Equal("API_KEY=hunter2", host.LineText(1));
        paged.SelectBytes(0, Env.Length);
        await _w.Clipboard.SetTextAsync("before");
        await paged.CopyAsync(cut: false);
        Assert.Equal(Env, await ClipboardAfterAsync(_w, "before"));
        Assert.Equal(CommandOutcome.Handled, await RunAsync("nks.view.toggleSecrets"));
        Assert.Equal("API_KEY=" + Mask, paged.DisplayOf(1));
    }

    [AvaloniaFact]
    public void A_variable_typed_into_the_paged_view_is_hidden_at_once_and_an_opening_quote_hides_the_lines_after_it()
    {
        var tab = OpenPaged(BigEnv());
        var paged = tab.Paged!;
        Assert.Equal("API_KEY=" + Mask, paged.DisplayOf(1)); // drawn once before the edits
        paged.GoToOffset(0);
        Assert.True(paged.InsertText("NEW=zzz\n", typing: false));
        Assert.Equal(["NEW=" + Mask, "# keys", "API_KEY=" + Mask], Enumerable.Range(0, 3).Select(i => paged.DisplayOf(i)));

        paged.GoToOffset(0);
        Assert.True(paged.InsertText("X=\"", typing: false));
        Assert.Equal(["X=" + Mask, Mask, Mask], Enumerable.Range(0, 3).Select(i => paged.DisplayOf(i)));
        Assert.StartsWith("X=\"NEW=zzz\n# keys\n", Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, 40))));
    }

    [AvaloniaFact]
    public void A_long_line_with_a_value_withholds_what_lies_beyond_its_first_part_and_a_long_comment_does_not()
    {
        var tab = OpenPaged(BigEnv("# " + new string('c', 20_000) + "\nBIG=" + new string('x', 20_000) + "\nPLAIN=x\n"));
        var paged = tab.Paged!;
        Assert.Equal("BIG=" + Mask, paged.DisplayOf(1));
        Assert.True(paged.HidesTailOf(1));
        Assert.False(paged.HidesTailOf(0));
        Assert.False(paged.HidesTailOf(2));
        Assert.StartsWith("# ccc", paged.DisplayOf(0));

        Assert.True(tab.ToggleSecrets());
        Assert.False(paged.HidesTailOf(1));
        Assert.StartsWith("BIG=xxx", paged.DisplayOf(1));
    }

    // ---- hex view ----

    private DocTab OpenHex(string name, string content = Env)
    {
        Open(name, content);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(_w.Active!.Hex);
        _w.Active.Hex!.Focus();
        Dispatcher.UIThread.RunJobs();
        return _w.Active;
    }

    [AvaloniaFact]
    public async Task The_hex_view_describes_the_bytes_of_a_value_as_hidden_and_copies_and_exports_stars_until_the_values_are_shown()
    {
        var tab = OpenHex(".env");
        var hex = tab.Hex!;
        int value = Env.IndexOf("hunter2", StringComparison.Ordinal);
        Assert.True(hex.SecretsHidden);
        Assert.True(hex.Describe(value).Hidden);
        Assert.True(hex.Describe(value + 6).Hidden);
        Assert.False(hex.Describe(value - 1).Hidden);
        Assert.Equal((byte)'=', hex.Describe(value - 1).Value);
        Assert.False(hex.Describe(value + 7).Hidden); // the line break after the value

        // the status line, the screen reader and the data inspector name the value as hidden and show none of its bytes
        hex.MoveTo(value);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        string marker = Loc.Get(K.SecretHiddenValue);
        Assert.Contains(marker, _w.StatusLeftText);
        Assert.Equal(Loc.Get(K.HexA11yHidden, ("offset", hex.OffsetText(value))), HexAutomationPeer.Describe(hex));
        hex.Inspector.FocusList();
        hex.Inspector.Refresh();
        Assert.All(hex.Inspector.RowTexts, text => Assert.EndsWith(marker, text));
        hex.MoveTo(Env.Length - 1); // the window of the inspector holds only the last line break
        hex.Inspector.Refresh();
        Assert.DoesNotContain(hex.Inspector.RowTexts, text => text.EndsWith(marker, StringComparison.Ordinal));
        Assert.DoesNotContain(marker, _w.StatusLeftText);

        // copy and cut give the bytes of a value as stars, and a cut that touches one deletes nothing
        hex.Select(value - 8, value + 7); // API_KEY=hunter2
        string stars = HexEditor.ToHexText(Encoding.UTF8.GetBytes("API_KEY=*******"));
        await _w.Clipboard!.SetTextAsync("before");
        await hex.CopyAsync(cut: false);
        Assert.Equal(stars, await ClipboardAfterAsync(_w, "before"));
        await _w.Clipboard.SetTextAsync("before");
        await hex.CopyAsync(cut: true);
        Assert.Equal(stars, await ClipboardAfterAsync(_w, "before"));
        Assert.Equal(Env, Bytes(tab));
        Assert.False(tab.Doc.Doc.IsModified);

        // copy as and export as read the same masked bytes
        await _w.Clipboard.SetTextAsync("before");
        await _w.HexCopyAsAsync(hex, ByteFormat.HexSpaced);
        string spaced = (await ClipboardAfterAsync(_w, "before"))!.ToUpperInvariant();
        Assert.Contains("2A 2A 2A 2A 2A 2A 2A", spaced);
        Assert.DoesNotContain("68 75 6E 74 65 72 32", spaced);
        string exported = Path.Combine(_temp.Path, "values.bin");
        _dialogs.SaveFiles.Enqueue(exported);
        await _w.HexExportAsync(hex, format: null);
        Assert.Equal("API_KEY=*******", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(exported)));

        // the switch of the tab shows the bytes again, and copy gives them
        Assert.Equal(CommandOutcome.Handled, await RunAsync("nks.view.toggleSecrets"));
        Assert.True(tab.SecretsRevealed);
        Assert.False(hex.Describe(value).Hidden);
        Assert.Equal((byte)'h', hex.Describe(value).Value);
        await _w.Clipboard.SetTextAsync("before");
        await hex.CopyAsync(cut: false);
        Assert.Equal(HexEditor.ToHexText(Encoding.UTF8.GetBytes("API_KEY=hunter2")), await ClipboardAfterAsync(_w, "before"));
    }

    [AvaloniaFact]
    public void The_hex_view_of_another_file_hides_nothing()
    {
        var hex = OpenHex("notes.txt").Hex!;
        Assert.False(hex.SecretsHidden);
        Assert.False(hex.Describe(Env.IndexOf("hunter2", StringComparison.Ordinal)).Hidden);
        Assert.Equal((byte)'h', hex.Describe(Env.IndexOf("hunter2", StringComparison.Ordinal)).Value);
    }

    // ---- the setting, the exports ----

    [AvaloniaFact]
    public void The_setting_of_the_secret_files_decides_which_files_hide_their_values_in_the_tabs_that_are_open()
    {
        Assert.Equal(SecretValues.DefaultFilePatterns, _app.Workbench.Settings.GetString("files.secretFilePatterns"));
        Assert.Equal(SecretValues.DefaultFilePatterns, _app.Workbench.Settings.GetString("files.secretFilePatterns"));
        var vault = Open("deploy.vault", "TOKEN=hunter2\n");
        Assert.False(vault.SecretsHidden);
        Assert.Empty(Placeholders(vault));

        Assert.Null(_app.Workbench.Set("files.secretFilePatterns", ".env;*.vault"));
        Assert.Equal(".env;*.vault", SecretValues.FilePatterns);
        Assert.True(vault.SecretsHidden);
        Assert.Equal([(1, 7)], Placeholders(vault));

        var env = Open(".env", "TOKEN=hunter2\n");
        Assert.Equal([(1, 7)], Placeholders(env));
        Assert.Null(_app.Workbench.Set("files.secretFilePatterns", "*.vault")); // the old list no longer names .env
        Assert.False(env.SecretsHidden);
        Assert.Empty(Placeholders(env));
        Assert.True(vault.SecretsHidden);

        Assert.Null(_app.Workbench.Set("files.secretFilePatterns", ""));
        Assert.False(vault.SecretsHidden);
    }

    [AvaloniaFact]
    public async Task Export_and_copy_with_formatting_carry_the_placeholder_while_the_values_are_hidden()
    {
        var tab = Open(".env");
        foreach (var form in new[] { MainWindow.ExportForm.Html, MainWindow.ExportForm.Rtf })
        {
            string path = Path.Combine(_temp.Path, "out." + (form == MainWindow.ExportForm.Html ? "html" : "rtf"));
            _dialogs.SaveFiles.Enqueue(path);
            Assert.Equal(CommandOutcome.Handled, _w.ExportHighlighted(form));
            ToolTestKit.WaitFor(() => File.Exists(path) && _w.StatusMessageText == Loc.Get(K.ExportDone, ("name", Path.GetFileName(path))), "the export did not finish", 6000);
            string written = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("hunter2", written);
            Assert.DoesNotContain("PLAIN=x", written);
        }

        tab.Editor!.Select(Env.IndexOf("API_KEY", StringComparison.Ordinal), 15);
        Assert.Equal(CommandOutcome.Handled, _w.CopyHighlighted(htmlSource: false));
        ToolTestKit.WaitFor(() => _w.StatusMessageText == Loc.Get(K.ExportCopied), "the copy did not finish");
        Assert.Equal("API_KEY=" + Mask, _app.ClipboardHistory.Items[0]);
        Assert.DoesNotContain(_app.ClipboardHistory.Items, item => item.Contains("hunter2", StringComparison.Ordinal));

        // once the user shows the values, the export carries them
        tab.Editor.ToggleSecrets();
        string shown = Path.Combine(_temp.Path, "shown.html");
        _dialogs.SaveFiles.Enqueue(shown);
        _w.ExportHighlighted(MainWindow.ExportForm.Html);
        ToolTestKit.WaitFor(() => File.Exists(shown) && _w.StatusMessageText == Loc.Get(K.ExportDone, ("name", "shown.html")), "the export did not finish", 6000);
        Assert.Contains("hunter2", await File.ReadAllTextAsync(shown));
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
