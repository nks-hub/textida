using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>File > Open Recent (P2.1.2): the recent files of Go to Anything as a submenu, newest first, with a mark for a missing file and a command that clears the list.</summary>
[Collection("LocState")]
public class RecentMenuTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public RecentMenuTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null, look: TestApp.WindowsLook);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private MenuGroup Recent() => Assert.Single(_w.MenuModel.Single(g => g.Label == "Soubor").Items.OfType<MenuGroup>(), g => g.Label == "Otevřít poslední");

    private List<MenuCommand> Rows() => [.. Recent().Head!().OfType<MenuCommand>()];

    [AvaloniaFact]
    public void The_submenu_lists_the_recent_files_newest_first_with_digit_keys_and_the_path_as_tip()
    {
        Assert.Equal(15, _app.Workbench.Settings.GetInt("advanced.recentFilesCount"));
        var paths = new[] { "a.txt", "b.txt", "c.txt" }.Select(n => _temp.File(n, n)).ToList();
        foreach (var p in paths) _w.OpenFile(p, null);

        var group = Recent();
        Assert.Contains(group.Items.OfType<MenuCommand>(), c => c.CommandId == "nks.file.clearRecent" && c.Label == "Vymazat seznam posledních souborů");
        var rows = Rows();
        Assert.Equal(3, rows.Count);
        string folder = Path.GetDirectoryName(paths[0])!;
        Assert.StartsWith("1 c.txt", rows[0].Label);
        Assert.StartsWith("2 b.txt", rows[1].Label);
        Assert.StartsWith("3 a.txt", rows[2].Label);
        Assert.All(rows, r => Assert.Equal(0, r.AccessAt));
        Assert.Equal([paths[2], paths[1], paths[0]], rows.Select(r => r.Tip));
        Assert.Contains(Path.GetFileName(folder), rows[0].Label); // the folder tells two files of the same name apart

        // a click opens the file
        _w.CloseTab(_w.Active!);
        rows[2].Run();
        Assert.Equal(paths[0], _w.Active!.Path);
        Assert.StartsWith("1 a.txt", Rows()[0].Label); // opening it again puts it on top
    }

    [AvaloniaFact]
    public void A_missing_file_is_marked_and_offers_to_leave_the_list()
    {
        var keep = _temp.File("keep.txt", "k");
        var gone = _temp.File("gone.txt", "g");
        _app.Recent.Add(keep);
        _app.Recent.Add(gone);
        File.Delete(gone);

        var rows = Rows();
        Assert.Equal("1 gone.txt (chybí)", rows[0].Label[..rows[0].Label.IndexOf("  ·", StringComparison.Ordinal)]);
        Assert.DoesNotContain("chybí", rows[1].Label);

        _dialogs.Answers.Enqueue(1); // cancel: the row stays
        rows[0].Run();
        Assert.Contains(gone, _app.Recent.Files);
        _dialogs.Answers.Enqueue(0);
        rows[0].Run();
        Assert.Equal("Soubor chybí", _dialogs.Asked[^1].Title);
        Assert.Equal([keep], _app.Recent.Files);
        Assert.Null(_w.Active?.Path); // nothing was opened
    }

    [AvaloniaFact]
    public async Task Clear_empties_the_list_and_the_submenu_says_it_is_empty()
    {
        _app.Recent.Add(_temp.File("x.txt", "x"));
        await _app.Workbench.Commands.ExecuteAsync("nks.file.clearRecent", new ShellContext(_w, forMenu: true));

        Assert.Empty(_app.Recent.Files);
        var row = Assert.Single(Rows());
        Assert.Equal("Žádné poslední soubory", row.Label);
        Assert.False(row.Enabled);
        Assert.Equal("Seznam posledních souborů je prázdný", _w.StatusMessageText);
        Assert.Empty(RecentFiles.Load(_app.ProfileDirectory, () => 15).Files); // the cleared list is stored
    }

    [AvaloniaFact]
    public void The_drawn_submenu_reads_the_list_again_each_time_it_opens()
    {
        var first = _temp.File("first.txt", "1");
        _app.Recent.Add(first);
        var menu = _w.GetVisualDescendants().OfType<Menu>().Single();
        var file = menu.Items.OfType<MenuItem>().First();
        var recent = file.Items.OfType<MenuItem>().Single(m => AutomationProperties.GetName(m) == "Otevřít poslední");

        recent.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        Assert.Equal(["_1 first.txt", "-", "Znovu otevřít naposledy zavřenou kartu", "Vymazat seznam posledních souborů"], Headers(recent));
        Assert.Equal(first, ToolTip.GetTip((MenuItem)recent.Items[0]!));

        _app.Recent.Add(_temp.File("second.txt", "2"));
        recent.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
        Assert.Equal(["_1 second.txt", "_2 first.txt", "-", "Znovu otevřít naposledy zavřenou kartu", "Vymazat seznam posledních souborů"], Headers(recent));

        // a row of a file keeps its access key mark and loses the folder; the command loses its mark
        static List<string> Headers(MenuItem parent) => [.. parent.Items.Select(i => i is MenuItem { Header: string h } ? Cut(h) : "-")];
        static string Cut(string h) => h.IndexOf("  ·", StringComparison.Ordinal) is var at and >= 0 ? h[..at] : MenuAccessKeys.Unmark(h);
    }
}
