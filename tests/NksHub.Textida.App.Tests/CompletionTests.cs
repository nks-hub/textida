using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Commands;
using AdapterCarets = NksHub.Textida.AvaloniaEditAdapter.Carets;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.5.5 through the running window: Ctrl+J offers the words of the open tabs and the clips of the language, and the choice replaces the word before every caret.</summary>
[Collection("LocState")]
public class CompletionTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private const string Header = "\"schemaVersion\":1,\"id\":\"acme.clips\",\"name\":\"Acme clips\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1}";

    public CompletionTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private void Install(string clips)
    {
        var dir = Path.Combine(_app.ProfileDirectory, "plugins", "acme");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), "{" + Header + ",\"contributes\":{\"clips\":[" + clips + "]}}", Encoding.UTF8);
        _app.ReloadClips();
    }

    private DocTab Open(string name, string content, int? caret = null)
    {
        var tab = _w.OpenFile(_temp.File(name, content), null);
        _w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tab.Editor!.TextArea.Focus();
        tab.Editor.CaretOffset = caret ?? tab.Editor.Document.TextLength;
        return tab;
    }

    private static string Text(DocTab t) => t.Editor!.Document.Text;
    private static void PlaceCarets(DocTab t, params int[] offsets) => t.Editor!.SetCarets(AdapterCarets.Create([.. offsets.Select(Sel.At)], offsets.Length - 1));

    [AvaloniaFact]
    public void The_offer_has_the_words_of_every_open_tab_and_the_clips_of_the_language_of_the_document()
    {
        Install("{\"id\":\"b1\",\"title\":\"Banner\",\"text\":\"<b>$|</b>\",\"language\":\"html\"},{\"id\":\"b2\",\"title\":\"Bandit\",\"text\":\"x\"},{\"id\":\"b3\",\"title\":\"Bank\",\"text\":\"y\",\"language\":\"css\"},{\"id\":\"z\",\"title\":\"Zebra\",\"text\":\"z\"}");
        Open("other.txt", "banana bandana bandana nothing");
        Open("mine.html", "ban");

        var offer = _w.GatherCompletion()!;

        Assert.Equal("ban", offer.Prefix);
        Assert.Equal(["bandana", "banana"], offer.Words);
        Assert.Equal(["Banner", "Bandit"], offer.Clips.Select(c => c.Clip.Title));
    }

    [AvaloniaFact]
    public void Ctrl_J_lists_the_words_and_then_the_clips()
    {
        Install("{\"id\":\"b1\",\"title\":\"Banner\",\"text\":\"<b>$|</b>\"}");
        Open("other.txt", "banana");
        var tab = Open("mine.txt", "ban");
        tab.Editor!.TextArea.Focus();

        TestApp.Press(_w, Key.J, RawInputModifiers.Control);

        Assert.Equal(["banana", "Banner"], _w.QuickResults.Select(r => r.Title));
    }

    [AvaloniaFact]
    public void Picking_a_word_replaces_the_word_in_front_of_every_caret_as_one_undo_step()
    {
        var tab = Open("w.txt", "banana\nba\nba");
        PlaceCarets(tab, 9, 12);
        var depth = tab.Doc.Doc.UndoDepth;

        Assert.Equal(CommandOutcome.Handled, _app.Workbench.Commands.ExecuteAsync("nks.edit.complete", new ShellContext(_w, forMenu: true)).AsTask().GetAwaiter().GetResult());
        var row = Assert.Single(_w.QuickResults);
        Assert.Equal("banana", row.Title);
        row.Run();

        Assert.Equal("banana\nbanana\nbanana", Text(tab));
        Assert.Equal(2, tab.Editor!.CaretCount);
        Assert.Equal([13, 20], tab.Editor.GetSelections().Select(s => s.Caret).Order());
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
        _w.Undo();
        Assert.Equal("banana\nba\nba", Text(tab));
    }

    [AvaloniaFact]
    public async Task Picking_a_clip_replaces_the_word_and_puts_the_caret_at_its_mark()
    {
        Install("{\"id\":\"b1\",\"title\":\"Banner\",\"text\":\"<b>$|</b>\",\"language\":\"html\"}");
        var tab = Open("c.html", "<p>ban");
        var depth = tab.Doc.Doc.UndoDepth;

        _w.ShowCompletion();
        var row = Assert.Single(_w.QuickResults);
        Assert.Equal("Banner", row.Title);
        row.Run();
        var sw = Stopwatch.StartNew();
        while (Text(tab) == "<p>ban" && sw.ElapsedMilliseconds < 10_000) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }

        Assert.Equal("<p><b></b>", Text(tab));
        Assert.Equal(6, tab.Editor!.CaretOffset);
        Assert.Equal(depth + 1, tab.Doc.Doc.UndoDepth);
    }

    [AvaloniaFact]
    public void Hidden_values_of_a_secrets_file_are_never_offered_and_shown_ones_only_in_the_file_itself()
    {
        var env = Open(".env", "API_KEY=supersecretvalue\nNAME=plainvalue\n");
        var other = Open("notes.txt", "");

        // another document: the keys, never a value
        var offer = _w.GatherCompletion()!;
        Assert.Contains("API_KEY", offer.Words);
        Assert.DoesNotContain("supersecretvalue", offer.Words);
        Assert.DoesNotContain("plainvalue", offer.Words);

        // shown in its own tab, still not offered to another document
        env.Editor!.ToggleSecrets();
        Assert.True(env.Editor.SecretsRevealed);
        Assert.DoesNotContain("supersecretvalue", _w.GatherCompletion()!.Words);
        Assert.True(ReferenceEquals(_w.Active, other));

        // inside the file itself: hidden gives keys only, shown gives the values too
        _w.CycleTab(1);
        Assert.True(ReferenceEquals(_w.Active, env));
        env.Editor.ToggleSecrets();
        Assert.False(env.Editor.SecretsRevealed);
        env.Editor.CaretOffset = env.Editor.Document.TextLength;
        var hidden = _w.GatherCompletion()!;
        Assert.DoesNotContain("supersecretvalue", hidden.Words);
        Assert.DoesNotContain("plainvalue", hidden.Words);
        Assert.Contains("API_KEY", hidden.Words);

        // the caret inside a hidden value: nothing is offered
        env.Editor.CaretOffset = 12;
        Assert.Null(_w.GatherCompletion());

        env.Editor.ToggleSecrets();
        env.Editor.CaretOffset = env.Editor.Document.TextLength;
        Assert.Contains("supersecretvalue", _w.GatherCompletion()!.Words);
    }

    [AvaloniaFact]
    public void The_offer_of_a_megabyte_file_has_the_words_of_all_tabs()
    {
        var text = new StringBuilder();
        for (int i = 0; text.Length < 1 << 20; i++) text.Append("alpha").Append(i % 5000).Append(i % 7 == 0 ? "\n" : " ");
        Open("big.txt", text.ToString());
        Open("small.txt", "alp");

        var offer = _w.GatherCompletion()!;

        Assert.Equal("alp", offer.Prefix);
        Assert.NotEmpty(offer.Words);
        Assert.All(offer.Words, w => Assert.StartsWith("alp", w));
    }

    [AvaloniaFact]
    [Trait("Category", "Perf")]
    public void The_offer_of_a_megabyte_file_takes_33_ms()
    {
        var text = new StringBuilder();
        for (int i = 0; text.Length < 1 << 20; i++) text.Append("alpha").Append(i % 5000).Append(i % 7 == 0 ? "\n" : " ");
        Open("big.txt", text.ToString());
        var tab = Open("small.txt", "alp");
        Install("{\"id\":\"b1\",\"title\":\"Alpine\",\"text\":\"x\"}");
        _w.GatherCompletion();
        var sw = Stopwatch.StartNew();
        var offer = _w.GatherCompletion()!;
        sw.Stop();
        Assert.NotEmpty(offer.Words);
        Assert.NotEmpty(offer.Clips);
        Assert.True(sw.ElapsedMilliseconds <= 33, $"{sw.ElapsedMilliseconds} ms");
        Assert.NotNull(tab);
    }
}
