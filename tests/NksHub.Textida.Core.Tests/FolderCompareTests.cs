using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.7.3: comparing two folders by size, time or content and copying one way without deleting.</summary>
public sealed class FolderCompareTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fc-" + Guid.NewGuid().ToString("N")[..8]);
    private string Left => Path.Combine(_root, "l");
    private string Right => Path.Combine(_root, "r");

    public FolderCompareTests()
    {
        Directory.CreateDirectory(Left);
        Directory.CreateDirectory(Right);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static void Put(string folder, string path, string text, DateTime time)
    {
        string full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        File.SetLastWriteTimeUtc(full, time);
    }

    private async Task<Dictionary<string, FolderState>> States(FolderCompareBy by) =>
        (await FolderCompare.CompareAsync(new LocalFolderSource(Left), new LocalFolderSource(Right), by, default)).ToDictionary(d => d.Path, d => d.State);

    [Fact]
    public async Task Size_time_and_content_each_decide_what_is_different()
    {
        Put(Left, "same.txt", "abc", Base);
        Put(Right, "same.txt", "abc", Base);
        Put(Left, "sub/size.txt", "abcd", Base);
        Put(Right, "sub/size.txt", "abc", Base);
        Put(Left, "time.txt", "aaa", Base);
        Put(Right, "time.txt", "bbb", Base.AddMinutes(5)); // same size, other time and other content
        Put(Left, "near.txt", "aaa", Base);
        Put(Right, "near.txt", "aaa", Base.AddSeconds(1)); // within the tolerance
        Put(Left, "only-left.txt", "x", Base);
        Put(Right, "only-right.txt", "y", Base);

        var bySize = await States(FolderCompareBy.Size);
        Assert.Equal(FolderState.Different, bySize["sub/size.txt"]);
        Assert.Equal(FolderState.Same, bySize["time.txt"]); // the size is all that counts here
        Assert.Equal(FolderState.OnlyLeft, bySize["only-left.txt"]);
        Assert.Equal(FolderState.OnlyRight, bySize["only-right.txt"]);

        var byTime = await States(FolderCompareBy.Time);
        Assert.Equal(FolderState.Different, byTime["time.txt"]);
        Assert.Equal(FolderState.Same, byTime["near.txt"]);
        Assert.Equal(FolderState.Same, byTime["same.txt"]);

        var byContent = await States(FolderCompareBy.Content);
        Assert.Equal(FolderState.Different, byContent["time.txt"]); // same size, other bytes
        Assert.Equal(FolderState.Same, byContent["near.txt"]); // other time, same bytes
        Assert.Equal(FolderState.Different, byContent["sub/size.txt"]);
    }

    [Fact]
    public async Task The_plan_copies_new_and_different_files_one_way_and_never_deletes()
    {
        Put(Left, "new.txt", "n", Base);
        Put(Left, "diff.txt", "left", Base);
        Put(Right, "diff.txt", "RIGHT!", Base);
        Put(Right, "extra.txt", "e", Base);
        var left = new LocalFolderSource(Left);
        var right = new LocalFolderSource(Right);
        var differences = await FolderCompare.CompareAsync(left, right, FolderCompareBy.Size, default);

        var toRight = FolderCompare.PlanCopy(differences, CopyDirection.LeftToRight);
        Assert.Equal(["diff.txt:True", "new.txt:False"], toRight.Select(s => $"{s.Path}:{s.Overwrites}"));
        Assert.Equal(["diff.txt:True", "extra.txt:False"], FolderCompare.PlanCopy(differences, CopyDirection.RightToLeft).Select(s => $"{s.Path}:{s.Overwrites}"));

        var times = differences.ToDictionary(d => d.Path, d => d.Left?.ModifiedUtc);
        var result = await FolderCompare.CopyAsync(left, right, toRight, p => times[p], null, default);

        Assert.Equal(2, result.Copied);
        Assert.Empty(result.Failed);
        Assert.Equal("left", File.ReadAllText(Path.Combine(Right, "diff.txt")));
        Assert.Equal("n", File.ReadAllText(Path.Combine(Right, "new.txt")));
        Assert.True(File.Exists(Path.Combine(Right, "extra.txt"))); // nothing is deleted
        Assert.Equal(Base, File.GetLastWriteTimeUtc(Path.Combine(Right, "new.txt")));
        Assert.Empty(Directory.GetFiles(Right, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_file_that_cannot_be_copied_is_recorded_and_the_others_go_on_and_a_path_cannot_leave_the_folder()
    {
        Put(Left, "ok.txt", "ok", Base);
        Put(Left, "locked.txt", "locked", Base);
        var left = new LocalFolderSource(Left);
        var right = new LocalFolderSource(Right);
        string locked = Path.Combine(Right, "locked.txt");
        FileStream? hold = null;
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(locked, "x");
            hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        else File.WriteAllText(Path.Combine(Directory.CreateDirectory(locked).FullName, "inside.txt"), "x"); // an open file does not stop a replace on Unix; a folder in its place does
        using var held = hold;
        var steps = new List<FolderCopyStep> { new("locked.txt", true, 6), new("ok.txt", false, 2) };

        var result = await FolderCompare.CopyAsync(left, right, steps, _ => null, null, default);

        Assert.Equal(1, result.Copied);
        Assert.Equal("locked.txt", Assert.Single(result.Failed).Path);
        Assert.Equal("ok", File.ReadAllText(Path.Combine(Right, "ok.txt")));

        var escape = await FolderCompare.CopyAsync(left, right, [new FolderCopyStep("../../escape.txt", false, 1)], _ => null, null, default);
        Assert.Single(escape.Failed);
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public async Task The_report_lists_only_what_differs_with_the_marks_for_each_side()
    {
        Put(Left, "a.txt", "1", Base);
        Put(Right, "a.txt", "22", Base);
        Put(Left, "b.txt", "1", Base);
        Put(Right, "b.txt", "1", Base);
        Put(Right, "c.txt", "1", Base);
        var left = new LocalFolderSource(Left);
        var right = new LocalFolderSource(Right);
        var differences = await FolderCompare.CompareAsync(left, right, FolderCompareBy.Size, default);

        string report = FolderCompare.Report(left, right, FolderCompareBy.Size, differences, "\n", by => by.ToString());

        Assert.Contains("! a.txt  (1 B 2026-01-01 12:00:00 / 2 B 2026-01-01 12:00:00)\n", report);
        Assert.Contains("> c.txt\n", report);
        Assert.DoesNotContain("b.txt", report);
    }
}
