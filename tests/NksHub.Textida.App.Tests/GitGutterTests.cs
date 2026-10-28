using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.6.1: the marks of the Git gutter. What Git says about a file is tested with the plugin (<c>GitPluginTests</c>); here is what the margin does with it.</summary>
[Collection("LocState")]
public class GitGutterTests : IDisposable
{
    public void Dispose() => Loc.SetLanguage(Loc.DefaultLanguage);

    private static GitMark Mark(int line, int lines, bool staged = false, GitMarkKind kind = GitMarkKind.Modified) => new(line, lines, kind, staged);

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
    public void The_tip_says_what_the_lines_are_and_what_a_click_does()
    {
        Loc.SetLanguage("en");

        Assert.Equal("Changed lines. Click to stage this change.", GitMargin.TipOf(Mark(1, 1)));
        Assert.Equal("Added lines, already staged. Click to take this change back from the index.", GitMargin.TipOf(Mark(1, 1, staged: true, kind: GitMarkKind.Added)));
        Assert.Equal("Removed lines. Click to stage this change.", GitMargin.TipOf(Mark(1, 0, kind: GitMarkKind.Removed)));
        Loc.SetLanguage("cs");
        Assert.StartsWith("Změněné řádky.", GitMargin.TipOf(Mark(1, 1)));
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
