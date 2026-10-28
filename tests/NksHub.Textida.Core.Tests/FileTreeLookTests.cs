using System.Diagnostics;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.Core.Tests;

/// <summary>P3.6.10: what the file tree knows about an entry besides its name: its kind, whether it is hidden, how a name is written, and what Git says about it.</summary>
public class FileKindTests
{
    [Theory]
    [InlineData("src", true, FileKind.Folder)]
    [InlineData("Program.cs", false, FileKind.Code)]
    [InlineData("MAIN.PY", false, FileKind.Code)]
    [InlineData("index.html", false, FileKind.Markup)]
    [InlineData("App.csproj", false, FileKind.Markup)]
    [InlineData("site.css", false, FileKind.Style)]
    [InlineData("data.json", false, FileKind.Data)]
    [InlineData(".gitignore", false, FileKind.Data)]
    [InlineData("notes.txt", false, FileKind.Text)]
    [InlineData("README", false, FileKind.Text)]
    [InlineData("Dockerfile", false, FileKind.Code)]
    [InlineData("logo.PNG", false, FileKind.Image)]
    [InlineData("backup.tar.gz", false, FileKind.Archive)]
    [InlineData("Textida.exe", false, FileKind.Binary)]
    [InlineData("unknown.zzz", false, FileKind.File)]
    [InlineData("noextension", false, FileKind.File)]
    [InlineData("trailing.", false, FileKind.File)]
    [InlineData("v1.0", true, FileKind.Folder)]
    public void A_name_belongs_to_the_kind_of_its_extension_or_its_well_known_name(string name, bool directory, FileKind expected) =>
        Assert.Equal(expected, FileKinds.Of(name, directory));

    [Fact]
    public void Every_kind_is_reached_by_some_name()
    {
        string[] names = ["d", "a.zzz", "n.txt", "c.cs", "m.html", "j.json", "s.css", "i.png", "z.zip", "b.exe"];
        Assert.Equal(Enum.GetValues<FileKind>().ToHashSet(), names.Select((n, i) => FileKinds.Of(n, directory(i))).ToHashSet());
        static bool directory(int index) => index == 0;
    }
}

public class FileNameDisplayTests
{
    [Theory]
    [InlineData("plain name.txt", "plain name.txt")]
    [InlineData("žluťoučký kůň.txt", "žluťoučký kůň.txt")]
    [InlineData("日本語.md", "日本語.md")]
    [InlineData("📄 note.txt", "📄 note.txt")] // a surrogate pair of a character that exists is kept
    [InlineData("a\uF03Ab.txt", "a‹U+F03A›b.txt")] // a tool mapped a colon into the private-use area
    [InlineData("\uE000", "‹U+E000›")]
    [InlineData("tab\there", "tab‹U+0009›here")]
    [InlineData("\U000F0001.dat", "‹U+F0001›.dat")] // the supplementary private-use planes
    [InlineData("\u0378", "‹U+0378›")] // not assigned
    public void A_code_point_no_font_draws_is_written_as_its_number(string name, string expected) => Assert.Equal(expected, FileNames.Display(name));

    [Fact]
    public void A_surrogate_without_its_partner_is_written_as_its_number() => Assert.Equal("lone‹U+D800›.txt", FileNames.Display("lone" + (char)0xD800 + ".txt"));

    [Fact]
    public void A_name_without_such_a_character_is_the_same_string()
    {
        string name = "ordinary-name_1.cs";
        Assert.Same(name, FileNames.Display(name));
    }
}

public class GitFileStatusTests
{
    private static string Root => OperatingSystem.IsWindows() ? @"C:\work\repo" : "/work/repo";

    private static string P(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static GitFileStatus Status(params (GitFileState, string)[] entries) => GitFileStatus.Build(Root, entries);

    [Fact]
    public void A_file_has_its_own_state_and_a_folder_the_heaviest_state_below_it()
    {
        var status = Status((GitFileState.Modified, "src/app/a.cs"), (GitFileState.New, "src/app/b.cs"), (GitFileState.New, "docs/x.md"));

        Assert.Equal(new GitFileMark(GitFileState.Modified, false), status.StateOf(P("src/app/a.cs"), false));
        Assert.Equal(new GitFileMark(GitFileState.New, false), status.StateOf(P("src/app/b.cs"), false));
        Assert.Equal(new GitFileMark(GitFileState.Modified, true), status.StateOf(P("src/app"), true));
        Assert.Equal(new GitFileMark(GitFileState.Modified, true), status.StateOf(P("src"), true));
        Assert.Equal(new GitFileMark(GitFileState.New, true), status.StateOf(P("docs"), true));
        Assert.Equal(new GitFileMark(GitFileState.Modified, true), status.StateOf(Root, true));
        Assert.Equal(default, status.StateOf(P("src/app/clean.cs"), false));
        Assert.Equal(default, status.StateOf(P("other"), true));
    }

    [Fact]
    public void A_conflict_outweighs_a_change_which_outweighs_a_deletion_which_outweighs_a_new_file_and_ignored_files_stay_where_they_are()
    {
        var status = Status(
            (GitFileState.New, "a/new.txt"), (GitFileState.Deleted, "a/gone.txt"), (GitFileState.Ignored, "a/b/out.log"),
            (GitFileState.Deleted, "c/gone.txt"), (GitFileState.New, "c/new.txt"),
            (GitFileState.Modified, "d/m.txt"), (GitFileState.Conflict, "d/x.txt"),
            (GitFileState.Ignored, "e/only.log"));

        Assert.Equal(GitFileState.Deleted, status.StateOf(P("a"), true).State);
        Assert.Equal(default, status.StateOf(P("a/b"), true)); // an ignored file marks nothing above it
        Assert.Equal(GitFileState.Ignored, status.StateOf(P("a/b/out.log"), false).State);
        Assert.Equal(GitFileState.Deleted, status.StateOf(P("c"), true).State);
        Assert.Equal(GitFileState.Conflict, status.StateOf(P("d"), true).State);
        Assert.Equal(default, status.StateOf(P("e"), true));
    }

    [Fact]
    public void A_folder_git_names_whole_gives_its_state_to_everything_below_it()
    {
        var status = Status((GitFileState.Ignored, "node_modules/"), (GitFileState.New, "fresh/"));

        Assert.Equal(new GitFileMark(GitFileState.Ignored, false), status.StateOf(P("node_modules"), true));
        Assert.Equal(new GitFileMark(GitFileState.Ignored, false), status.StateOf(P("node_modules/pkg/deep/index.js"), false));
        Assert.Equal(new GitFileMark(GitFileState.New, false), status.StateOf(P("fresh/a/b.txt"), false));
        Assert.Equal(new GitFileMark(GitFileState.New, true), status.StateOf(Root, true)); // the new folder is a change of the folder above it
        Assert.Equal(default, status.StateOf(P("node_modules_other"), true));
    }

    [Fact]
    public void Paths_are_compared_the_way_the_system_compares_them()
    {
        var status = Status((GitFileState.Modified, "Src/A.cs"));
        var caseless = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
        Assert.Equal(caseless ? GitFileState.Modified : GitFileState.None, status.StateOf(P("src/a.cs"), false).State);
    }

    [Fact]
    public void The_letters_of_the_plugin_answer_and_the_states_belong_together()
    {
        foreach (var state in Enum.GetValues<GitFileState>().Where(s => s != GitFileState.None))
            Assert.Equal(state, GitFileStatus.FromCode(GitFileStatus.Code(state)));
        Assert.Equal(GitFileState.None, GitFileStatus.FromCode('x'));
    }

    [Fact]
    public void A_status_is_the_same_as_another_with_the_same_entries_and_the_list_asks_each_status_of_a_tree_with_several_folders()
    {
        Assert.True(Status((GitFileState.New, "a.txt")).SameAs(Status((GitFileState.New, "a.txt"))));
        Assert.False(Status((GitFileState.New, "a.txt")).SameAs(Status((GitFileState.Modified, "a.txt"))));
        Assert.False(Status((GitFileState.New, "a.txt")).SameAs(Status()));

        var other = OperatingSystem.IsWindows() ? @"C:\work\other" : "/work/other";
        var statuses = new[] { Status((GitFileState.New, "a.txt")), GitFileStatus.Build(other, [(GitFileState.Modified, "b.txt")]) };
        Assert.Equal(GitFileState.New, GitFileStatus.StateOf(statuses, P("a.txt"), false).State);
        Assert.Equal(GitFileState.Modified, GitFileStatus.StateOf(statuses, Path.Combine(other, "b.txt"), false).State);
        Assert.Equal(default, GitFileStatus.StateOf(statuses, Path.Combine(other, "c.txt"), false));
    }

    [Fact]
    public void The_git_folder_is_found_above_a_folder_and_in_the_file_of_a_linked_worktree()
    {
        using var dir = new JournalTempDir();
        var deep = Path.Combine(dir.Path, "repo", "a", "b");
        Directory.CreateDirectory(deep);
        Assert.Null(GitFileStatus.FindGitDirectory(deep));

        Directory.CreateDirectory(Path.Combine(dir.Path, "repo", ".git"));
        Assert.Equal(Path.Combine(dir.Path, "repo", ".git"), GitFileStatus.FindGitDirectory(deep));

        Directory.Delete(Path.Combine(dir.Path, "repo", ".git"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "elsewhere", "wt"));
        File.WriteAllText(Path.Combine(dir.Path, "repo", ".git"), "gitdir: ../elsewhere/wt\n");
        Assert.Equal(Path.GetFullPath(Path.Combine(dir.Path, "elsewhere", "wt")), GitFileStatus.FindGitDirectory(deep));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public void A_hundred_thousand_entries_are_built_and_read_in_well_under_a_second()
    {
        var entries = Enumerable.Range(0, 100_000).Select(i => (i % 3 == 0 ? GitFileState.Modified : GitFileState.New, $"d{i % 500}/s{i % 40}/f{i}.txt")).ToList();
        var sw = Stopwatch.StartNew();
        var status = GitFileStatus.Build(Root, entries);
        long built = sw.ElapsedMilliseconds;
        sw.Restart();
        int hits = 0;
        for (int i = 0; i < 100_000; i++)
            if (status.StateOf(P($"d{i % 500}/s{i % 40}/f{i}.txt"), false).State != GitFileState.None) hits++;
        long read = sw.ElapsedMilliseconds;
        Assert.Equal(100_000, hits);
        Assert.True(built < 1500 && read < 1500, $"built in {built} ms, 100000 lookups in {read} ms");
    }
}

[Collection("Latency")]
public class FileTreeHiddenAndContentTests
{
    [Fact]
    public async Task A_name_with_a_leading_dot_is_hidden_and_the_switch_takes_its_row_away_and_brings_it_back()
    {
        using var dir = new JournalTempDir();
        Directory.CreateDirectory(dir.Combine(".cache"));
        Directory.CreateDirectory(dir.Combine("src"));
        File.WriteAllText(dir.Combine(".env"), "");
        File.WriteAllText(dir.Combine("a.txt"), "");
        File.WriteAllText(Path.Combine(dir.Path, ".cache", "inner.txt"), "");
        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();

        Assert.Equal([".cache", "src", ".env", "a.txt"], tree.Rows.Select(r => r.Entry.Name));
        Assert.Equal([true, false, true, false], tree.Rows.Select(r => r.Entry.Hidden));

        var changes = 0;
        tree.Changed += () => changes++;
        tree.ShowHidden = false;
        Assert.Equal(["src", "a.txt"], tree.Rows.Select(r => r.Entry.Name));
        tree.ShowHidden = false;
        Assert.Equal(1, changes); // the same value changes nothing
        tree.ShowHidden = true;
        Assert.Equal([".cache", "src", ".env", "a.txt"], tree.Rows.Select(r => r.Entry.Name));
    }

    [Fact]
    public async Task The_hidden_attribute_hides_an_entry_on_windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var dir = new JournalTempDir();
        File.WriteAllText(dir.Combine("visible.txt"), "");
        File.WriteAllText(dir.Combine("secret.txt"), "");
        File.SetAttributes(dir.Combine("secret.txt"), FileAttributes.Hidden);
        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();
        Assert.Equal([false, true], tree.Rows.Select(r => r.Entry.Hidden).OrderBy(h => h));
        tree.ShowHidden = false;
        Assert.Equal(["visible.txt"], tree.Rows.Select(r => r.Entry.Name));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public async Task Switching_the_hidden_entries_of_a_folder_with_a_hundred_thousand_files_is_one_pass_over_its_listing()
    {
        using var dir = new JournalTempDir();
        string big = dir.Combine("big");
        Directory.CreateDirectory(big);
        Parallel.For(0, 100_000, i => File.WriteAllBytes(Path.Combine(big, (i % 10 == 0 ? ".h" : "f") + $"{i:D6}.txt"), []));
        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();
        await tree.ExpandAsync(big);
        Assert.Equal(100_001, tree.Rows.Count);

        var sw = Stopwatch.StartNew();
        tree.ShowHidden = false;
        long hiding = sw.ElapsedMilliseconds;
        Assert.Equal(90_001, tree.Rows.Count);
        sw.Restart();
        tree.ShowHidden = true;
        long showing = sw.ElapsedMilliseconds;
        Assert.Equal(100_001, tree.Rows.Count);
        Assert.True(hiding < 500 && showing < 500, $"hiding took {hiding} ms, showing {showing} ms");
    }

    [Fact]
    public async Task Writing_into_a_file_raises_the_content_event_and_lists_no_folder_again()
    {
        using var dir = new JournalTempDir();
        File.WriteAllText(dir.Combine("a.txt"), "one");
        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();
        var content = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int listings = 0;
        tree.ContentChanged += () => content.TrySetResult();
        tree.DiskChanged += _ => Interlocked.Increment(ref listings);
        tree.Watch();

        File.AppendAllText(dir.Combine("a.txt"), " two");
        await content.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, listings); // the names did not change, so no folder is listed again
    }
}
