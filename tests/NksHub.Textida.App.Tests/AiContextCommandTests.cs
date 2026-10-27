using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.9.1 through the running window: the scopes, the preview with the size and what was left out, and that nothing of a <c>.env</c> file reaches the clipboard.</summary>
[Collection("LocState")]
public class AiContextCommandTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public AiContextCommandTests()
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

    private async Task<string?> CopyAsync(string scope, int answer = 0)
    {
        await _w.Clipboard!.SetTextAsync("before");
        _dialogs.Answers.Enqueue(answer);
        Assert.Equal(CommandOutcome.Handled, _w.ShowAiContext());
        _w.SetPaletteQuery(scope);
        _w.RunPaletteSelection();
        await _w.AiContextTask;
        return await _w.Clipboard.TryGetTextAsync();
    }

    private DocTab OpenFile(string name, string text)
    {
        var tab = _w.OpenFile(_temp.File(name, text), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return tab;
    }

    [AvaloniaFact]
    public async Task The_file_goes_to_the_clipboard_as_markdown_with_path_lines_language_encoding_and_revision_after_a_preview_with_the_size()
    {
        var tab = OpenFile("A.cs", "int a;\nint b;\n");
        var text = await CopyAsync("This file");

        var preview = Assert.Single(_dialogs.Asked);
        Assert.Contains("Files: 1.", preview.Message);
        Assert.Contains(" as Markdown, ", preview.Message);
        Assert.Equal(["Copy as Markdown", "Copy as XML", "Cancel"], preview.Buttons.Select(b => b.Label));
        Assert.StartsWith("# Context\n\n## ", text);
        Assert.Contains("- language: csharp\n- encoding: UTF-8\n- revision: " + tab.Doc.Doc.Revision, text);
        Assert.Contains("### Lines 1-2\n\n```csharp\nint a;\nint b;\n```", text.ReplaceLineEndings("\n"));
    }

    [AvaloniaFact]
    public async Task The_second_button_copies_xml_and_cancel_copies_nothing()
    {
        OpenFile("A.cs", "int a;\n");
        var xml = await CopyAsync("This file", answer: 1);
        Assert.StartsWith("<context>", xml);
        Assert.Contains("<lines range=\"1\"><![CDATA[int a;", xml);

        Assert.Equal("before", await CopyAsync("This file", answer: 2));
    }

    [AvaloniaFact]
    public async Task Unsaved_changes_are_copied_and_marked_and_the_selection_scope_gives_only_the_selected_lines()
    {
        var tab = OpenFile("A.cs", "one\ntwo\nthree\n");
        tab.Editor!.Document.Text = "one\ntwo\nthree\nfour\n";
        Dispatcher.UIThread.RunJobs();
        var whole = await CopyAsync("This file");
        Assert.Contains("- unsaved changes: yes", whole);
        Assert.Contains("four", whole);

        tab.Editor.SetCarets(AdapterCarets.Create([new Sel(4, 7), new Sel(14, 18)], 1));
        var selected = await CopyAsync("Selection");
        Assert.Contains("### Lines 2\n\n```csharp\ntwo\n```", selected.ReplaceLineEndings("\n"));
        Assert.Contains("### Lines 4\n\n```csharp\nfour\n```", selected.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("one", selected);
    }

    [AvaloniaFact]
    public void Without_a_selection_the_selection_scope_is_disabled_with_the_reason()
    {
        OpenFile("A.cs", "x\n");
        _w.ShowAiContext();
        var row = _w.QuickResults.Single(r => r.Title == "Selection");
        Assert.False(row.Enabled);
        Assert.Equal(Loc.Get(K.AiNoSelection), row.Reason);
    }

    [AvaloniaFact]
    public async Task The_values_of_an_env_file_never_reach_the_clipboard_shown_or_hidden_and_the_preview_says_why()
    {
        var tab = OpenFile(".env", "TOKEN=hunter2\n");
        foreach (var scope in new[] { "This file", "Open tabs" })
        {
            Assert.Equal("before", await CopyAsync(scope));
            var preview = _dialogs.Asked[^1];
            Assert.Contains("Files: 0.", preview.Message);
            Assert.Contains(preview.Details!, d => d.EndsWith(".env: secrets file, its values are never copied"));
        }

        tab.Editor!.ToggleSecrets();
        Assert.True(tab.Editor.SecretsRevealed);
        tab.Editor.SetCarets(AdapterCarets.Create([new Sel(0, 13)], 1));
        Assert.Equal("before", await CopyAsync("Selection"));
        Assert.DoesNotContain("hunter2", string.Concat(_dialogs.Asked.Select(a => a.Message + string.Concat(a.Details ?? []))));
    }

    [AvaloniaFact]
    public async Task A_paged_file_is_copied_only_as_its_selection_and_listed_as_left_out_without_one()
    {
        _app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = _w.OpenFile(_temp.Bytes("big.log", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(1, 200_000).Select(i => $"line {i}\n")))), null);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++)
        {
            Thread.Sleep(25);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(tab.Doc.IsLarge);
        tab.Paged!.Focus();

        Assert.Equal("before", await CopyAsync("This file"));
        Assert.Contains(_dialogs.Asked[^1].Details!, d => d.EndsWith("big.log: large file in paged mode, only a selection is copied"));

        TestApp.Press(_w, Key.Down, RawInputModifiers.Shift);
        var text = await CopyAsync("This file");
        Assert.Contains("line 1\n", text.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("line 2\n", text.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("line 5000", text);
    }

    [AvaloniaFact]
    public async Task The_opened_folder_gives_its_text_files_and_names_env_binary_hidden_and_ignored_ones()
    {
        var root = Path.Combine(_temp.Path, "proj");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "main.cs"), "class Main { }\n");
        File.WriteAllText(Path.Combine(root, ".env"), "TOKEN=hunter2\n");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "out.txt\n");
        File.WriteAllText(Path.Combine(root, "out.txt"), "generated");
        File.WriteAllBytes(Path.Combine(root, "pic.bin"), [0, 1, 2]);
        _dialogs.Folders.Enqueue(root);
        await _w.OpenFolderWithDialogAsync();

        var text = await CopyAsync("Opened folder");
        Assert.Contains("## main.cs", text);
        Assert.DoesNotContain("hunter2", text);
        Assert.DoesNotContain("generated", text);
        var details = _dialogs.Asked[^1].Details!;
        Assert.Contains(".env: secrets file, its values are never copied", details);
        Assert.Contains("pic.bin: binary file", details);
        Assert.Contains("matched by ignore rules (files: 1)", details);
        Assert.Contains("hidden file or folder (files: 1)", details);
    }
}
