using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Tests;

/// <summary>Projects, favorites and named sessions: what they keep in the profile and what they bring back.</summary>
[Collection("LocState")]
public class WorkspaceAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App? _app;
    private MainWindow? _w;

    public void Dispose()
    {
        if (_app is not null) TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    /// <summary>The profile of the app under test: smoke runs keep it in a folder of their own.</summary>
    private string Profile => Path.Combine(_temp.Path, "smoke-" + Environment.ProcessId);

    private MainWindow Start()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        return _w = _app.CreateWindow(null);
    }

    private static async Task WaitFor(Func<bool> condition, int milliseconds = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < milliseconds, "timed out waiting for the UI");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private string Folder(string name, params string[] files)
    {
        string dir = Path.Combine(_temp.Path, name);
        Directory.CreateDirectory(dir);
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), "content of " + f + "\nsecond line\nthird line\n");
        return dir;
    }

    [AvaloniaFact]
    public void A_named_session_brings_back_tabs_groups_carets_and_unsaved_text()
    {
        var w = Start();
        string file = Folder("s", "a.txt", "b.txt") + Path.DirectorySeparatorChar + "a.txt";
        string other = Path.Combine(Path.GetDirectoryName(file)!, "b.txt");

        var a = w.OpenFile(file, null);
        a.Surface!.SetSelections([new Sel(a.Surface.Document.GetLineByNumber(2).Offset + 3, a.Surface.Document.GetLineByNumber(2).Offset + 6)]);
        a.Surface.Document.Insert(0, "EDIT ");
        var b = w.OpenFile(other, null);
        b.Surface!.SetSelections([new Sel(b.Surface.Document.GetLineByNumber(3).Offset + 2, b.Surface.Document.GetLineByNumber(3).Offset + 2)]);
        var untitled = w.NewDocument();
        untitled.Surface!.Document.Insert(0, "only in the session");
        w.SelectTab(a);

        Assert.True(w.SaveNamedSession("morning"));
        Assert.Equal("content of a.txt\nsecond line\nthird line\n", File.ReadAllText(file)); // the file on disk was not touched

        foreach (var t in w.Tabs.ToList()) w.CloseTab(t);
        Assert.Empty(w.Tabs);

        var saved = _app!.Workspace.Sessions().Single();
        Assert.Equal("morning", saved.Name);
        Assert.Empty(w.OpenNamedSession(saved));

        var tabs = w.Tabs.ToList();
        Assert.Equal(3, tabs.Count);
        var reopenedA = tabs.Single(t => t.Path == file);
        Assert.True(reopenedA.Doc.HasUnsavedWork);
        Assert.StartsWith("EDIT content of a.txt", reopenedA.Surface!.Document.Text);
        var selA = reopenedA.Surface.GetSelections()[0];
        Assert.Equal((2, 4), (reopenedA.Surface.Document.GetLocation(selA.Start).Line, reopenedA.Surface.Document.GetLocation(selA.Start).Column));
        Assert.Equal(3, selA.Length);
        var reopenedB = tabs.Single(t => t.Path == other);
        Assert.False(reopenedB.Doc.HasUnsavedWork);
        Assert.Equal((3, 3), (reopenedB.Surface!.Document.GetLocation(reopenedB.Surface.GetSelections()[0].Caret).Line, reopenedB.Surface.Document.GetLocation(reopenedB.Surface.GetSelections()[0].Caret).Column));
        Assert.Equal("only in the session", tabs.Single(t => t.Path is null).Surface!.Document.Text);
        Assert.Equal(reopenedA, w.Active);

        Assert.True(_app.Workspace.DeleteSession("morning"));
        Assert.Empty(_app.Workspace.Sessions());
    }

    [AvaloniaFact]
    public async Task A_project_with_two_folders_shows_both_in_the_panel_and_comes_back_from_the_profile()
    {
        var w = Start();
        string one = Folder("one", "x.txt");
        string two = Folder("two", "y.txt");
        Assert.True(await w.ShowFolderAsync(one));
        await w.AddFolderToPanelAsync(two);
        await WaitFor(() => w.Files!.Rows.Count(r => r.Depth == 0 && r.Entry.IsDirectory && r.Entry.Path is var p && (p == one || p == two)) == 2);
        Assert.Contains(w.Files!.Rows, r => r.Entry.Path == Path.Combine(one, "x.txt"));
        Assert.Contains(w.Files.Rows, r => r.Entry.Path == Path.Combine(two, "y.txt"));

        w.OpenFile(Path.Combine(two, "y.txt"), null);
        w.SaveProject("pair");
        Assert.Equal("pair", _app!.Workspace.Active!.Name);

        // another run of the app reads the same profile
        var reloaded = WorkspaceStore.Load(Profile).Find("pair")!;
        Assert.Equal([one, two], reloaded.Folders);
        Assert.Equal([Path.Combine(two, "y.txt")], reloaded.Files);

        Assert.True(await w.ShowFolderAsync(Folder("lone", "z.txt")));
        Assert.Null(_app.Workspace.Active);
        Assert.Single(w.Files!.Roots);

        Assert.True(await w.OpenProjectAsync(reloaded));
        await WaitFor(() => w.Files!.Roots.Count == 2);
        Assert.Equal([one, two], w.Files!.Roots);
        Assert.Contains(w.Files.Rows, r => r.Entry.Path == Path.Combine(two, "y.txt"));
        Assert.Equal("pair", _app.Workspace.Active!.Name);
    }

    [AvaloniaFact]
    public void Favorites_persist_and_a_missing_one_is_marked_in_the_list()
    {
        var w = Start();
        string file = Folder("f", "keep.txt") + Path.DirectorySeparatorChar + "keep.txt";
        string gone = Path.Combine(_temp.Path, "gone", "away.txt");
        Assert.True(_app!.Workspace.AddFavorite(file));
        Assert.True(_app.Workspace.AddFavorite(gone));
        Assert.False(_app.Workspace.AddFavorite(file));

        Assert.Equal([file, gone], WorkspaceStore.Load(Profile).Favorites);

        w.ShowFavorites();
        var rows = w.QuickResults;
        Assert.Equal(2, rows.Count);
        Assert.True(rows.Single(r => r.Detail == file).Enabled);
        var missing = rows.Single(r => r.Detail == gone);
        Assert.False(missing.Enabled);
        Assert.Equal(Loc.Get(K.FavoriteMissing), missing.Reason);
        w.ClosePalette();

        w.OpenFavorite(file);
        Assert.Equal(file, w.Active!.Path);
        Assert.True(_app.Workspace.RemoveFavorite(gone));
        Assert.Equal([file], WorkspaceStore.Load(Profile).Favorites);
    }

    [AvaloniaFact]
    public void A_damaged_session_or_workspace_file_is_reported_and_the_start_goes_on()
    {
        var good = WorkspaceStore.Load(Profile);
        good.SaveSession(new SessionData { Name = "fine", Groups = [new SessionGroupData { Tabs = [new SessionTabData { Text = "kept" }] }] });
        File.WriteAllText(Path.Combine(Profile, "sessions", "broken.json"), "{ this is not json");
        File.WriteAllText(Path.Combine(Profile, "sessions", "nameless.json"), "{}");
        File.WriteAllText(Path.Combine(Profile, "workspace.json"), "garbage");

        var w = Start();
        w.StartWorkspace();

        Assert.Contains(_app!.Workspace.Problems, p => p.StartsWith("workspace.json", StringComparison.Ordinal));
        Assert.Contains(w.Notices, n => n.Message.Contains("workspace.json", StringComparison.Ordinal));
        Assert.Equal(["fine"], _app.Workspace.Sessions().Select(s => s.Name));
        Assert.Contains(_app.Workspace.Problems, p => p.Contains("broken.json", StringComparison.Ordinal));
        Assert.Contains(_app.Workspace.Problems, p => p.Contains("nameless.json", StringComparison.Ordinal));

        w.ShowSessions();
        Assert.Equal(["fine"], w.QuickResults.Select(r => r.Title));
        Assert.Contains(w.Notices, n => n.Message.Contains("broken.json", StringComparison.Ordinal));
        w.ClosePalette();

        Assert.Empty(w.OpenNamedSession(_app.Workspace.Sessions().Single()));
        Assert.Equal("kept", w.Active!.Surface!.Document.Text);
    }
}
