using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Plugins.Git;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.6.4: the commit, the commands that talk to a remote, the branches, the stashes and the log of the Git plugin, as a real process on real repositories and a real bare repository as the remote.</summary>
public partial class GitPluginTests
{
    static readonly string[] OperationArguments = ["--message-history", "5", "--max-pick-items", "10", "--max-text-length", "2000", "--log-entries", "20"];

    static string GitIn(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} in {dir}: {error.Result}");
        return output.Result;
    }

    static string Quote(string text) => JsonSerializer.Serialize(text);

    static string Nl(string text) => text.Replace("\r\n", "\n");

    static string StatusOf(JsonElement answer) => answer.GetProperty("status").GetString()!;

    /// <summary>A bare repository as the <c>origin</c> of a local one, and clones of it for the other people who push to it.</summary>
    sealed class Remote : IDisposable
    {
        readonly List<string> _folders = [];

        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "textida-remote-" + Guid.NewGuid().ToString("N"));

        public Remote(Repo local)
        {
            Directory.CreateDirectory(Dir);
            _folders.Add(Dir);
            GitIn(Dir, "init", "-q", "--bare");
            string branch = local.Run("branch", "--show-current").Trim();
            GitIn(Dir, "symbolic-ref", "HEAD", "refs/heads/" + branch);
            local.Run("remote", "add", "origin", Dir);
            local.Run("push", "-q", "-u", "origin", "HEAD");
        }

        /// <summary>Another person's copy of the repository.</summary>
        public string Clone()
        {
            var dir = Path.Combine(Path.GetTempPath(), "textida-clone-" + Guid.NewGuid().ToString("N"));
            _folders.Add(dir);
            GitIn(Path.GetTempPath(), "clone", "-q", "-c", "core.autocrlf=false", Dir, dir); // a global autocrlf would check the files out with CRLF, and the next commit there would change them
            GitIn(dir, "config", "user.email", "other@example.invalid");
            GitIn(dir, "config", "user.name", "Other");
            GitIn(dir, "config", "core.autocrlf", "false");
            return dir;
        }

        /// <summary>Another person commits a file and pushes it: from now on the remote has a commit the local repository does not have.</summary>
        public string TheyCommit(string file, string content, string message)
        {
            var dir = Clone();
            File.WriteAllText(Path.Combine(dir, file), content);
            GitIn(dir, "add", "-A");
            GitIn(dir, "commit", "-q", "-m", message);
            GitIn(dir, "push", "-q");
            return dir;
        }

        public void Dispose()
        {
            foreach (var folder in _folders.Where(Directory.Exists))
            {
                foreach (var f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(folder, true);
            }
        }
    }

    static void CommitIn(string dir, string file, string content, string message)
    {
        File.WriteAllText(Path.Combine(dir, file), content);
        GitIn(dir, "add", "-A");
        GitIn(dir, "commit", "-q", "-m", message);
    }

    static string[] Notices(Rig rig)
    {
        lock (rig.Ui.Notices) return [.. rig.Ui.Notices.Select(n => n.Message)];
    }

    static string PathsJson(params string[] paths) => $",\"paths\":[{string.Join(',', paths.Select(Quote))}]";

    /// <summary>A git:// address whose server takes the connection and never says a word: a command that talks to it waits until it is cancelled or times out.</summary>
    static string SilentRemote(out TcpListener listener)
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var held = new List<TcpClient>();
        var accepting = listener;
        _ = Task.Run(async () =>
        {
            try { while (true) held.Add(await accepting.AcceptTcpClientAsync()); }
            catch (Exception e) when (e is ObjectDisposedException or SocketException or InvalidOperationException) { }
        });
        return $"git://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/x.git";
    }

    // ---- the commit panel: the changes, staging, the commit ----

    [Fact]
    public async Task The_changes_of_a_repository_are_the_files_git_status_lists_with_the_branch_and_the_last_messages()
    {
        using var repo = new Repo(("a.txt", Original), ("b.txt", "b\n"));
        repo.Write("a.txt", ThreeChanges());
        repo.Write("b.txt", "B\n");
        repo.Run("add", "b.txt");
        repo.Write("c.txt", "new\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        var answer = await rig.RepoAsync("git.changes");

        Assert.Equal("changes", StatusOf(answer));
        Assert.Equal(Path.GetFileName(repo.Dir), Path.GetFileName(answer.GetProperty("root").GetString()!.TrimEnd('/', '\\')));
        Assert.Equal(repo.Run("branch", "--show-current").Trim(), answer.GetProperty("branch").GetString());
        Assert.Equal(JsonValueKind.Null, answer.GetProperty("upstream").ValueKind);
        Assert.True(answer.GetProperty("hasCommits").GetBoolean());
        Assert.Equal(3, answer.GetProperty("total").GetInt32());
        var entries = answer.GetProperty("entries").EnumerateArray().ToDictionary(e => e.GetProperty("path").GetString()!, e => e.GetProperty("index").GetString() + e.GetProperty("tree").GetString());
        Assert.Equal(new Dictionary<string, string> { ["a.txt"] = " M", ["b.txt"] = "M ", ["c.txt"] = "??" }, entries);
        Assert.Equal(["first"], answer.GetProperty("messages").EnumerateArray().Select(m => m.GetString()));
        Assert.Empty(Notices(rig)); // a read tells the user nothing
    }

    [Fact]
    public async Task The_changes_say_how_far_the_branch_is_from_its_upstream_by_what_was_fetched_last()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        using var remote = new Remote(repo);
        remote.TheyCommit("x.txt", "x\n", "theirs");
        CommitIn(repo.Dir, "m.txt", "m\n", "mine");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        var before = await rig.RepoAsync("git.changes");
        Assert.Equal((1, 0), (before.GetProperty("ahead").GetInt32(), before.GetProperty("behind").GetInt32())); // the remote was not asked
        Assert.EndsWith("/" + repo.Run("branch", "--show-current").Trim(), before.GetProperty("upstream").GetString());

        await rig.RepoAsync("git.fetch");
        var after = await rig.RepoAsync("git.changes");
        Assert.Equal((1, 1), (after.GetProperty("ahead").GetInt32(), after.GetProperty("behind").GetInt32()));
    }

    [Fact]
    public async Task Staging_and_unstaging_paths_moves_files_into_and_out_of_the_index_and_a_path_outside_the_repository_is_refused()
    {
        using var repo = new Repo(("a.txt", Original));
        repo.Write("a.txt", ThreeChanges());
        repo.Write("c.txt", "new\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        Assert.Equal("staged", StatusOf(await rig.RepoAsync("git.stagePaths", extra: PathsJson("a.txt", "c.txt"))));
        Assert.Equal("a.txt\nc.txt", Nl(repo.Run("diff", "--cached", "--name-only").Trim()));
        Assert.Equal("unstaged", StatusOf(await rig.RepoAsync("git.unstagePaths", extra: PathsJson("c.txt"))));
        Assert.Equal("a.txt", Nl(repo.Run("diff", "--cached", "--name-only").Trim()));
        Assert.Equal("c.txt", Nl(repo.Run("ls-files", "--others", "--exclude-standard").Trim()));

        File.Delete(repo.PathOf("a.txt"));
        Assert.Equal("staged", StatusOf(await rig.RepoAsync("git.stagePaths", extra: PathsJson("a.txt")))); // a deleted file is staged as deleted
        Assert.Contains("D\ta.txt", Nl(repo.Run("diff", "--cached", "--name-status")));

        Assert.Equal("failed", StatusOf(await rig.RepoAsync("git.stagePaths", extra: PathsJson("../outside.txt"))));
        Assert.Contains(Notices(rig), n => n.Contains("outside the repository", StringComparison.Ordinal));
    }

    [Fact]
    public async Task In_a_repository_without_a_commit_unstaging_takes_a_file_out_of_the_index_and_the_changes_say_there_is_no_commit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "textida-git-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            GitIn(dir, "init", "-q");
            File.WriteAllText(Path.Combine(dir, "n.txt"), "n\n");
            await using var rig = await Rig.CreateAsync(Path.Combine(dir, "n.txt"), extra: OperationArguments);
            await rig.RepoAsync("git.stagePaths", extra: PathsJson("n.txt"));
            Assert.Equal("n.txt", GitIn(dir, "ls-files").Trim());
            await rig.RepoAsync("git.unstagePaths", extra: PathsJson("n.txt"));
            Assert.Equal("", GitIn(dir, "ls-files").Trim());
            Assert.False((await rig.RepoAsync("git.changes")).GetProperty("hasCommits").GetBoolean());
        }
        finally
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task A_commit_makes_the_commit_git_would_with_the_message_and_amend_replaces_the_last_one()
    {
        using var repo = new Repo(("a.txt", Original));
        repo.Write("a.txt", ThreeChanges());
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);
        string Message(string text) => $",\"message\":{Quote(text)}";

        Assert.Equal("nothingStaged", StatusOf(await rig.RepoAsync("git.commit", extra: Message("nothing"))));
        await rig.RepoAsync("git.stagePaths", extra: PathsJson("a.txt"));
        Assert.Equal("noMessage", StatusOf(await rig.RepoAsync("git.commit", extra: Message("  \n"))));
        Assert.Equal("1", repo.Run("rev-list", "--count", "HEAD").Trim());

        var done = await rig.RepoAsync("git.commit", extra: Message("Change three lines\n\nWith a longer text, # not a comment.\n"));
        Assert.Equal("committed", StatusOf(done));
        Assert.Equal(repo.Run("rev-parse", "HEAD").Trim(), done.GetProperty("sha").GetString());
        Assert.Equal("Change three lines", done.GetProperty("subject").GetString());
        Assert.Equal("Change three lines\n\nWith a longer text, # not a comment.", Nl(repo.Run("log", "-1", "--format=%B")).TrimEnd());
        Assert.Equal(ThreeChanges(), Nl(repo.Run("show", "HEAD:a.txt")));
        Assert.Equal("", repo.Run("status", "--porcelain=v1").Trim());
        Assert.Contains(Notices(rig), n => n.StartsWith("Committed ", StringComparison.Ordinal));

        Assert.Equal("amended", StatusOf(await rig.RepoAsync("git.commit", extra: Message("A better subject") + ",\"amend\":true")));
        Assert.Equal("2", repo.Run("rev-list", "--count", "HEAD").Trim());
        Assert.Equal("A better subject", repo.Run("log", "-1", "--format=%B").Trim());
        Assert.Equal("amended", StatusOf(await rig.RepoAsync("git.commit", extra: ",\"amend\":true"))); // no message: the old one stays
        Assert.Equal("A better subject", repo.Run("log", "-1", "--format=%B").Trim());
        Assert.Equal("2", repo.Run("rev-list", "--count", "HEAD").Trim());
    }

    [Fact]
    public async Task A_commit_without_a_usable_author_says_so_and_makes_no_commit()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        repo.Run("config", "user.name", "");
        repo.Run("config", "user.email", "");
        repo.Write("a.txt", "b\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);
        await rig.RepoAsync("git.stagePaths", extra: PathsJson("a.txt"));

        Assert.Equal("identity", StatusOf(await rig.RepoAsync("git.commit", extra: $",\"message\":{Quote("x")}")));

        Assert.Contains(Notices(rig), n => n.Contains("user.name", StringComparison.Ordinal));
        Assert.Equal("1", repo.Run("rev-list", "--count", "HEAD").Trim());
    }

    [Fact]
    public async Task One_change_of_a_file_is_staged_without_an_open_document_and_the_commit_holds_only_that_change()
    {
        using var repo = new Repo(("f.txt", Original));
        repo.Write("f.txt", ThreeChanges());
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), extra: OperationArguments);
        string Hunk(int line, bool unstage) => $",\"file\":\"f.txt\",\"line\":{line},\"unstage\":{(unstage ? "true" : "false")}";

        Assert.Equal("noHunk", StatusOf(await rig.RepoAsync("git.stageFileHunk", extra: Hunk(3, false))));
        Assert.Equal("", repo.Run("diff", "--cached").Trim());

        Assert.Equal("staged", StatusOf(await rig.RepoAsync("git.stageFileHunk", extra: Hunk(5, false)))); // the sixth line is line 5 (0 based)
        Assert.Equal(Numbered(12, i => i == 6 ? "L6" : null), Nl(repo.Run("show", ":f.txt")));
        Assert.Equal(ThreeChanges(), Nl(File.ReadAllText(repo.PathOf("f.txt")))); // the file on disk is not touched

        await rig.RepoAsync("git.commit", extra: $",\"message\":{Quote("only the sixth")}");
        Assert.Equal(Numbered(12, i => i == 6 ? "L6" : null), Nl(repo.Run("show", "HEAD:f.txt")));
        var rest = repo.Run("diff", "-U0", "--no-color");
        Assert.Contains("+L2", rest);
        Assert.Contains("+L11", rest);
        Assert.DoesNotContain("+L6", rest);

        Assert.Equal("staged", StatusOf(await rig.RepoAsync("git.stageFileHunk", extra: Hunk(1, false))));
        Assert.Contains("+L2", repo.Run("diff", "--cached", "-U0", "--no-color"));
        Assert.Equal("unstaged", StatusOf(await rig.RepoAsync("git.stageFileHunk", extra: Hunk(1, true))));
        Assert.Equal("", repo.Run("diff", "--cached").Trim());
    }

    [Fact]
    public async Task A_new_file_can_be_staged_by_one_change_and_taken_out_of_the_index_again_by_the_last_one()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        repo.Write("n.txt", "one\ntwo\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        Assert.Equal("staged", StatusOf(await rig.RepoAsync("git.stageFileHunk", extra: ",\"file\":\"n.txt\",\"line\":0,\"unstage\":false")));
        Assert.Equal("one\ntwo\n", Nl(repo.Run("show", ":n.txt")));
        Assert.Equal("unstaged", StatusOf(await rig.RepoAsync("git.stageFileHunk", extra: ",\"file\":\"n.txt\",\"line\":0,\"unstage\":true")));
        Assert.Equal("", repo.Run("ls-files", "n.txt").Trim()); // not in the index any more, still on disk
        Assert.True(File.Exists(repo.PathOf("n.txt")));
    }

    [Fact]
    public async Task The_preview_gets_a_file_as_head_or_the_index_has_it_and_says_when_it_is_not_there_or_too_large()
    {
        using var repo = new Repo(("a.txt", "old\n"));
        repo.Write("a.txt", "new\n");
        repo.Run("add", "a.txt");
        repo.Write("a.txt", "newest\n");
        repo.Write("big.txt", new string('x', 70_000));
        repo.Run("add", "big.txt");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments); // a reply holds 60000 bytes in this rig

        async Task<JsonElement> Blob(string rev, string file) => await rig.RepoAsync("git.blob", extra: $",\"file\":\"{file}\",\"rev\":\"{rev}\"");

        Assert.Equal("old\n", Encoding.UTF8.GetString((await Blob("head", "a.txt")).GetProperty("bytes").GetBytesFromBase64()));
        Assert.Equal("new\n", Encoding.UTF8.GetString((await Blob("index", "a.txt")).GetProperty("bytes").GetBytesFromBase64()));
        var missing = await Blob("head", "big.txt"); // not in HEAD
        Assert.Equal("blob", StatusOf(missing));
        Assert.False(missing.GetProperty("exists").GetBoolean());
        Assert.Equal("tooLarge", StatusOf(await Blob("index", "big.txt")));
    }

    // ---- fetch, pull and push against a real remote ----

    [Fact]
    public async Task Fetch_brings_the_commits_of_the_remote_in_a_task_and_pull_merges_them_and_says_when_there_is_nothing_new()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        using var remote = new Remote(repo);
        remote.TheyCommit("theirs.txt", "t\n", "theirs");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        Assert.Equal("fetched", StatusOf(await rig.RepoAsync("git.fetch")));
        lock (rig.Ui.Started) Assert.Single(rig.Ui.Started);
        await T.Until(() => { lock (rig.Ui.Ended) return rig.Ui.Ended.Count == 1; });
        Assert.Equal(1, (await rig.RepoAsync("git.changes")).GetProperty("behind").GetInt32());
        Assert.False(File.Exists(repo.PathOf("theirs.txt"))); // fetch changes no file

        Assert.Equal("pulled", StatusOf(await rig.RepoAsync("git.pull", extra: ",\"mode\":\"merge\"")));
        Assert.True(File.Exists(repo.PathOf("theirs.txt")));
        Assert.Equal(0, (await rig.RepoAsync("git.changes")).GetProperty("behind").GetInt32());
        Assert.Equal("upToDate", StatusOf(await rig.RepoAsync("git.pull", extra: ",\"mode\":\"merge\"")));
    }

    [Fact]
    public async Task Pull_asks_how_to_combine_unless_told_and_a_rebase_keeps_the_history_straight_with_a_dirty_tree()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("keep.txt", "k\n"));
        using var remote = new Remote(repo);
        remote.TheyCommit("theirs.txt", "t\n", "theirs");
        CommitIn(repo.Dir, "mine.txt", "m\n", "mine");
        repo.Write("keep.txt", "dirty\n"); // an unstaged change the rebase must carry over
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        rig.Ui.Pick = _ => null;
        Assert.Equal("dismissed", StatusOf(await rig.RepoAsync("git.pull")));
        Assert.Equal(["merge", "rebase"], rig.Ui.Picks.Last().Items.Select(i => i.Id));
        lock (rig.Ui.Started) Assert.Empty(rig.Ui.Started); // nothing was started

        rig.Ui.Pick = request => request.Items.Single(i => i.Id == "rebase").Id;
        Assert.Equal("pulled", StatusOf(await rig.RepoAsync("git.pull")));
        Assert.Equal("", repo.Run("log", "--merges", "--oneline").Trim());
        Assert.Equal(["mine", "theirs", "first"], Nl(repo.Run("log", "--format=%s")).Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("dirty\n", Nl(File.ReadAllText(repo.PathOf("keep.txt"))));
    }

    [Fact]
    public async Task Push_shows_the_commits_that_go_out_asks_first_and_sets_the_upstream_of_a_new_branch()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        using var remote = new Remote(repo);
        CommitIn(repo.Dir, "m.txt", "m\n", "first of mine");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        ConfirmRequest? asked = null;
        rig.Ui.Confirm = request => { asked = request; return false; };
        Assert.Equal("dismissed", StatusOf(await rig.RepoAsync("git.push")));
        Assert.Contains("first of mine", asked!.Message);
        Assert.DoesNotContain("first of mine", GitIn(remote.Dir, "log", "--format=%s", "--all")); // declined: nothing was sent
        lock (rig.Ui.Started) Assert.Empty(rig.Ui.Started);

        rig.Ui.Confirm = _ => true;
        Assert.Equal("pushed", StatusOf(await rig.RepoAsync("git.push")));
        Assert.Contains("first of mine", GitIn(remote.Dir, "log", "--format=%s", "--all"));
        Assert.Equal("nothingToPush", StatusOf(await rig.RepoAsync("git.push")));

        repo.Run("checkout", "-q", "-b", "feature");
        CommitIn(repo.Dir, "f.txt", "f\n", "on the feature");
        Assert.Equal("pushed", StatusOf(await rig.RepoAsync("git.push")));
        Assert.Equal("origin/feature", repo.Run("rev-parse", "--abbrev-ref", "feature@{u}").Trim());
        Assert.Contains("on the feature", GitIn(remote.Dir, "log", "--format=%s", "feature"));
    }

    [Fact]
    public async Task A_push_the_remote_rejects_says_so_and_a_pull_that_stops_at_conflicts_leaves_them_in_the_status_panel()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        using var remote = new Remote(repo);
        remote.TheyCommit("a.txt", "theirs\n", "theirs");
        CommitIn(repo.Dir, "a.txt", "mine\n", "mine");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);
        rig.Ui.Confirm = _ => true;

        Assert.Equal("rejected", StatusOf(await rig.RepoAsync("git.push")));
        Assert.Contains(Notices(rig), n => n.Contains("rejected the push", StringComparison.Ordinal));
        Assert.DoesNotContain("mine", GitIn(remote.Dir, "log", "--format=%s", "--all"));

        var pulled = await rig.RepoAsync("git.pull", extra: ",\"mode\":\"merge\"");
        Assert.Equal("conflicts", StatusOf(pulled));
        Assert.Equal(1, pulled.GetProperty("count").GetInt32());
        Assert.Contains("UU a.txt", repo.Run("status", "--porcelain=v1"));
        await T.Until(() => rig.LatestPanel() is { } panel && panel.Items.Any(i => i.Label == "a.txt"));
        Assert.Contains(Notices(rig), n => n.Contains("conflicts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_command_that_talks_to_a_remote_can_be_cancelled_and_git_is_stopped()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        repo.Run("remote", "add", "origin", SilentRemote(out var silent));
        using var listener = silent;
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        var running = rig.RepoAsync("git.fetch");
        await T.Until(() => { lock (rig.Ui.Started) return rig.Ui.Started.Count == 1; });
        string id;
        lock (rig.Ui.Started) id = rig.Ui.Started[0].Id;
        Assert.True(await rig.UiApi.CancelTaskAsync("git", id));

        Assert.Equal("cancelled", StatusOf(await running.WaitAsync(TimeSpan.FromSeconds(30))));
        Assert.Contains(Notices(rig), n => n == "Cancelled.");
        await T.Until(() => { lock (rig.Ui.Ended) return rig.Ui.Ended.Count == 1; });
    }

    [Fact]
    public async Task A_command_that_talks_to_a_remote_ends_at_the_time_limit()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        repo.Run("remote", "add", "origin", SilentRemote(out var silent));
        using var listener = silent;
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: [.. OperationArguments, "--network-timeout-ms", "800"]);

        Assert.Equal("timeout", StatusOf(await rig.RepoAsync("git.fetch").WaitAsync(TimeSpan.FromSeconds(30))));
        Assert.Contains(Notices(rig), n => n.Contains("did not answer in time", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_user_name_and_password_in_the_address_of_a_remote_never_reach_a_message_and_a_failed_connection_is_told()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        repo.Run("remote", "add", "origin", "https://someone:s3cr3t-pass@127.0.0.1:1/x.git");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        Assert.Equal("failed", StatusOf(await rig.RepoAsync("git.fetch").WaitAsync(TimeSpan.FromSeconds(60))));

        Assert.NotEmpty(Notices(rig));
        Assert.All(Notices(rig), n => Assert.DoesNotContain("s3cr3t-pass", n, StringComparison.Ordinal));
        Assert.Equal("https://***@host/x", GitRepository.Scrub("https://user:secret@host/x"));
        Assert.Equal("https://***@host/x", GitRepository.Scrub("https://token@host/x"));
        Assert.Equal("git@host:x.git", GitRepository.Scrub("git@host:x.git")); // an ssh address has no password in it
    }

    [Theory]
    [InlineData(1, "To x\n ! [rejected]        main -> main (fetch first)\nerror: failed to push some refs", NetworkOutcome.Rejected)]
    [InlineData(1, "fatal: Authentication failed for 'https://host/x.git/'", NetworkOutcome.AuthFailed)]
    [InlineData(128, "fatal: could not read Username for 'https://host': terminal prompts disabled", NetworkOutcome.AuthFailed)]
    [InlineData(128, "git@host: Permission denied (publickey).", NetworkOutcome.AuthFailed)]
    [InlineData(128, "fatal: unable to access 'https://host/x.git/': Could not resolve host: host", NetworkOutcome.Failed)]
    [InlineData(0, "", NetworkOutcome.Done)]
    public void The_text_git_prints_tells_how_a_command_that_talks_to_a_remote_failed(int code, string error, NetworkOutcome expected) =>
        Assert.Equal(expected, GitRepository.Classify(new GitResult(code, "", error)));

    // ---- branches ----

    [Fact]
    public async Task A_branch_is_made_switched_to_and_deleted_and_a_name_git_refuses_is_told()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        string main = repo.Run("branch", "--show-current").Trim();
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        Assert.Equal("created", StatusOf(await rig.RepoAsync("git.createBranch", extra: ",\"branch\":\"feature/x\"")));
        Assert.Equal("feature/x", repo.Run("branch", "--show-current").Trim());
        Assert.Equal("failed", StatusOf(await rig.RepoAsync("git.createBranch", extra: ",\"branch\":\"bad name\"")));
        Assert.Contains(Notices(rig), n => n.Contains("is not a name a branch can have", StringComparison.Ordinal));

        rig.Ui.Pick = request => request.Items.Single(i => i.Label == main).Id;
        Assert.Equal("switched", StatusOf(await rig.RepoAsync("git.switchBranch")));
        Assert.Equal(main, repo.Run("branch", "--show-current").Trim());
        Assert.Equal([main], rig.Ui.Picks.Last().Items.Select(i => i.Label)); // the list does not offer the branch we were on

        rig.Ui.Pick = request => request.Items.Single(i => i.Label == "feature/x").Id;
        Assert.Equal("deleted", StatusOf(await rig.RepoAsync("git.deleteBranch"))); // merged: no question
        Assert.Equal("", repo.Run("branch", "--list", "feature/x").Trim());
    }

    [Fact]
    public async Task Switching_with_changes_in_the_tree_asks_first_and_a_branch_that_is_not_merged_is_deleted_only_after_a_second_question()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        string main = repo.Run("branch", "--show-current").Trim();
        repo.Run("checkout", "-q", "-b", "side");
        CommitIn(repo.Dir, "s.txt", "s\n", "only on side");
        repo.Run("checkout", "-q", main);
        repo.Write("a.txt", "dirty\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        ConfirmRequest? asked = null;
        rig.Ui.Confirm = request => { asked = request; return false; };
        Assert.Equal("dismissed", StatusOf(await rig.RepoAsync("git.switchBranch", extra: ",\"branch\":\"side\"")));
        Assert.Contains(": 1.", asked!.Message);
        Assert.Equal(main, repo.Run("branch", "--show-current").Trim());

        Assert.Equal("dismissed", StatusOf(await rig.RepoAsync("git.deleteBranch", extra: ",\"branch\":\"side\"")));
        Assert.Equal("side", repo.Run("branch", "--list", "side").Trim().TrimStart('*', ' '));
        rig.Ui.Confirm = _ => true;
        Assert.Equal("deleted", StatusOf(await rig.RepoAsync("git.deleteBranch", extra: ",\"branch\":\"side\"")));
        Assert.Equal("", repo.Run("branch", "--list", "side").Trim());

        Assert.Equal("created", StatusOf(await rig.RepoAsync("git.createBranch", extra: ",\"branch\":\"keepdirty\"")));
        Assert.Equal("dirty\n", Nl(File.ReadAllText(repo.PathOf("a.txt")))); // the change goes with the new branch
    }

    [Fact]
    public async Task A_branch_of_the_remote_becomes_a_local_branch_that_tracks_it()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        using var remote = new Remote(repo);
        var other = remote.Clone();
        GitIn(other, "checkout", "-q", "-b", "topic");
        CommitIn(other, "t.txt", "t\n", "on topic");
        GitIn(other, "push", "-q", "-u", "origin", "topic");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);
        await rig.RepoAsync("git.fetch");

        Assert.Equal("switched", StatusOf(await rig.RepoAsync("git.switchBranch", extra: ",\"branch\":\"origin/topic\"")));
        Assert.Equal("topic", repo.Run("branch", "--show-current").Trim());
        Assert.Equal("origin/topic", repo.Run("rev-parse", "--abbrev-ref", "topic@{u}").Trim());
        Assert.True(File.Exists(repo.PathOf("t.txt")));
    }

    // ---- stash ----

    [Fact]
    public async Task Stash_sets_the_changes_and_new_files_aside_brings_them_back_and_drops_after_a_question()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);
        Assert.Equal("nothingToStash", StatusOf(await rig.RepoAsync("git.stashSave", extra: ",\"message\":\"x\"")));

        repo.Write("a.txt", "changed\n");
        repo.Write("new.txt", "new\n");
        Assert.Equal("stashed", StatusOf(await rig.RepoAsync("git.stashSave", extra: ",\"message\":\"work in progress\"")));
        Assert.Equal("", repo.Run("status", "--porcelain=v1").Trim());
        Assert.Contains("work in progress", repo.Run("stash", "list"));

        Assert.Equal("applied", StatusOf(await rig.RepoAsync("git.stashApply", extra: ",\"stash\":\"stash@{0}\"")));
        Assert.Equal("changed\n", Nl(File.ReadAllText(repo.PathOf("a.txt"))));
        Assert.True(File.Exists(repo.PathOf("new.txt")));
        Assert.Contains("work in progress", repo.Run("stash", "list")); // apply keeps the stash

        rig.Ui.Confirm = _ => false;
        Assert.Equal("dismissed", StatusOf(await rig.RepoAsync("git.stashDrop", extra: ",\"stash\":\"stash@{0}\"")));
        Assert.Contains("work in progress", repo.Run("stash", "list"));
        rig.Ui.Confirm = _ => true;
        Assert.Equal("dropped", StatusOf(await rig.RepoAsync("git.stashDrop", extra: ",\"stash\":\"stash@{0}\"")));
        Assert.Equal("", repo.Run("stash", "list").Trim());
        Assert.Equal("dismissed", StatusOf(await rig.RepoAsync("git.stashApply"))); // none left: told, nothing asked
        Assert.Contains(Notices(rig), n => n == "There are no stashes.");
    }

    [Fact]
    public async Task A_stash_that_conflicts_with_the_tree_is_applied_with_conflicts_in_the_status_panel()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        repo.Write("a.txt", "stashed\n");
        repo.Run("stash", "push", "-q", "-m", "s");
        CommitIn(repo.Dir, "a.txt", "committed\n", "other change");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        Assert.Equal("conflicts", StatusOf(await rig.RepoAsync("git.stashApply", extra: ",\"stash\":\"stash@{0}\"")));
        Assert.Contains("UU a.txt", repo.Run("status", "--porcelain=v1"));
        await T.Until(() => rig.LatestPanel() is { } panel && panel.Items.Any(i => i.Label == "a.txt"));
    }

    // ---- history ----

    [Fact]
    public async Task The_history_of_a_file_lists_its_commits_and_opening_one_shows_its_change_and_the_history_of_the_repository_lists_all()
    {
        using var repo = new Repo(("a.txt", "one\n"), ("b.txt", "b\n"));
        CommitIn(repo.Dir, "a.txt", "one\ntwo\n", "second");
        CommitIn(repo.Dir, "b.txt", "b2\n", "only b");
        CommitIn(repo.Dir, "a.txt", "one\ntwo\nthree\n", "third");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);

        Assert.Equal("log", StatusOf(await rig.RepoAsync("git.logFile")));
        var panel = rig.LatestPanel()!;
        Assert.Equal("Git: history of a.txt", panel.Title);
        Assert.Equal(3, panel.Items.Count); // "only b" did not touch the file
        Assert.EndsWith("third", panel.Items[0].Label);
        Assert.EndsWith("first", panel.Items[2].Label);

        Assert.True(await rig.UiApi.ActivateItemAsync("git", GitPlugin.LogPanel, panel.Items[0].Id));
        await T.Until(() => rig.Tools.Diffs.Count == 1);
        Assert.Equal("one\ntwo\n", rig.VirtualText(0));
        Assert.Equal("one\ntwo\nthree\n", rig.VirtualText(1));
        Assert.Equal("a.txt (before " + panel.Items[0].Id[..8] + ")", rig.Tools.Diffs[0].Left.Title);

        Assert.Equal("log", StatusOf(await rig.RepoAsync("git.logRepo")));
        panel = rig.LatestPanel()!;
        Assert.Equal(4, panel.Items.Count);
        var onlyB = panel.Items.Single(i => i.Label.EndsWith("only b", StringComparison.Ordinal));
        Assert.True(await rig.UiApi.ActivateItemAsync("git", GitPlugin.LogPanel, onlyB.Id)); // one file in the commit: no question
        await T.Until(() => rig.Tools.Diffs.Count == 2);
        Assert.Equal("b.txt (" + onlyB.Id[..8] + ")", rig.Tools.Diffs[1].Right.Title);
    }

    [Fact]
    public async Task A_repository_command_for_a_folder_outside_a_repository_and_for_no_path_says_so()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        var outside = Path.Combine(Path.GetTempPath(), "textida-nogit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), extra: OperationArguments);
            Assert.Equal("noRepository", StatusOf(await rig.RepoAsync("git.changes", outside))); // a read: no notice
            Assert.Empty(Notices(rig));
            Assert.Equal("noRepository", StatusOf(await rig.RepoAsync("git.push", outside)));
            Assert.Contains(Notices(rig), n => n == "The file is not in a Git repository.");
            var json = "{\"command\":\"git.pull\",\"language\":\"en\"}";
            Assert.Equal("noFile", StatusOf((await rig.Host.CallAsync("git", "command/execute", Encoding.UTF8.GetBytes(json))).Clone()));
        }
        finally { Directory.Delete(outside, true); }
    }
}
