using NksHub.Textida.App.Startup;

namespace NksHub.Textida.App.Tests;

public class CommandLineTests
{
    private static readonly string Cwd = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    private static string Full(string rel) => Path.GetFullPath(rel, Cwd);

    [Fact]
    public void No_arguments_means_empty_workspace()
    {
        var a = CommandLine.Parse([], Cwd);
        Assert.Null(a.Error);
        Assert.Empty(a.Files);
        Assert.False(a.NewWindow || a.Wait || a.Smoke);
    }

    [Theory]
    [InlineData("--project", "proj")]
    [InlineData("--project=proj")]
    public void Project_names_the_folder_of_the_window_resolved_against_the_callers_directory(params string[] args)
    {
        var a = CommandLine.Parse([.. args, "a.txt"], Cwd);
        Assert.Null(a.Error);
        Assert.Equal(Full("proj"), a.Project);
        Assert.Equal([new OpenRequest(Full("a.txt"), null)], a.Files);
        Assert.Null(CommandLine.Parse(["a.txt"], Cwd).Project);
    }

    [Fact]
    public void A_path_that_is_an_existing_folder_is_the_project_and_a_file_stays_a_file()
    {
        using var temp = new TempDir();
        Directory.CreateDirectory(Path.Combine(temp.Path, "proj"));
        var file = temp.File("a.txt", "x");

        var a = CommandLine.Parse(["proj", "a.txt"], temp.Path);

        Assert.Null(a.Error);
        Assert.Equal(Path.Combine(temp.Path, "proj"), a.Project);
        Assert.Equal([new OpenRequest(file, null)], a.Files);
        Assert.NotNull(CommandLine.Parse(["proj", "--project", "other"], temp.Path).Error);
    }

    [Theory]
    [InlineData("--project")]
    [InlineData("--project=")]
    [InlineData("--project", "a", "--project", "b")]
    public void Project_needs_one_folder(params string[] args) => Assert.NotNull(CommandLine.Parse(args, Cwd).Error);

    [Fact]
    public void Relative_paths_are_resolved_against_the_callers_directory()
    {
        var a = CommandLine.Parse(["a.txt", Path.Combine("sub", "b.txt")], Cwd);
        Assert.Equal([new OpenRequest(Full("a.txt"), null), new OpenRequest(Full(Path.Combine("sub", "b.txt")), null)], a.Files);
    }

    [Theory]
    [InlineData("a.txt", "--line", "7")]
    [InlineData("--line", "7", "a.txt")]
    [InlineData("--line=7", "a.txt")]
    [InlineData("a.txt", "--line=7")]
    public void Line_applies_to_the_neighbouring_path(params string[] args)
    {
        var a = CommandLine.Parse(args, Cwd);
        Assert.Null(a.Error);
        Assert.Equal([new OpenRequest(Full("a.txt"), 7)], a.Files);
    }

    [Fact]
    public void Each_path_gets_its_own_line()
    {
        var a = CommandLine.Parse(["a.txt", "--line", "3", "b.txt", "--line", "9", "c.txt"], Cwd);
        Assert.Equal([new OpenRequest(Full("a.txt"), 3), new OpenRequest(Full("b.txt"), 9), new OpenRequest(Full("c.txt"), null)], a.Files);
    }

    [Fact]
    public void Switches_are_recognised()
    {
        var a = CommandLine.Parse(["--new-window", "--wait", "x.txt"], Cwd);
        Assert.True(a.NewWindow);
        Assert.True(a.Wait);
        Assert.Single(a.Files);
        Assert.True(CommandLine.Parse(["--smoke"], Cwd).Smoke);
    }

    [Fact]
    public void Double_dash_makes_the_rest_paths()
    {
        var a = CommandLine.Parse(["--", "--wait"], Cwd);
        Assert.Null(a.Error);
        Assert.False(a.Wait);
        Assert.Equal([new OpenRequest(Full("--wait"), null)], a.Files);
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("--Wait")]
    [InlineData("--new-windows")]
    public void Unknown_switch_is_an_error(string option)
    {
        var a = CommandLine.Parse(["a.txt", option], Cwd);
        Assert.Contains(option, a.Error);
    }

    [Theory]
    [InlineData("a.txt", "--line")]
    [InlineData("a.txt", "--line", "x")]
    [InlineData("a.txt", "--line", "0")]
    [InlineData("a.txt", "--line", "-3")]
    [InlineData("a.txt", "--line=")]
    [InlineData("--line", "4")]
    public void Bad_line_is_an_error(params string[] args) => Assert.NotNull(CommandLine.Parse(args, Cwd).Error);

    [Fact]
    public void Wait_needs_a_path() => Assert.NotNull(CommandLine.Parse(["--wait"], Cwd).Error);

    [Fact]
    public void Diff_takes_two_paths_and_needs_both()
    {
        var a = CommandLine.Parse(["--wait", "--diff", "l.txt", Path.Combine("sub", "r.txt")], Cwd);
        Assert.Null(a.Error);
        Assert.Equal([Full("l.txt"), Full(Path.Combine("sub", "r.txt"))], a.Diff);
        Assert.Empty(a.Files);
        Assert.Null(CommandLine.Parse(["x.txt"], Cwd).Diff);
        Assert.NotNull(CommandLine.Parse(["--diff", "l.txt"], Cwd).Error);
        Assert.NotNull(CommandLine.Parse(["--diff", "l.txt", "--wait"], Cwd).Error);
        Assert.NotNull(CommandLine.Parse(["--diff", "a", "b", "--diff", "c", "d"], Cwd).Error);
    }
}
