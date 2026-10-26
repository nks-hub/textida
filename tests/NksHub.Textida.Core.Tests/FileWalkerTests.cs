using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.Core.Tests;

public class FileWalkerTests
{
    internal static void Write(string root, string rel, string content = "x\n")
    {
        string path = System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private static List<string> Walk(string root, FileWalkOptions? o = null, FileWalkStats? stats = null) =>
        [.. FileWalker.Walk(root, o ?? new FileWalkOptions { IncludeHidden = true }, stats).Select(e => System.IO.Path.GetRelativePath(root, e.Path).Replace('\\', '/')).Order(StringComparer.Ordinal)];

    [Fact]
    public void NestedIgnoreFilesNegationAndDirectoryPatternsGiveTheReferenceList()
    {
        using var dir = new JournalTempDir();
        string r = dir.Path;
        Write(r, ".gitignore", "*.log\nbuild/\n!keep.log\n/rootonly.txt\ndocs/**/*.tmp\n");
        Write(r, "a.txt");
        Write(r, "a.log");
        Write(r, "keep.log");
        Write(r, "rootonly.txt");
        Write(r, "sub/rootonly.txt");
        Write(r, "build/out.txt");
        Write(r, "sub/build/out.txt");
        Write(r, "docs/x.tmp");
        Write(r, "docs/deep/er/y.tmp");
        Write(r, "docs/z.txt");
        Write(r, "sub/.gitignore", "*.txt\n!important.txt\n/local.md\n");
        Write(r, "sub/b.txt");
        Write(r, "sub/important.txt");
        Write(r, "sub/local.md");
        Write(r, "sub/inner/local.md");
        Write(r, "sub/inner/c.txt");
        Write(r, "sub/inner/.gitignore", "!c.txt\n");
        Write(r, ".git/config", "[core]");
        Write(r, ".codegraph/db");

        var expected = new[]
        {
            ".gitignore", "a.txt", "docs/z.txt", "keep.log", "sub/.gitignore", "sub/important.txt", "sub/inner/.gitignore", "sub/inner/c.txt", "sub/inner/local.md",
        };
        Assert.Equal(expected.Order(StringComparer.Ordinal), Walk(r));
    }

    [Fact]
    public void DotIgnoreOverridesGitignoreInTheSameDirectory()
    {
        using var dir = new JournalTempDir();
        Write(dir.Path, ".gitignore", "*.cfg\n");
        Write(dir.Path, ".ignore", "!app.cfg\n*.bak\n");
        Write(dir.Path, "app.cfg");
        Write(dir.Path, "other.cfg");
        Write(dir.Path, "old.bak");
        Assert.Equal([".gitignore", ".ignore", "app.cfg"], Walk(dir.Path));
    }

    [Fact]
    public void HiddenFilesAreSkippedUnlessRequested()
    {
        using var dir = new JournalTempDir();
        Write(dir.Path, "visible.txt");
        Write(dir.Path, ".hidden.txt");
        Write(dir.Path, ".cache/inside.txt");
        var stats = new FileWalkStats();
        Assert.Equal(["visible.txt"], Walk(dir.Path, new FileWalkOptions(), stats));
        Assert.Equal(2, stats.Hidden);
        Assert.Equal(3, Walk(dir.Path, new FileWalkOptions { IncludeHidden = true }).Count);
    }

    [Fact]
    public void FilesOverTheSizeLimitAreLeftOutAndCounted()
    {
        using var dir = new JournalTempDir();
        Write(dir.Path, "small.txt", "abc");
        Write(dir.Path, "big.txt", new string('x', 5000));
        var stats = new FileWalkStats();
        Assert.Equal(["small.txt"], Walk(dir.Path, new FileWalkOptions { MaxFileSize = 1000 }, stats));
        Assert.Equal(1, stats.TooLarge);
    }

    [Fact]
    public void ExtraPatternsAndCustomIgnoreFileNamesApply()
    {
        using var dir = new JournalTempDir();
        Write(dir.Path, "a.cs");
        Write(dir.Path, "gen/b.cs");
        Write(dir.Path, "c.generated.cs");
        Write(dir.Path, ".textidaignore", "a.cs\n");
        var o = new FileWalkOptions { IncludeHidden = true, ExtraIgnorePatterns = ["gen/", "*.generated.cs"], IgnoreFileNames = [".textidaignore"] };
        Assert.Equal([".textidaignore"], Walk(dir.Path, o));
    }

    [Fact]
    public void IgnoreFilesCanBeSwitchedOff()
    {
        using var dir = new JournalTempDir();
        Write(dir.Path, ".gitignore", "*.txt\n");
        Write(dir.Path, "a.txt");
        Assert.Equal([".gitignore", "a.txt"], Walk(dir.Path, new FileWalkOptions { IncludeHidden = true, UseIgnoreFiles = false }));
    }

    [Fact]
    public void SymlinkLoopsAreNotFollowedForever()
    {
        using var dir = new JournalTempDir();
        Write(dir.Path, "real/file.txt");
        string link = System.IO.Path.Combine(dir.Path, "real", "loop");
        if (!TryLink(link, dir.Path)) { Assert.False(OperatingSystem.IsWindows(), "a junction can always be created on Windows"); return; }

        var stats = new FileWalkStats();
        var plain = Walk(dir.Path, new FileWalkOptions { FollowLinks = false }, stats);
        Assert.Equal(["real/file.txt"], plain);
        Assert.Equal(1, stats.Links);

        stats = new FileWalkStats();
        var followed = Walk(dir.Path, new FileWalkOptions { FollowLinks = true }, stats);
        Assert.Equal(["real/file.txt"], followed);
        Assert.Equal(1, stats.LinkLoops);
    }

    [Fact]
    public void ALinkToAnotherFolderIsFollowedOnlyWhenAsked()
    {
        using var dir = new JournalTempDir();
        using var other = new JournalTempDir();
        Write(dir.Path, "a.txt");
        Write(other.Path, "o.txt");
        if (!TryLink(System.IO.Path.Combine(dir.Path, "ext"), other.Path)) { Assert.False(OperatingSystem.IsWindows()); return; }
        Assert.Equal(["a.txt"], Walk(dir.Path));
        Assert.Equal(["a.txt", "ext/o.txt"], Walk(dir.Path, new FileWalkOptions { FollowLinks = true }));
    }

    internal static bool TryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows()) return false;
            var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true });
            p!.WaitForExit();
            return p.ExitCode == 0;
        }
    }

    /// <summary>Runs git and returns stdout; null when git is not installed.</summary>
    private static string? Git(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-c", "core.excludesFile=", "-c", "core.ignorecase=false", "-c", "core.quotepath=off" }.Concat(args)) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            var err = p.StandardError.ReadToEndAsync();
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? o : throw new InvalidOperationException(err.Result);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [Fact]
    public void ListMatchesGitForTrickyGitignoreSyntax()
    {
        using var dir = new JournalTempDir();
        string r = dir.Path;
        if (Git(r, "init", "-q") is null) return; // git is not installed here
        string[] paths =
        [
            "a.o", "b.O", "src/main.c", "src/main.o", "src/lib/x.o", "src/lib/y.c", "tmp/1.txt", "tmp/keep/2.txt", "tmp/keep/sub/3.txt",
            "foo/bar/baz.txt", "foo/bar.txt", "bar/foo/baz.txt", "x/y/z/deep.dat", "x/deep.dat", "deep.dat", "doc/notes.md", "doc/a/b/notes.md",
            "star*.txt".Replace("*", "S"), "q1.txt", "q2.txt", "qq.txt", "set/a1", "set/b2", "set/c3", "set/d4", "end space.txt", "#hash.txt", "!bang.txt",
            "v/1.log", "v/w/2.log", "v/w/keep.log", "lib/a.a", "lib/b.so", "out/gen/g.cs", "out/gen/sub/h.cs", "out/src.cs", "mid/a/b/c.txt", "mid/b/c.txt",
        ];
        foreach (var p in paths) Write(r, p);
        Write(r, ".gitignore", string.Join('\n',
            "*.o", "!src/main.o", "/tmp/*", "!/tmp/keep", "tmp/keep/sub/", "foo/bar", "/deep.dat", "x/**/deep.dat", "**/notes.md", "!doc/a/b/notes.md",
            "q?.txt", "set/[a-c]*", "set/[!a-c]4", "end\\ space.txt", "\\#hash.txt", "\\!bang.txt", "*.log", "!keep.log", "lib/*", "!lib/*.a",
            "out/gen/**", "!out/gen/sub/", "mid/**/c.txt", "# a comment", "", "  "));
        Write(r, "src/.gitignore", "*.c\n!y.c\n");

        var expected = Git(r, "ls-files", "--others", "--exclude-standard")!
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim('"')).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected, Walk(r));
    }
}
