using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Files;
using NksHub.Textida.PluginHost;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.6.10: the rows of the file tree. Icons and colours by type, dimmed hidden entries and their switch, names with characters no font draws, the Git state from the Git plugin, and what a screen reader hears.</summary>
[Collection("LocState")]
public class FilePanelLookTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public FilePanelLookTests()
    {
        _app = TestApp.Prepare(_temp, new ScriptedDialogs());
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task WaitFor(Func<bool> condition, string what, int milliseconds = 90_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < milliseconds, "timed out waiting for " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    private async Task<string> FolderWithAsync(params string[] files)
    {
        string root = Path.Combine(_temp.Path, "work");
        foreach (var name in files)
        {
            string path = Path.Combine(root, name.TrimEnd('/'));
            if (name.EndsWith('/')) Directory.CreateDirectory(path);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "x");
            }
        }
        Assert.True(await _w.ShowFolderAsync(root));
        return root;
    }

    private Grid RowOf(string name) => (Grid)_w.BuildFileRow(_w.Files!.Rows.Single(r => r.Entry.Name == name));

    private static Avalonia.Controls.Shapes.Path IconOf(Grid row) => (Avalonia.Controls.Shapes.Path)row.Children[1];

    private static TextBlock NameOf(Grid row) => (TextBlock)row.Children[2];

    private static string LabelOf(Grid row) => AutomationProperties.GetName(row);

    [AvaloniaFact]
    public async Task Every_kind_of_entry_has_an_icon_of_its_own_and_the_class_that_gives_it_its_colour()
    {
        await FolderWithAsync("src/", "a.cs", "b.html", "c.css", "d.json", "e.txt", "f.png", "g.zip", "h.exe", "i.unknown");

        var rows = _w.Files!.Rows.Select(r => (Row: r, Grid: (Grid)_w.BuildFileRow(r))).ToList();

        Assert.Equal(10, rows.Count);
        Assert.All(rows, r =>
        {
            var kind = FileKinds.Of(r.Row.Entry.Name, r.Row.Entry.IsDirectory);
            Assert.Contains("filerow", r.Grid.Classes);
            Assert.Contains(FileGlyphs.ClassOf(kind), IconOf(r.Grid).Classes);
            Assert.Same(FileGlyphs.Of(kind, false), IconOf(r.Grid).Data);
        });
        Assert.Equal(10, rows.Select(r => IconOf(r.Grid).Data).Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.NotSame(FileGlyphs.Of(FileKind.Folder, true), FileGlyphs.Of(FileKind.Folder, false));
    }

    [AvaloniaFact]
    public async Task The_icon_and_the_name_take_their_colours_from_the_theme_in_the_light_and_the_dark_theme_and_in_high_contrast()
    {
        await FolderWithAsync("a.cs");
        _w.Show();
        _w.ToggleFiles(true);
        _w.UpdateLayout();
        await WaitFor(() => _w.FileList.GetVisualDescendants().OfType<Grid>().Any(g => g.Classes.Contains("filerow")), "the row", 10_000);
        var row = _w.FileList.GetVisualDescendants().OfType<Grid>().First(g => g.Classes.Contains("filerow"));
        var icon = IconOf(row);

        Color Resource(string key, ThemeVariant variant)
        {
            Assert.True(_app.TryFindResource(key, variant, out var value), key);
            return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
        }
        Color Stroke() => Assert.IsAssignableFrom<ISolidColorBrush>(icon.Stroke).Color;

        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            _app.RequestedThemeVariant = variant;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Resource("FileCode", variant), Stroke());
        }
        Assert.NotEqual(Resource("FileCode", ThemeVariant.Light), Resource("FileCode", ThemeVariant.Dark));

        _app.Contrast.Apply(ContrastColors.Black);
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ContrastColors.Black.WindowText, Stroke());
            foreach (var key in new[] { "GitModified", "GitNew", "GitDeleted", "GitConflict" }) Assert.Equal(ContrastColors.Black.HotLight, Resource(key, ThemeVariant.Dark));
            Assert.Equal(ContrastColors.Black.GrayText, Resource("GitIgnored", ThemeVariant.Dark));
        }
        finally { _app.Contrast.Restore(); }
    }

    [AvaloniaFact]
    public async Task A_hidden_entry_is_dimmed_and_named_hidden_and_the_switch_takes_it_out_of_the_tree()
    {
        await FolderWithAsync(".env", ".cache/", "visible.txt");

        var hidden = RowOf(".env");
        Assert.Contains("hiddenfile", hidden.Classes);
        Assert.Contains(Loc.Get(K.A11yFilesHidden), LabelOf(hidden));
        Assert.Contains("hiddenfile", RowOf(".cache").Classes);
        Assert.DoesNotContain("hiddenfile", RowOf("visible.txt").Classes);
        Assert.DoesNotContain(Loc.Get(K.A11yFilesHidden), LabelOf(RowOf("visible.txt")));

        var shown = _app.Workbench.Settings.GetBool("files.showHidden");
        Assert.True(shown); // dimmed, not left out, until the user says so
        bool? MenuCheck() => _w.MenuModel.SelectMany(Flatten).OfType<MenuCommand>().Single(c => c.CommandId == "nks.files.toggleHidden").Checked;
        Assert.True(MenuCheck()); // the switch is a command of the View menu with a check mark
        Assert.True(_app.Workbench.Commands.HasHandler("nks.files.toggleHidden"));

        _w.ToggleHiddenFiles();
        Assert.False(_w.ShowHiddenFiles);
        Assert.Equal(["visible.txt"], _w.Files!.Rows.Select(r => r.Entry.Name));
        Assert.False(MenuCheck());
        _w.ToggleHiddenFiles();
        Assert.Equal([".cache", ".env", "visible.txt"], _w.Files.Rows.Select(r => r.Entry.Name));
        Assert.True(MenuCheck());
    }

    private static IEnumerable<MenuNode> Flatten(MenuNode node) => node is MenuGroup group ? group.Items.SelectMany(Flatten).Prepend(node) : [node];

    [AvaloniaFact]
    public async Task A_name_with_a_character_no_font_draws_shows_the_number_of_the_character_instead_of_a_box()
    {
        string odd = "report" + (char)0xF03A + "2024.txt"; // what a tool writes where Windows would not allow a colon
        await FolderWithAsync(odd, "normal.txt");

        var row = (Grid)_w.BuildFileRow(_w.Files!.Rows.Single(r => r.Entry.Name == odd));

        Assert.Equal("report‹U+F03A›2024.txt", NameOf(row).Text);
        Assert.Contains("report‹U+F03A›2024.txt", LabelOf(row));
        Assert.Equal("normal.txt", NameOf(RowOf("normal.txt")).Text);
        Assert.Equal(odd, _w.Files.Rows.Single(r => r.Entry.Name == odd).Entry.Name); // only what is drawn changes, the file keeps its name
    }

    [AvaloniaFact]
    public async Task A_screen_reader_hears_the_type_hidden_and_the_state_in_words_in_every_language()
    {
        await FolderWithAsync("src/", ".secret.json", "photo.png");
        foreach (var language in Loc.Languages)
        {
            Loc.SetLanguage(language);
            var folder = LabelOf(RowOf("src"));
            Assert.Equal(Loc.Get(K.A11yFilesFolderClosed, ("name", "src")), folder);
            var file = LabelOf(RowOf("photo.png"));
            Assert.Equal(Loc.Get(K.A11yFilesFile, ("name", "photo.png"), ("kind", Loc.Get(K.A11yFilesKindImage))), file);
            var secret = LabelOf(RowOf(".secret.json"));
            Assert.Contains(Loc.Get(K.A11yFilesKindData), secret);
            Assert.Contains(Loc.Get(K.A11yFilesHidden), secret);
            Assert.DoesNotContain("{", secret); // no placeholder was left unfilled
        }
        Assert.Equal(Loc.Languages.Count, Loc.Languages.Select(l => { Loc.SetLanguage(l); return Loc.Get(K.A11yFilesKindImage); }).Distinct().Count());
    }

    [AvaloniaFact]
    public async Task A_folder_outside_a_repository_asks_nothing_and_starts_no_plugin()
    {
        await FolderWithAsync("a.txt");
        await Task.Delay(300);
        Dispatcher.UIThread.RunJobs();

        Assert.Empty(_w.GitFiles);
        Assert.Equal(PluginState.Inactive, _app.Plugins.HostForTests.GetState(PluginServices.GitPluginId));
        Assert.DoesNotContain("gitmodified", RowOf("a.txt").Classes);
    }

    /// <summary>A repository in a temporary folder; the real git program runs in it.</summary>
    private sealed class Repo(string dir)
    {
        public string Dir { get; } = dir;

        public string PathOf(string name) => Path.Combine(Dir, name.Replace('/', Path.DirectorySeparatorChar));

        public void Write(string name, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(name))!);
            File.WriteAllText(PathOf(name), text);
        }

        public void Run(params string[] args) => Execute(args, mustSucceed: true);

        /// <summary>A merge that ends in a conflict exits with a non-zero code.</summary>
        public void RunAllowFailure(params string[] args) => Execute(args, mustSucceed: false);

        private void Execute(string[] args, bool mustSucceed)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = Dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!;
            var error = p.StandardError.ReadToEndAsync();
            p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.True(!mustSucceed || p.ExitCode == 0, $"git {string.Join(' ', args)}: {error.Result}");
        }
    }

    private Repo NewRepo()
    {
        var repo = new Repo(Path.Combine(_temp.Path, "repo"));
        Directory.CreateDirectory(repo.Dir);
        repo.Run("init", "-q");
        repo.Run("config", "user.email", "test@example.invalid");
        repo.Run("config", "user.name", "Test");
        repo.Run("config", "core.autocrlf", "false");
        repo.Write(".gitignore", "*.log\nbuild/\n");
        repo.Write("a.txt", "a\n");
        repo.Write("b.txt", "b\n");
        repo.Write("sub/c.txt", "c\n");
        repo.Write("sub/deep/d.txt", "d\n");
        repo.Run("add", "-A");
        repo.Run("commit", "-q", "-m", "first");
        return repo;
    }

    private GitFileMark MarkOf(Repo repo, string name, bool directory = false) => GitFileStatus.StateOf(_w.GitFiles, repo.PathOf(name), directory);

    [AvaloniaFact]
    public async Task The_state_of_a_repository_comes_from_the_git_plugin_marks_the_files_and_the_folders_above_them_and_follows_a_change_from_outside()
    {
        var repo = NewRepo();
        repo.Write("a.txt", "a2\n");            // modified
        repo.Write("new.txt", "n\n");           // new
        repo.Write("sub/deep/d.txt", "d2\n");   // modified deep below a folder
        repo.Write("x.log", "l\n");             // ignored
        repo.Write("build/out.bin", "o\n");     // an ignored folder
        File.Delete(repo.PathOf("b.txt"));      // deleted: it has no row, its folder shows it

        Assert.True(await _w.ShowFolderAsync(repo.Dir));
        await WaitFor(() => _w.GitFiles.Count > 0, "the state from the Git plugin");

        Assert.Equal(GitFileState.Modified, MarkOf(repo, "a.txt").State);
        Assert.Equal(GitFileState.New, MarkOf(repo, "new.txt").State);
        Assert.Equal(GitFileState.Ignored, MarkOf(repo, "x.log").State);
        Assert.Equal(GitFileState.Ignored, MarkOf(repo, "build", directory: true).State);
        Assert.Equal(new GitFileMark(GitFileState.Modified, true), MarkOf(repo, "sub", directory: true));
        Assert.Equal(default, MarkOf(repo, ".gitignore"));

        var a = RowOf("a.txt");
        Assert.Contains("gitmodified", a.Classes);
        Assert.Equal("M", ((TextBlock)a.Children[3]).Text);
        Assert.Contains(Loc.Get(K.A11yFilesGitModified), LabelOf(a));
        var created = RowOf("new.txt");
        Assert.Contains("gitnew", created.Classes);
        Assert.Equal("N", ((TextBlock)created.Children[3]).Text);
        var folder = RowOf("sub");
        Assert.Contains("gitmodified", folder.Classes);
        Assert.Contains(Loc.Get(K.A11yFilesGitBelowModified), LabelOf(folder));
        Assert.Contains("gitignored", RowOf("build").Classes);
        Assert.Contains(Loc.Get(K.A11yFilesGitIgnored), LabelOf(RowOf("x.log")));
        Assert.DoesNotContain(_w.Files!.Rows, r => r.Entry.Name == "b.txt");

        // another program commits: the colours go without the user doing anything
        repo.Run("add", "-A");
        repo.Run("commit", "-q", "-m", "second");
        await WaitFor(() => MarkOf(repo, "a.txt").State == GitFileState.None && MarkOf(repo, "new.txt").State == GitFileState.None, "the state after a commit made by another program", 60_000);
        Assert.Equal(GitFileState.Ignored, MarkOf(repo, "x.log").State);
        Assert.Equal(default, MarkOf(repo, "sub", directory: true));
        Assert.DoesNotContain("gitmodified", RowOf("a.txt").Classes);

        // and a file written by another program turns up with its state
        repo.Write("late.txt", "l\n");
        await WaitFor(() => MarkOf(repo, "late.txt").State == GitFileState.New, "a new file made by another program", 60_000);
        await WaitFor(() => _w.Files.Rows.Any(r => r.Entry.Name == "late.txt"), "its row");
        Assert.Contains("gitnew", RowOf("late.txt").Classes);
    }

    [AvaloniaFact]
    public async Task While_the_panel_is_closed_the_state_is_not_read_and_a_change_made_meanwhile_shows_when_it_opens()
    {
        var repo = NewRepo();
        repo.Write("a.txt", "a2\n");
        Assert.True(await _w.ShowFolderAsync(repo.Dir));
        await WaitFor(() => MarkOf(repo, "a.txt").State == GitFileState.Modified, "the state from the Git plugin");

        _w.ToggleFiles(false);
        repo.Run("add", "-A");
        repo.Run("commit", "-q", "-m", "second");
        for (int i = 0; i < 60; i++) // the watchers and their delays are over long before this
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Assert.Equal(GitFileState.Modified, MarkOf(repo, "a.txt").State);

        _w.ToggleFiles(true);
        await WaitFor(() => MarkOf(repo, "a.txt").State == GitFileState.None, "the state after the panel opened", 60_000);
    }

    [AvaloniaFact]
    public async Task A_conflict_is_the_heaviest_state_and_the_setting_turns_the_state_off()
    {
        var repo = NewRepo();
        repo.Run("checkout", "-q", "-b", "other");
        repo.Write("a.txt", "theirs\n");
        repo.Run("commit", "-q", "-am", "other");
        repo.Run("checkout", "-q", "-");
        repo.Write("a.txt", "ours\n");
        repo.Run("commit", "-q", "-am", "ours");
        repo.RunAllowFailure("merge", "other");
        repo.Write("sub/c.txt", "changed\n");

        Assert.True(await _w.ShowFolderAsync(repo.Dir));
        await WaitFor(() => _w.GitFiles.Count > 0, "the state from the Git plugin");

        Assert.Equal(GitFileState.Conflict, MarkOf(repo, "a.txt").State);
        var row = RowOf("a.txt");
        Assert.Contains("gitconflict", row.Classes);
        Assert.Equal("!", ((TextBlock)row.Children[3]).Text);
        Assert.Contains(Loc.Get(K.A11yFilesGitConflict), LabelOf(row));

        _app.Workbench.Set("files.gitStatus", System.Text.Json.Nodes.JsonValue.Create(false));
        await WaitFor(() => _w.GitFiles.Count == 0, "the state to go", 10_000);
        var plain = RowOf("a.txt");
        Assert.DoesNotContain("gitconflict", plain.Classes);
        Assert.Equal(3, plain.Children.Count); // no letter after the name
        Assert.DoesNotContain(Loc.Get(K.A11yFilesGitConflict), LabelOf(plain));
    }
}
