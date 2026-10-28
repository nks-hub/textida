using NksHub.Textida.Core.Changes;

namespace NksHub.Textida.Core.Tests;

/// <summary>P6.1.1 for the watched folder: what the review of changes from outside leaves out (git folders, ignore files, transcripts of agents) and the batches of changed files.</summary>
public class ReviewFolderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-review-folder-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string P(params string[] parts) => Path.Combine([_dir, .. parts]);

    private string Put(string relative, string text = "x")
    {
        string path = P(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    // ---- what is left out ----

    [Fact]
    public void The_git_and_codegraph_folders_and_anything_outside_the_root_are_left_out()
    {
        var ignore = new ReviewIgnore(_dir, ignoreCase: false);
        Assert.True(ignore.IsIgnored(P(".git", "HEAD")));
        Assert.True(ignore.IsIgnored(P(".git", "refs", "heads", "main")));
        Assert.True(ignore.IsIgnored(P(".codegraph", "index.db")));
        Assert.True(ignore.IsIgnored(P("sub", ".git", "config")));
        Assert.True(ignore.IsIgnored(P("worktree", ".git"))); // the file that points a linked work tree to its repository
        Assert.False(ignore.IsIgnored(P("src", "a.cs")));
        Assert.False(ignore.IsIgnored(P(".github", "ci.yml"))); // only the two names, not every dot folder
        Assert.False(ignore.IsIgnored(P("notes.gitignore.txt")));
        Assert.True(ignore.IsIgnored(Path.Combine(Path.GetDirectoryName(_dir)!, "elsewhere.txt")));
        Assert.True(ignore.IsIgnored(_dir));
    }

    [Fact]
    public void Ignore_files_are_read_the_way_the_file_walk_reads_them()
    {
        Put(".gitignore", "bin/\n*.log\n!keep.log\n/build\ndocs/*.tmp\n");
        Put("src/.gitignore", "generated/\n!a.log\n");
        Put("src/.ignore", "scratch.txt\n");
        var ignore = new ReviewIgnore(_dir, ignoreCase: false);

        Assert.True(ignore.IsIgnored(P("bin", "x.dll")));
        Assert.True(ignore.IsIgnored(P("src", "bin", "x.dll"))); // a folder rule without a slash inside matches at any depth
        Assert.True(ignore.IsIgnored(P("bin", "deep", "er", "f.txt"))); // an ignored folder takes its content with it
        Assert.True(ignore.IsIgnored(P("bin", "keep.log"))); // a negation does not bring back what lies in an ignored folder
        Assert.True(ignore.IsIgnored(P("a.log")));
        Assert.False(ignore.IsIgnored(P("keep.log")));
        Assert.False(ignore.IsIgnored(P("logs", "keep.log")));
        Assert.True(ignore.IsIgnored(P("logs", "b.log")));
        Assert.True(ignore.IsIgnored(P("build", "o.obj")));
        Assert.False(ignore.IsIgnored(P("src", "build", "o.obj"))); // anchored to the folder of the ignore file
        Assert.True(ignore.IsIgnored(P("docs", "x.tmp")));
        Assert.False(ignore.IsIgnored(P("docs", "sub", "x.tmp"))); // * does not cross a slash
        Assert.True(ignore.IsIgnored(P("src", "generated", "g.cs")));
        Assert.False(ignore.IsIgnored(P("generated", "g.cs"))); // the rule of src applies from src down
        Assert.False(ignore.IsIgnored(P("src", "a.log"))); // a deeper ignore file overrides a shallower one
        Assert.True(ignore.IsIgnored(P("src", "scratch.txt"))); // .ignore is read like .gitignore
        Assert.False(ignore.IsIgnored(P("scratch.txt")));
        Assert.False(ignore.IsIgnored(P("src", "main.cs")));
    }

    [Fact]
    public void The_transcripts_of_agents_are_left_out_wherever_their_folders_lie()
    {
        string home = Path.Combine(Path.GetTempPath(), "someone");
        Assert.True(ReviewIgnore.IsAgentTranscript(Path.Combine(home, ".claude", "projects", "p1", "session.jsonl")));
        Assert.True(ReviewIgnore.IsAgentTranscript(Path.Combine(home, ".claude-work", "projects", "p1", "session.jsonl")));
        Assert.True(ReviewIgnore.IsAgentTranscript(Path.Combine(home, ".codex", "sessions", "2026", "01", "rollout.jsonl")));
        Assert.False(ReviewIgnore.IsAgentTranscript(Path.Combine(home, ".claude", "settings.json")));
        Assert.False(ReviewIgnore.IsAgentTranscript(Path.Combine(home, "work", "projects", "a.cs")));
        Assert.False(ReviewIgnore.IsAgentTranscript(Path.Combine(home, ".codex", "config.toml")));

        var ignore = new ReviewIgnore(_dir, ignoreCase: false);
        Assert.True(ignore.IsIgnored(P(".claude", "projects", "p", "s.jsonl")));
        Assert.True(ignore.IsIgnored(P(".codex", "sessions", "2026", "r.jsonl")));
        Assert.False(ignore.IsIgnored(P("notes", "projects", "x.md")));
    }

    [Fact]
    public void A_changed_ignore_file_is_read_again_after_refresh_and_not_before()
    {
        Put(".gitignore", "*.txt\n");
        var ignore = new ReviewIgnore(_dir, ignoreCase: false);
        Assert.True(ignore.IsIgnored(P("a.txt")));
        File.Delete(P(".gitignore"));
        Assert.True(ignore.IsIgnored(P("a.txt"))); // the rules of the batch in hand stay the same
        ignore.Refresh();
        Assert.False(ignore.IsIgnored(P("a.txt")));
    }

    [Fact]
    public void Names_and_patterns_follow_the_case_rule_given()
    {
        Put(".gitignore", "Build/\n");
        Assert.False(new ReviewIgnore(_dir, ignoreCase: false).IsIgnored(P("build", "o.obj")));
        Assert.True(new ReviewIgnore(_dir, ignoreCase: true).IsIgnored(P("build", "o.obj")));
        Assert.True(new ReviewIgnore(_dir, ignoreCase: true).IsIgnored(P(".GIT", "HEAD")));
        Assert.False(new ReviewIgnore(_dir, ignoreCase: false).IsIgnored(P(".GIT", "HEAD")));
    }

    // ---- the batches ----

    private static async Task<bool> Until(Func<bool> condition, int milliseconds = 10_000)
    {
        for (var clock = System.Diagnostics.Stopwatch.StartNew(); clock.ElapsedMilliseconds < milliseconds; await Task.Delay(20))
            if (condition()) return true;
        return condition();
    }

    [Fact]
    public async Task A_burst_of_writes_is_one_batch_of_the_files_that_are_not_left_out()
    {
        string changed = Put("src/a.txt", "one");
        Put("bin/o.dll");
        Put(".git/x");
        Put(".codegraph/y");
        Put(".gitignore", "bin/\n");
        using var watcher = new FolderChangeWatcher([_dir], TimeSpan.FromMilliseconds(150), 100);
        var seen = new List<string>();
        watcher.Changed += batch => { lock (seen) seen.AddRange(batch.Files); };
        watcher.Start();

        File.WriteAllText(changed, "two");
        string created = Put("src/b.txt");
        Put("bin/o.dll", "changed");
        Put(".git/x", "changed");
        Put(".codegraph/y", "changed");
        Directory.CreateDirectory(P("src", "newfolder"));

        Assert.True(await Until(() => { lock (seen) return seen.Contains(changed) && seen.Contains(created); }), "the changed and the created file were not reported");
        await Task.Delay(500); // anything that was going to arrive late has arrived
        lock (seen)
        {
            Assert.All(seen, f => Assert.Contains(f, new[] { changed, created }));
            Assert.DoesNotContain(P("src", "newfolder"), seen);
        }
    }

    [Fact]
    public async Task A_batch_names_the_files_the_folder_reported_as_created_and_not_the_ones_that_were_only_written()
    {
        string written = Put("old.txt", "one");
        using var watcher = new FolderChangeWatcher([_dir], TimeSpan.FromMilliseconds(150), 100);
        var batches = new List<FolderChangeBatch>();
        watcher.Changed += batch => { lock (batches) batches.Add(batch); };
        watcher.Start();

        File.WriteAllText(written, "two");
        string created = Put("fresh.txt", "x");

        Assert.True(await Until(() => { lock (batches) return batches.SelectMany(b => b.Files).Contains(written) && batches.SelectMany(b => b.Files).Contains(created); }), "both files were not reported");
        await Task.Delay(500);
        lock (batches)
        {
            Assert.Contains(batches, b => b.IsCreated(created));
            Assert.DoesNotContain(batches, b => b.IsCreated(written));
        }
        Assert.False(new FolderChangeBatch([created], 0).IsCreated(created)); // a batch built without the information names nothing as created
    }

    [Fact]
    public async Task A_batch_holds_at_most_the_given_number_of_files_and_counts_what_did_not_fit()
    {
        using var watcher = new FolderChangeWatcher([_dir], TimeSpan.FromMilliseconds(400), 3);
        var batches = new List<FolderChangeBatch>();
        watcher.Changed += batch => { lock (batches) batches.Add(batch); };
        watcher.Start();

        for (int i = 0; i < 10; i++) Put($"many/f{i}.txt");

        Assert.True(await Until(() => { lock (batches) return batches.Count > 0; }), "no batch arrived");
        await Task.Delay(800);
        lock (batches)
        {
            Assert.All(batches, b => Assert.True(b.Files.Count <= 3, $"{b.Files.Count} files in one batch"));
            Assert.True(batches.Sum(b => b.Dropped) > 0, "nothing was counted as dropped");
        }
    }

    [Fact]
    public async Task A_stopped_watcher_reports_nothing_and_a_missing_folder_is_no_error()
    {
        var watcher = new FolderChangeWatcher([_dir, P("not-there")], TimeSpan.FromMilliseconds(100), 10);
        int calls = 0;
        watcher.Changed += _ => Interlocked.Increment(ref calls);
        watcher.Start();
        watcher.Dispose();
        Put("late.txt");
        await Task.Delay(500);
        Assert.Equal(0, calls);
        watcher.Dispose(); // twice is fine
        Assert.Throws<ArgumentOutOfRangeException>(() => new FolderChangeWatcher([_dir], TimeSpan.Zero, 0));
    }
}
