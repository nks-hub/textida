using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.5.1 as a user lives it: everything goes through the commands of the app and the dialogs are answered from a script. The app is stopped the way a closed window stops it
/// and started again on the same profile, and the second run finds what the first one left.
/// </summary>
[Collection("LocState")]
public class WorkspaceScenarioTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App _app;
    private MainWindow _w;

    public WorkspaceScenarioTests()
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

    private string Profile => Path.Combine(_temp.Path, "smoke-" + Environment.ProcessId);

    private static async Task WaitFor(Func<bool> condition, string what, int milliseconds = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < milliseconds, "timed out waiting for " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private async Task RunAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await WaitFor(() => run.IsCompleted, command);
        Assert.Equal(CommandOutcome.Handled, await run);
    }

    private string Folder(string name, params string[] files)
    {
        string dir = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "content of " + f + "\n");
        return dir;
    }

    private void Restart()
    {
        _app = TestApp.Restart(_temp, _app, _dialogs);
        _w = _app.CreateWindow(null);
    }

    [AvaloniaFact]
    public async Task A_project_with_two_folders_and_its_tabs_and_the_favorites_are_there_after_a_restart_and_a_missing_favorite_is_marked()
    {
        string one = Folder("one", "a.txt", "b.txt");
        string two = Folder("two", "c.txt", "d.txt");
        string a = Path.Combine(one, "a.txt"), b = Path.Combine(one, "b.txt"), c = Path.Combine(two, "c.txt"), d = Path.Combine(two, "d.txt");

        // first run: the folders in the panel, two tabs, the project saved under a name, one more tab opened after that
        _dialogs.Folders.Enqueue(one);
        await RunAsync("nks.file.openFolder");
        await WaitFor(() => _w.Files is not null && _w.Files.Roots.Count == 1, "the first folder");
        _dialogs.Folders.Enqueue(two);
        await RunAsync("nks.project.addFolder");
        await WaitFor(() => _w.Files!.Roots.Count == 2, "the second folder");
        _w.OpenFile(a, null);
        _w.OpenFile(c, null);
        _dialogs.Prompts.Enqueue("Work");
        await RunAsync("nks.project.save");
        await WaitFor(() => _app.Workspace.Active?.Name == "Work", "the saved project");
        var saved = WorkspaceStore.Load(Profile).Find("Work")!;
        Assert.Equal([one, two], saved.Folders);
        Assert.Equal([a, c], saved.Files);
        _w.OpenFile(d, null);

        // favorites from the panel: a file, a folder and a file that is gone by the next run
        _w.SelectFilePath(b);
        await RunAsync("nks.favorite.add");
        _w.SelectFilePath(two);
        await RunAsync("nks.favorite.add");
        _w.SelectFilePath(a);
        await RunAsync("nks.favorite.add");
        Assert.Equal([b, two, a], WorkspaceStore.Load(Profile).Favorites);
        File.Delete(b);

        // second run on the same profile: nothing is open and the panel holds the last folder, not the project
        Restart();
        Assert.Empty(_w.Tabs);

        await RunAsync("nks.project.open");
        var project = Assert.Single(_w.QuickResults);
        Assert.Equal(("Work", $"{one}; {two}"), (project.Title, project.Detail));
        project.Run();
        await WaitFor(() => _w.Files is not null && _w.Files.Roots.Count == 2 && _w.Tabs.Count() == 3, "the project to open");
        Assert.Equal([one, two], _w.Files!.Roots);
        Assert.Equal([a, c, d], _w.Tabs.Select(t => t.Path).Order());
        Assert.Equal(d, _w.Active!.Path); // the tab that was in front when the window closed
        Assert.Equal("Work", _app.Workspace.Active!.Name);
        Assert.Equal(Loc.Get(K.ProjectOpened, ("name", "Work")), _w.StatusMessageText);

        // the favorites: the file that is gone is dimmed and says so, the others open
        foreach (var tab in _w.Tabs.ToList()) _w.CloseTab(tab);
        await RunAsync("nks.favorite.open");
        var rows = _w.QuickResults;
        Assert.Equal(["b.txt", "two", "a.txt"], rows.Select(r => r.Title));
        Assert.Equal([false, true, true], rows.Select(r => r.Enabled));
        Assert.Equal(Loc.Get(K.FavoriteMissing), rows[0].Reason);
        Assert.Null(rows[1].Reason);
        rows[2].Run();
        _w.ClosePalette();
        await WaitFor(() => _w.Active?.Path == a, "the favorite file to open");

        _w.OpenFavorite(b);
        Assert.Equal(Loc.Get(K.FavoriteMissing) + ": " + b, _w.StatusMessageText);
        Assert.Single(_w.Tabs);

        // a favorite folder replaces the project in the panel
        await RunAsync("nks.favorite.open");
        _w.QuickResults[1].Run();
        _w.ClosePalette();
        await WaitFor(() => _w.Files!.Roots.Count == 1 && _w.Files.Roots[0] == two, "the favorite folder");
        Assert.Null(_app.Workspace.Active);

        // taking the dead favorite out of the list is remembered by the profile
        await RunAsync("nks.favorite.remove");
        _w.QuickResults.Single(r => r.Title == "b.txt").Run();
        _w.ClosePalette();
        Assert.Equal([two, a], _app.Workspace.Favorites);
        Assert.Equal([two, a], WorkspaceStore.Load(Profile).Favorites);
    }

    [AvaloniaFact]
    public async Task The_panel_menu_adds_the_selected_row_to_the_favorites_and_lists_them()
    {
        string one = Folder("one", "a.txt");
        string a = Path.Combine(one, "a.txt");
        Assert.True(await _w.ShowFolderAsync(one));
        var menu = _w.FileList.ContextMenu!.Items.OfType<MenuItem>().ToList();
        void Click(string id) => menu.Single(m => (string)m.Header! == _app.Workbench.Strings.Get(_app.Workbench.Commands.TryGet(id, out var c) ? c.TitleKey : id)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        _w.SelectFilePath(a);
        Click("nks.favorite.add");
        await WaitFor(() => _app.Workspace.Favorites.Count == 1, "the favorite");
        Assert.Equal([a], WorkspaceStore.Load(Profile).Favorites);

        Click("nks.favorite.open");
        await WaitFor(() => _w.IsPaletteOpen, "the list of favorites");
        Assert.Equal(["a.txt"], _w.QuickResults.Select(r => r.Title));
        _w.QuickResults[0].Run();
        _w.ClosePalette();
        await WaitFor(() => _w.Active?.Path == a, "the favorite to open");
    }
}
