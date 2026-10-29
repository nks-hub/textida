using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.6.1: the marks of the Git gutter. What Git says about a file is tested with the plugin (<c>GitPluginTests</c>); here is what the margin does with it.</summary>
[Collection("LocState")]
public class GitGutterTests : IDisposable
{
    public void Dispose() => Loc.SetLanguage(Loc.DefaultLanguage);

    private static GitMark Mark(int line, int lines, bool staged = false, GitMarkKind kind = GitMarkKind.Modified, int oldLines = 0, GitBase baseOf = GitBase.Index) =>
        new(line, lines, kind, staged, 0, oldLines, baseOf);

    [Fact]
    public void A_line_belongs_to_the_mark_that_covers_it_and_a_change_that_is_not_staged_wins_over_one_that_is()
    {
        var staged = Mark(2, 4, staged: true);
        var unstaged = Mark(4, 2);
        GitMark[] marks = [Mark(0, 1), staged, unstaged, Mark(10, 1, kind: GitMarkKind.Added)];

        Assert.Same(marks[0], GitMargin.MarkAt(marks, 0));
        Assert.Null(GitMargin.MarkAt(marks, 1));
        Assert.Same(staged, GitMargin.MarkAt(marks, 2));
        Assert.Same(staged, GitMargin.MarkAt(marks, 3));
        Assert.Same(unstaged, GitMargin.MarkAt(marks, 4)); // both cover it, the one a click would stage
        Assert.Same(unstaged, GitMargin.MarkAt(marks, 5));
        Assert.Null(GitMargin.MarkAt(marks, 6));
        Assert.Same(marks[3], GitMargin.MarkAt(marks, 10));
        Assert.Null(GitMargin.MarkAt(marks, 11));
        Assert.Null(GitMargin.MarkAt([], 0));
    }

    [Fact]
    public void A_removal_belongs_to_the_line_it_stands_before_and_to_no_other()
    {
        GitMark[] marks = [Mark(3, 0, kind: GitMarkKind.Removed)];

        Assert.Null(GitMargin.MarkAt(marks, 2));
        Assert.Same(marks[0], GitMargin.MarkAt(marks, 3));
        Assert.Null(GitMargin.MarkAt(marks, 4));
    }

    [Fact]
    public void The_tip_says_what_the_change_is_how_big_it_is_and_what_a_click_does()
    {
        Loc.SetLanguage("en");

        Assert.Equal("1 changed line (before: 1), not staged, against the index\nClick to show the change.", GitMargin.TipOf(Mark(1, 1, oldLines: 1)));
        Assert.Equal("3 changed lines (before: 2), not staged, against the index\nClick to show the change.", GitMargin.TipOf(Mark(1, 3, oldLines: 2)));
        Assert.Equal("2 added lines, staged, against HEAD\nClick to show the change.", GitMargin.TipOf(Mark(1, 2, staged: true, kind: GitMarkKind.Added, baseOf: GitBase.Head)));
        Assert.Equal("1 removed line, not staged, against the index\nClick to show the change.", GitMargin.TipOf(Mark(1, 0, kind: GitMarkKind.Removed, oldLines: 1)));
        Loc.SetLanguage("cs");
        Assert.StartsWith("1 změněný řádek (dříve 1), mimo index", GitMargin.TipOf(Mark(1, 1, oldLines: 1)));
        Assert.StartsWith("2 přidané řádky, v indexu", GitMargin.TipOf(Mark(1, 2, staged: true, kind: GitMarkKind.Added)));
        Assert.StartsWith("5 odstraněných řádků", GitMargin.TipOf(Mark(1, 0, kind: GitMarkKind.Removed, oldLines: 5)));
    }

    [Fact]
    public void Stepping_goes_to_the_next_change_after_the_one_the_caret_is_in_and_round_the_ends_of_the_file()
    {
        GitMark[] marks = [Mark(2, 3), Mark(10, 0, kind: GitMarkKind.Removed), Mark(20, 1, kind: GitMarkKind.Added)];

        Assert.Same(marks[0], GitMargin.StepFrom(marks, 0, forward: true)); // the caret is in none: the first one after it
        Assert.Same(marks[1], GitMargin.StepFrom(marks, 3, forward: true)); // in the middle of the first one: the one after it, not its own start
        Assert.Same(marks[2], GitMargin.StepFrom(marks, 10, forward: true));
        Assert.Same(marks[0], GitMargin.StepFrom(marks, 20, forward: true)); // after the last comes the first
        Assert.Same(marks[0], GitMargin.StepFrom(marks, 25, forward: true));
        Assert.Same(marks[2], GitMargin.StepFrom(marks, 0, forward: false)); // before the first comes the last
        Assert.Same(marks[0], GitMargin.StepFrom(marks, 10, forward: false));
        Assert.Same(marks[1], GitMargin.StepFrom(marks, 20, forward: false));
        Assert.Same(marks[1], GitMargin.StepFrom(marks, 15, forward: false));
        Assert.Same(marks[2], GitMargin.StepFrom(marks, marks[0], forward: false));
        Assert.Null(GitMargin.StepFrom([], 3, forward: true));
    }

    [Fact]
    public void Stepping_visits_every_change_once_and_comes_back_to_where_it_began()
    {
        GitMark[] marks = [Mark(1, 1), Mark(4, 2, staged: true), Mark(4, 2), Mark(9, 1), Mark(30, 0, kind: GitMarkKind.Removed)];
        var seen = new List<GitMark>();
        var mark = marks[0];
        for (int i = 0; i < 4; i++)
        {
            mark = GitMargin.StepFrom(marks, mark, forward: true)!;
            seen.Add(mark);
        }
        Assert.Equal([marks[2], marks[3], marks[4], marks[0]], seen); // where a staged and an unstaged change start together, the unstaged one
        for (int i = 0; i < 4; i++) mark = GitMargin.StepFrom(marks, mark, forward: false)!;
        Assert.Same(marks[0], mark);
    }

    [Fact]
    public void The_rows_of_the_peek_are_the_old_lines_then_the_new_ones_with_the_changed_words_of_lines_in_the_same_place_marked()
    {
        var mark = Mark(4, 2, oldLines: 2);
        var rows = GitPeekView.BuildRows(new GitPeek(1, mark, ["let total = 10;", "run(a, b);"], true, false), ["let total = 25;", "run(a, b, c);", "extra"]);

        Assert.Equal([true, true, false, false, false], rows.Select(r => r.Old));
        Assert.Equal(["let total = 10;", "run(a, b);", "let total = 25;", "run(a, b, c);", "extra"], rows.Select(r => r.Text));
        Assert.Equal("10", rows[0].Text.Substring(rows[0].Changed[0].Start, rows[0].Changed[0].Length));
        Assert.Equal("25", rows[2].Text.Substring(rows[2].Changed[0].Start, rows[2].Changed[0].Length));
        Assert.Empty(rows[1].Changed); // only a word was added to this line
        Assert.NotEmpty(rows[3].Changed);
        Assert.Empty(rows[4].Changed); // a line with no partner is changed as a whole, and not word by word

        var added = GitPeekView.BuildRows(new GitPeek(1, Mark(4, 1, kind: GitMarkKind.Added), [], true, false), ["x"]);
        Assert.Equal([false], added.Select(r => r.Old));
        Assert.Empty(added[0].Changed);
    }

    [Fact]
    public void The_plugin_is_asked_only_about_files_below_a_git_folder_or_a_git_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "textida-inrepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "sub", "deeper"));
        try
        {
            var file = Path.Combine(root, "sub", "deeper", "a.txt");
            Assert.False(PluginServices.InRepository(file));

            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Assert.True(PluginServices.InRepository(file));

            Directory.Delete(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "sub", ".git"), "gitdir: elsewhere"); // a linked worktree or a submodule has a file
            Assert.True(PluginServices.InRepository(file));
            Assert.False(PluginServices.InRepository(Path.Combine(root, "other.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void The_limits_of_the_gutter_stay_below_what_the_plugin_reads()
    {
        Assert.True(PluginLimitsPlaceholders.GitGutterMaxBytes <= PluginLimitsPlaceholders.VirtualDocumentMaxBytes);
        Assert.True(PluginLimitsPlaceholders.GitGutterDebounceMilliseconds > 0 && PluginLimitsPlaceholders.GitWatchDebounceMilliseconds > 0);
    }
}
