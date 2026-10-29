using System.Diagnostics;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Files;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Plugins.Git;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.6.1 and P8.6.2: the Git plugin as a real process against the real host, document API and UI API, on real temporary repositories.</summary>
[Collection("PATH")]
public class GitPluginTests
{
    sealed class Owner : IDocumentOwnerThread
    {
        readonly object _gate = new();

        public Task<T> InvokeAsync<T>(Func<T> work)
        {
            try { lock (_gate) return Task.FromResult(work()); }
            catch (Exception e) { return Task.FromException<T>(e); }
        }
    }

    sealed class Proposals : IEditProposals
    {
        public ValueTask<ProposalDecision> SubmitAsync(EditProposal proposal, CancellationToken cancellation) => throw new NotSupportedException();
    }

    /// <summary>A repository in a temporary folder with one commit.</summary>
    sealed class Repo : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "textida-git-" + Guid.NewGuid().ToString("N"));

        public Repo(params (string Name, string Content)[] files)
        {
            Directory.CreateDirectory(Dir);
            Run("init", "-q");
            Run("config", "user.email", "test@example.invalid");
            Run("config", "user.name", "Test");
            Run("config", "core.autocrlf", "false");
            foreach (var (name, content) in files) Write(name, content);
            Run("add", "-A");
            Run("commit", "-q", "-m", "first");
        }

        public string PathOf(string name) => Path.Combine(Dir, name);

        public void Write(string name, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOf(name))!);
            File.WriteAllBytes(PathOf(name), Encoding.UTF8.GetBytes(content));
        }

        public string Run(params string[] args) => Execute(args, true);

        /// <summary>Runs git and does not fail on a non-zero exit (a merge that ends in a conflict).</summary>
        public string RunAllowFailure(params string[] args) => Execute(args, false);

        string Execute(string[] args, bool mustSucceed)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = Dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            Assert.True(!mustSucceed || p.ExitCode == 0,$"git {string.Join(' ', args)}: {error.Result}");
            return output.Result;
        }

        public void Dispose()
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Dir, true);
        }
    }

    sealed class Rig : IAsyncDisposable
    {
        public required PluginHost.PluginHost Host { get; init; }
        public required DocumentApi Docs { get; init; }
        public required Document Doc { get; init; }
        public required string Path { get; init; }
        public required UiApiTests.RecordingUi Ui { get; init; }
        public required DocumentApiTests.RecordingTools Tools { get; init; }
        public required UiApi UiApi { get; init; }
        public long Caret;

        public static async Task<Rig> CreateAsync(string path, long caret = 0, int maxFind = 2, TextCodec? codec = null, int watchMs = 0, int treeEntries = 0)
        {
            var ui = new UiApiTests.RecordingUi();
            var tools = new DocumentApiTests.RecordingTools();
            PluginHost.PluginHost? host = null;
            PluginEventSender send = (plugin, name, payload) => host!.SendEventAsync(plugin, name, payload);
            var permissions = new PluginPermissions();
            permissions.Grant("git", DocumentPermissions.Read, GrantScope.All);
            permissions.Grant("git", DocumentPermissions.Virtual, GrantScope.All);
            permissions.Grant("git", DocumentPermissions.EditDirect, GrantScope.All);
            var docs = new DocumentApi(new DocumentApiOptions
            {
                MaxReadBytes = 1 << 16, MaxFindMatches = maxFind, MaxSnapshotsPerPlugin = 2, SnapshotLifetime = TimeSpan.FromSeconds(10),
                EventQueueCapacity = 64, MaxRememberedBatches = 16, Permissions = permissions, CallLog = new PluginCallLog(100), Proposals = new Proposals(),
                Tools = tools, MaxSymbolsPerDocument = 10, MaxSymbolTextLength = 50, MaxSnippetLength = 100, MaxSnippetCarets = 2, TabTimeout = TimeSpan.FromSeconds(1), MaxSecretScanBytes = 1 << 20, MaxClipboardChars = 1000, MaxScanBytes = 1 << 22, MaxVirtualDocumentsPerPlugin = 4, MaxVirtualBytes = 1 << 20, MaxDiagnosticsPerDocument = 10, MaxCompletionItems = 10, MaxCompletionEdits = 4, CompletionTimeout = TimeSpan.FromSeconds(2),
                DiffActions = new DiffActionOptions(4, 200),
            }, new Owner(), send, (plugin, method, payload, timeout, ct) => host!.CallAsync(plugin, method, payload, timeout, ct));
            var uiApi = new UiApi(new UiApiOptions { MaxPanelsPerPlugin = 2, MaxItemsPerPanel = 100, MaxTreeDepth = 3, MaxTasksPerPlugin = 2, MaxTextLength = 2000, MaxPickItems = 10, TaskCancelGrace = TimeSpan.FromSeconds(30) }, ui, send);
            host = new PluginHost.PluginHost(PluginHostTests.Options(), PluginApiRouter.Create(docs.HandleAsync, ("ui/", uiApi.HandleAsync)), docs);
            var dll = System.IO.Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.Plugins.Git.dll");
            Assert.True(File.Exists(dll), dll);
            string[] args = [dll, "--timeout-ms", "20000", "--max-bytes", "1000000", "--max-entries", "50", "--chunk-bytes", "60000", "--peek-max-lines", "500"];
            if (watchMs > 0) args = [.. args, "--watch-debounce-ms", watchMs.ToString()];
            if (treeEntries > 0) args = [.. args, "--max-tree-entries", treeEntries.ToString()];
            host.Register(new PluginSpec("git", "Git", T.DotnetHost, args, ["onCommand:git"]));
            await host.ActivateAsync("onCommand:git");
            var doc = Document.FromBytes(File.ReadAllBytes(path), PagedBackendFactory.Instance, new DocumentOptions { Format = codec ?? TextCodec.Utf8 });
            var rig = new Rig { Host = host, Docs = docs, Doc = doc, Path = path, Ui = ui, Tools = tools, UiApi = uiApi, Caret = caret };
            docs.Expose(doc, () => SelectionSet.Single(new TextSelection(rig.Caret, rig.Caret)));
            return rig;
        }

        public async Task<string> RunAsync(string command, string? file = null, long? offset = null) =>
            (await RunJsonAsync(command, file, offset)).GetProperty("status").GetString()!;

        /// <summary>The whole answer of a command; <paramref name="offset"/> makes the hunk commands act there instead of at the caret.</summary>
        public async Task<JsonElement> RunJsonAsync(string command, string? file = null, long? offset = null)
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{Doc.Id}\",\"revision\":{Doc.Revision},\"language\":\"en\",\"path\":{JsonSerializer.Serialize(file ?? Path)}{(offset is { } at ? $",\"offset\":{at}" : "")}}}";
            return (await Host.CallAsync("git", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

        readonly List<Document> _opened = [];

        /// <summary>A command with more members of the request (already written as JSON, each starting with a comma), at the revision of the document or at <paramref name="revision"/>.</summary>
        public async Task<JsonElement> RunWithAsync(string command, string extra, long? revision = null)
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{Doc.Id}\",\"revision\":{revision ?? Doc.Revision},\"language\":\"en\",\"path\":{JsonSerializer.Serialize(Path)}{extra}}}";
            return (await Host.CallAsync("git", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

        /// <summary>Another open file, shown to plugins like the first one (of another repository, say).</summary>
        public Document Open(string path)
        {
            var doc = Document.FromBytes(File.ReadAllBytes(path), PagedBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
            Docs.Expose(doc, () => SelectionSet.Single(new TextSelection(0, 0)));
            _opened.Add(doc);
            return doc;
        }

        /// <summary>A command for the file <paramref name="path"/> that <paramref name="doc"/> shows; the answer's status.</summary>
        public async Task<string> RunForAsync(Document doc, string path, string command)
        {
            var json = $"{{\"command\":\"{command}\",\"document\":\"{doc.Id}\",\"revision\":{doc.Revision},\"language\":\"en\",\"path\":{JsonSerializer.Serialize(path)}}}";
            return (await Host.CallAsync("git", "command/execute", Encoding.UTF8.GetBytes(json))).GetProperty("status").GetString()!;
        }

        /// <summary>The state of the files below a folder, as the file tree asks for it: a request without a document.</summary>
        public async Task<JsonElement> TreeStatusAsync(string folder)
        {
            var json = $"{{\"command\":\"{GitPlugin.TreeStatusCommand}\",\"language\":\"en\",\"path\":{JsonSerializer.Serialize(folder)}}}";
            return (await Host.CallAsync("git", "command/execute", Encoding.UTF8.GetBytes(json))).Clone();
        }

        public int StatusCount() { lock (Ui.Statuses) return Ui.Statuses.Count; }

        public byte[] TreeBytes() => Doc.GetSnapshot().ReadBytes(new ByteRange(0, Doc.ByteLength));

        /// <summary>The latest panel and status the plugin sent; read under the lock the recording UI writes under.</summary>
        public PanelModel? LatestPanel() { lock (Ui.Panels) return Ui.Panels.Count == 0 ? null : Ui.Panels[^1]; }

        public StatusModel? LatestStatus() { lock (Ui.Statuses) return Ui.Statuses.Count == 0 ? null : Ui.Statuses[^1]; }

        public string VirtualText(int n) => TextOf(Tools.Virtuals[n].Document);

        public string TreeText() => TextOf(Doc);

        static string TextOf(Document d) => Encoding.UTF8.GetString(d.GetSnapshot().ReadBytes(new ByteRange(0, d.ByteLength)));

        public async ValueTask DisposeAsync()
        {
            await Host.DisposeAsync();
            await Docs.DisposeAsync();
            Doc.Dispose();
            foreach (var doc in _opened) doc.Dispose();
        }
    }

    static string Lines(params string[] lines) => string.Concat(lines.Select(l => l + "\n"));

    static string Numbered(int count, Func<int, string?> change) => Lines([.. Enumerable.Range(1, count).Select(i => change(i) ?? "l" + i)]);

    const string Original = "l1\nl2\nl3\nl4\nl5\nl6\nl7\nl8\nl9\nl10\nl11\nl12\n";

    /// <summary>Lines 2, 6 and 11 changed: three hunks.</summary>
    static string ThreeChanges() => Numbered(12, i => i is 2 or 6 or 11 ? "L" + i : null);

    static long Offset(string text, string line) => Encoding.UTF8.GetByteCount(text[..text.IndexOf(line, StringComparison.Ordinal)]) + 1;

    [Fact]
    public void The_hunk_at_the_caret_is_the_one_whose_range_holds_it()
    {
        HunkSpan[] hunks = [new(0, 3, 6), new(1, 10, 10), new(2, 20, 25)];
        Assert.Equal(0, Hunks.At(hunks, 3, 100)!.Index);
        Assert.Equal(0, Hunks.At(hunks, 5, 100)!.Index);
        Assert.Null(Hunks.At(hunks, 6, 100)); // the first byte after the hunk
        Assert.Null(Hunks.At(hunks, 9, 100));
        Assert.Equal(1, Hunks.At(hunks, 10, 100)!.Index); // lines removed here
        Assert.Null(Hunks.At(hunks, 25, 100));
        Assert.Equal(2, Hunks.At(hunks, 25, 25)!.Index); // the end of the document counts
    }

    [Fact]
    public async Task Status_in_the_panel_is_what_git_status_says_also_after_a_change_from_outside_and_it_does_not_touch_the_index()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("b.txt", "b\n"), ("c.txt", "c\n"), ("sub/d.txt", "d\n"));
        repo.Write("a.txt", "a2\n");                  // changed in the tree
        repo.Write("b.txt", "b2\n"); repo.Run("add", "b.txt"); // staged
        repo.Run("mv", "c.txt", "c2.txt");            // renamed
        repo.Write("new one.txt", "x\n");             // untracked, with a space
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));
        var indexFile = Path.Combine(repo.Dir, ".git", "index");
        var before = File.ReadAllBytes(indexFile);

        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Equal(Expected(repo), Shown(rig));
        Assert.Equal(before, File.ReadAllBytes(indexFile));
        var panel = rig.Ui.Panels[^1];
        Assert.Equal(GitPlugin.StatusPanel, panel.Id);
        Assert.Contains(panel.Items, i => i.Label == "a.txt" && i.Detail == "In tree: modified");
        Assert.Contains(panel.Items, i => i.Label == "b.txt" && i.Detail == "Staged: modified");
        Assert.Contains(panel.Items, i => i.Label == "new one.txt" && i.Detail == "untracked");

        // another program changes the repository: the next run shows it
        repo.Write("sub/d.txt", "d2\n");
        repo.Run("add", "a.txt");
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Equal(Expected(repo), Shown(rig));
        Assert.Contains(rig.Ui.Panels[^1].Items, i => i.Label == "sub/d.txt");
        Assert.Contains(rig.Ui.Panels[^1].Items, i => i.Label == "a.txt" && i.Detail == "Staged: modified");
    }

    static Dictionary<string, string> TreeEntries(JsonElement answer) =>
        answer.GetProperty("entries").EnumerateArray().ToDictionary(e => e[1].GetString()!, e => e[0].GetString()!);

    [Fact]
    public async Task The_state_of_the_files_of_a_folder_is_what_git_status_says_with_untracked_and_ignored_folders_as_one_entry_and_it_needs_no_document()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("b.txt", "b\n"), ("c.txt", "c\n"), ("sub/d.txt", "d\n"), ("sub/e.txt", "e\n"), (".gitignore", "*.log\nbuild/\n"));
        repo.Write("a.txt", "a2\n");                          // changed in the tree
        repo.Write("b.txt", "b2\n"); repo.Run("add", "b.txt"); // staged
        File.Delete(repo.PathOf("c.txt"));                    // deleted in the tree
        repo.Write("new.txt", "x\n");                         // untracked
        repo.Write("fresh/one.txt", "1\n"); repo.Write("fresh/two.txt", "2\n"); // an untracked folder is one entry
        repo.Write("sub/f.txt", "f\n");                       // untracked in a folder Git knows
        repo.Run("mv", "sub/e.txt", "sub/e2.txt");            // renamed
        repo.Write("debug.log", "l\n");                       // ignored file
        repo.Write("build/out.bin", "o\n"); repo.Write("build/deep/x.bin", "x\n"); // ignored folder, one entry
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));
        var indexFile = Path.Combine(repo.Dir, ".git", "index");
        var before = File.ReadAllBytes(indexFile);

        var answer = await rig.TreeStatusAsync(repo.Dir);

        Assert.Equal("tree", answer.GetProperty("status").GetString());
        var entries = TreeEntries(answer);
        Assert.Equal(new Dictionary<string, string>
        {
            ["a.txt"] = "M", ["b.txt"] = "M", ["c.txt"] = "D", ["new.txt"] = "N", ["fresh/"] = "N", ["sub/f.txt"] = "N", ["sub/e2.txt"] = "M", ["debug.log"] = "I", ["build/"] = "I",
        }.OrderBy(e => e.Key), entries.OrderBy(e => e.Key));
        Assert.Equal(entries.Count, answer.GetProperty("count").GetInt32());
        Assert.Equal(before, File.ReadAllBytes(indexFile)); // reading the state writes nothing
        Assert.Empty(rig.Ui.Panels);                          // and shows nothing
        Assert.Empty(rig.Ui.Notices);

        // the paths Git itself names, the ignored ones aside, are the ones the answer has (a folder it names whole is one of them)
        var fields = repo.Run("status", "--porcelain=v1", "-z", "--untracked-files=normal").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var named = new List<string>();
        for (int i = 0; i < fields.Length; i++)
        {
            named.Add(fields[i][3..]);
            if (fields[i][0] is 'R' or 'C') i++; // the old name of a rename follows
        }
        Assert.Equal(named.Order(), entries.Where(e => e.Value != "I").Select(e => e.Key).Order());
    }

    [Fact]
    public async Task The_state_of_a_subfolder_has_paths_relative_to_it_and_only_its_own_entries()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("sub/d.txt", "d\n"), ("sub/deep/g.txt", "g\n"));
        repo.Write("a.txt", "a2\n");
        repo.Write("sub/d.txt", "d2\n");
        repo.Write("sub/deep/new.txt", "n\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));

        Assert.Equal(new Dictionary<string, string> { ["d.txt"] = "M", ["deep/new.txt"] = "N" }.OrderBy(e => e.Key), TreeEntries(await rig.TreeStatusAsync(repo.PathOf("sub"))).OrderBy(e => e.Key));
        Assert.Equal(new Dictionary<string, string> { ["new.txt"] = "N" }, TreeEntries(await rig.TreeStatusAsync(repo.PathOf("sub/deep"))));
    }

    [Fact]
    public async Task A_conflict_is_the_heaviest_state_and_the_entries_over_the_limit_are_the_lightest_ones()
    {
        using var repo = ConflictRepo();
        repo.Write("note.txt", "n\n");
        repo.Write("x.log", "l\n");
        repo.Write(".gitignore", "*.log\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"), treeEntries: 2);

        var entries = TreeEntries(await rig.TreeStatusAsync(repo.Dir));

        Assert.Equal(2, entries.Count);
        Assert.Equal("C", entries["c.txt"]);
        Assert.DoesNotContain("I", entries.Values); // the ignored file is the lightest and goes first
    }

    [Fact]
    public async Task A_folder_outside_a_repository_and_one_that_is_not_there_have_no_state_and_no_notice()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));
        var outside = Path.Combine(Path.GetTempPath(), "textida-nogit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            Assert.Equal("noRepository", (await rig.TreeStatusAsync(outside)).GetProperty("status").GetString());
            Assert.Equal("noRepository", (await rig.TreeStatusAsync(Path.Combine(outside, "missing"))).GetProperty("status").GetString());
            Assert.Equal("noFile", (await rig.TreeStatusAsync("")).GetProperty("status").GetString());
            Assert.Empty(rig.Ui.Notices);
        }
        finally { Directory.Delete(outside, true); }
    }

    [Theory]
    [InlineData(' ', 'M', GitFileState.Modified)]
    [InlineData('M', ' ', GitFileState.Modified)]
    [InlineData('M', 'M', GitFileState.Modified)]
    [InlineData('T', ' ', GitFileState.Modified)]
    [InlineData('R', ' ', GitFileState.Modified)]
    [InlineData('R', 'M', GitFileState.Modified)]
    [InlineData('A', ' ', GitFileState.New)]
    [InlineData('A', 'M', GitFileState.New)]
    [InlineData(' ', 'A', GitFileState.New)]
    [InlineData('C', ' ', GitFileState.New)]
    [InlineData('?', '?', GitFileState.New)]
    [InlineData(' ', 'D', GitFileState.Deleted)]
    [InlineData('D', ' ', GitFileState.Deleted)]
    [InlineData('A', 'D', GitFileState.Deleted)]
    [InlineData('!', '!', GitFileState.Ignored)]
    [InlineData('U', 'U', GitFileState.Conflict)]
    [InlineData('A', 'A', GitFileState.Conflict)]
    [InlineData('D', 'D', GitFileState.Conflict)]
    [InlineData('D', 'U', GitFileState.Conflict)]
    [InlineData('U', 'D', GitFileState.Conflict)]
    public void Two_letters_of_git_status_are_one_of_the_states_of_the_tree(char index, char tree, GitFileState expected) =>
        Assert.Equal(expected, TreeStatus.Classify(new StatusEntry(index, tree, "p", null)));

    static string[] Expected(Repo repo)
    {
        var fields = repo.Run("status", "--porcelain=v1", "-z", "--untracked-files=all").Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>();
        for (int i = 0; i < fields.Length; i++)
        {
            paths.Add(fields[i][3..]);
            if (fields[i][0] is 'R' or 'C') i++; // the old name of a rename follows
        }
        return [.. paths.Order()];
    }

    static string[] Shown(Rig rig) => [.. rig.Ui.Panels[^1].Items.Select(i => i.Label).Order()];

    [Fact]
    public async Task A_clean_tree_and_a_file_outside_a_repository_are_said_so()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Empty(rig.Ui.Panels[^1].Items);
        Assert.Contains((NoticeLevel.Info, "The working tree is clean."), rig.Ui.Notices);

        var outside = Path.Combine(Path.GetTempPath(), "textida-nogit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "x.txt"), "x\n");
            Assert.Equal("noRepository", await rig.RunAsync(GitPlugin.StatusCommand, Path.Combine(outside, "x.txt")));
            Assert.Equal("noFile", await rig.RunAsync(GitPlugin.StatusCommand, ""));
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public async Task The_diff_against_the_index_and_against_head_shows_the_same_hunks_as_git_diff()
    {
        using var repo = new Repo(("f.txt", Original));
        repo.Write("f.txt", ThreeChanges());
        repo.Run("add", "f.txt");                       // the index has all three changes ...
        repo.Write("f.txt", Numbered(12, i => i is 2 or 11 ? "L" + i : i is 6 ? "SIX" : i is 9 ? "NINE" : null)); // ... the tree differs in two places
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), 0, 100);

        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffIndexCommand));
        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffHeadCommand));
        Assert.Equal(repo.Run("show", ":f.txt"), rig.VirtualText(0));
        Assert.Equal(Original, rig.VirtualText(1));
        Assert.Equal(["f.txt (index)", "f.txt (HEAD)"], rig.Tools.Diffs.Select(d => d.Left.Title));
        Assert.Equal(HunkHeaders(repo.Run("diff", "-U0", "--no-color")), await HunksAsync(rig, 0));
        Assert.Equal(HunkHeaders(repo.Run("diff", "-U0", "--no-color", "HEAD")), await HunksAsync(rig, 1));
    }

    /// <summary>The old and the new range of every hunk of a <c>-U0</c> diff, as <c>start,count&gt;start,count</c>.</summary>
    static string[] HunkHeaders(string diff) =>
        [.. diff.Split('\n').Where(l => l.StartsWith("@@ ")).Select(l =>
        {
            var parts = l.Split(' ');
            return Range(parts[1][1..]) + ">" + Range(parts[2][1..]);
        })];

    /// <summary>A git range <c>start[,count]</c> as <c>first line (0 based),count</c>; for a removal git names the line before the hole.</summary>
    static string Range(string text)
    {
        var p = text.Split(',');
        long start = long.Parse(p[0]), count = p.Length > 1 ? long.Parse(p[1]) : 1;
        return $"{(count == 0 ? start : start - 1)},{count}";
    }

    static async Task<string[]> HunksAsync(Rig rig, int virtualDocument)
    {
        var index = rig.Tools.Virtuals[virtualDocument].Document;
        var json = $"{{\"left\":{{\"document\":\"{index.Id}\",\"revision\":{index.Revision}}},\"right\":{{\"document\":\"{rig.Doc.Id}\",\"revision\":{rig.Doc.Revision}}}}}";
        using var parsed = JsonDocument.Parse(json);
        var answer = await rig.Docs.HandleAsync("git", DocumentMethods.DiffCompute, parsed.RootElement, default);
        using var result = JsonDocument.Parse(answer!);
        return [.. result.RootElement.GetProperty("hunks").EnumerateArray().Select(h =>
            $"{h.GetProperty("left").GetProperty("startLine").GetInt64()},{h.GetProperty("left").GetProperty("lines").GetInt64()}>{h.GetProperty("right").GetProperty("startLine").GetInt64()},{h.GetProperty("right").GetProperty("lines").GetInt64()}")];
    }

    [Fact]
    public async Task Staging_one_of_three_changes_puts_only_that_hunk_in_the_index()
    {
        using var repo = new Repo(("f.txt", Original));
        var tree = ThreeChanges();
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), Offset(tree, "L6"));

        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));

        Assert.Equal(Numbered(12, i => i == 6 ? "L6" : null), repo.Run("show", ":f.txt"));
        var cached = repo.Run("diff", "--cached", "-U0", "--no-color");
        Assert.Equal(1, cached.Split('\n').Count(l => l.StartsWith("@@ ")));
        Assert.Contains("-l6\n+L6\n", cached);
        Assert.DoesNotContain("L2", cached);
        Assert.DoesNotContain("L11", cached);
        Assert.Equal(2, repo.Run("diff", "-U0", "--no-color").Split('\n').Count(l => l.StartsWith("@@ ")));
        Assert.Equal(tree, File.ReadAllText(repo.PathOf("f.txt"))); // the working tree file is not touched
        Assert.Contains((NoticeLevel.Info, "Hunk 2 of f.txt is staged."), rig.Ui.Notices);

        // a caret outside every change stages nothing
        rig.Caret = Offset(tree, "l8");
        Assert.Equal("noHunk", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal(1, repo.Run("diff", "--cached", "-U0", "--no-color").Split('\n').Count(l => l.StartsWith("@@ ")));
    }

    [Fact]
    public async Task A_change_of_the_index_by_another_program_between_the_preview_and_the_stage_is_a_conflict_and_writes_nothing()
    {
        using var repo = new Repo(("f.txt", Original));
        var tree = ThreeChanges();
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), Offset(tree, "L11"));
        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffIndexCommand));

        // another program stages the first change
        var other = Numbered(12, i => i == 2 ? "L2" : null);
        var sha = HashObject(repo, other);
        repo.Run("update-index", "--cacheinfo", $"100644,{sha},f.txt");

        Assert.Equal("conflict", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal(sha, repo.Run("rev-parse", ":f.txt").Trim());           // the index is what the other program made it
        Assert.Equal(other, rig.VirtualText(0));                            // and the index document was read again
        Assert.Contains(rig.Ui.Notices, n => n.Level == NoticeLevel.Warning && n.Message.StartsWith("The index of f.txt changed"));

        // after looking again the same stage goes through, on top of the other change
        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal(Numbered(12, i => i is 2 or 11 ? "L" + i : null), repo.Run("show", ":f.txt"));
    }

    static string HashObject(Repo repo, string content)
    {
        var temp = Path.Combine(repo.Dir, "..", "hash-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(temp, content);
        try { return repo.Run("hash-object", "-w", Path.GetFullPath(temp)).Trim(); }
        finally { File.Delete(temp); }
    }

    [Fact]
    public async Task A_new_file_is_added_to_the_index_with_its_staged_hunk_and_a_file_with_glob_characters_is_that_file()
    {
        using var repo = new Repo(("keep.txt", "k\n"));
        repo.Write("n[1].txt", "one\ntwo\n");
        repo.Write("n1.txt", "other\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("n[1].txt"), 1);

        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));

        Assert.Equal("n[1].txt\n", repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal("one\ntwo\n", repo.Run("show", ":n[1].txt"));
    }

    [Fact]
    public async Task Unstaging_one_of_two_staged_hunks_takes_back_only_that_hunk()
    {
        using var repo = new Repo(("f.txt", Original));
        var tree = ThreeChanges();
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), Offset(tree, "L2"));
        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        rig.Caret = Offset(tree, "L6");
        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal(Numbered(12, i => i is 2 or 6 ? "L" + i : null), repo.Run("show", ":f.txt"));

        Assert.Equal("unstaged", await rig.RunAsync(GitPlugin.UnstageHunkCommand));

        Assert.Equal(Numbered(12, i => i == 2 ? "L2" : null), repo.Run("show", ":f.txt"));
        var cached = repo.Run("diff", "--cached", "-U0", "--no-color");
        Assert.Equal(1, cached.Split('\n').Count(l => l.StartsWith("@@ ")));
        Assert.Contains("-l2\n+L2\n", cached);
        Assert.Equal(tree, File.ReadAllText(repo.PathOf("f.txt")));
        Assert.Contains((NoticeLevel.Info, "Hunk 2 of f.txt is removed from the index."), rig.Ui.Notices);

        // a caret in a change that is not staged, and one outside every change, take nothing back
        rig.Caret = Offset(tree, "L11");
        Assert.Equal("noStagedHunk", await rig.RunAsync(GitPlugin.UnstageHunkCommand));
        rig.Caret = Offset(tree, "l8");
        Assert.Equal("noStagedHunk", await rig.RunAsync(GitPlugin.UnstageHunkCommand));
        Assert.Equal(Numbered(12, i => i == 2 ? "L2" : null), repo.Run("show", ":f.txt"));
    }

    [Fact]
    public async Task Unstaging_the_only_hunk_of_a_new_file_takes_the_file_out_of_the_index()
    {
        using var repo = new Repo(("keep.txt", "k\n"));
        repo.Write("new.txt", "one\ntwo\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("new.txt"), 1);
        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal("new.txt\n", repo.Run("diff", "--cached", "--name-only"));

        Assert.Equal("unstaged", await rig.RunAsync(GitPlugin.UnstageHunkCommand));

        Assert.Equal("", repo.Run("diff", "--cached", "--name-only"));
        Assert.Equal("?? new.txt\n", repo.Run("status", "--porcelain=v1"));
    }

    [Fact]
    public void A_caret_is_moved_from_the_tree_to_the_index_by_the_changes_before_it()
    {
        // index -> tree: 2 bytes replaced by 5 at 10..12 (tree 10..15), 4 bytes removed at 30 (tree 33), 3 bytes added at 50 (tree 53..56)
        HunkPair[] hunks = [new(0, 10, 12, 10, 15), new(1, 30, 34, 33, 33), new(2, 50, 50, 53, 56)];
        Assert.Equal(5, Hunks.ToLeft(hunks, 5));
        Assert.Equal(10, Hunks.ToLeft(hunks, 12));   // inside a change: where it started
        Assert.Equal(12, Hunks.ToLeft(hunks, 15));   // after it: 3 bytes less
        Assert.Equal(30, Hunks.ToLeft(hunks, 33));   // where lines were removed
        Assert.Equal(40, Hunks.ToLeft(hunks, 39));   // after both: 3 less and 4 more, one more in all
        Assert.Equal(50, Hunks.ToLeft(hunks, 54));   // inside the added bytes
    }

    [Fact]
    public void Conflict_blocks_are_found_with_the_ranges_of_both_sides()
    {
        var text = "a\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> other\nb\n<<<<<<< HEAD\r\nx\r\n||||||| base\r\ny\r\n=======\r\nz\r\n>>>>>>> other\r\nc\n<<<<<<< open\nq\n=======\n";
        var bytes = Encoding.UTF8.GetBytes(text);
        var blocks = Conflicts.Find(bytes);
        Assert.Equal(2, blocks.Count); // the last one is not closed
        string Slice(long s, long e) => text[(int)s..(int)e];
        Assert.Equal("ours\n", Slice(blocks[0].OursStart, blocks[0].OursEnd));
        Assert.Equal("theirs\n", Slice(blocks[0].TheirsStart, blocks[0].TheirsEnd));
        Assert.Equal("<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> other\n", Slice(blocks[0].Start, blocks[0].End));
        Assert.Equal("x\r\n", Slice(blocks[1].OursStart, blocks[1].OursEnd));
        Assert.Equal("z\r\n", Slice(blocks[1].TheirsStart, blocks[1].TheirsEnd));
        Assert.Same(blocks[0], Conflicts.At(blocks, blocks[0].Start));
        Assert.Same(blocks[0], Conflicts.At(blocks, blocks[0].End - 1));
        Assert.Null(Conflicts.At(blocks, blocks[0].End));
        Assert.Null(Conflicts.At(blocks, 0));
        Assert.Empty(Conflicts.Find("# Title\n=======\ntext\n"u8));
    }

    /// <summary>A repository whose merge stopped on one conflicting line.</summary>
    static Repo ConflictRepo()
    {
        var repo = new Repo(("c.txt", "a\nb\nc\n"));
        var main = repo.Run("branch", "--show-current").Trim();
        repo.Run("checkout", "-q", "-b", "other");
        repo.Write("c.txt", "a\ntheirs\nc\n");
        repo.Run("commit", "-q", "-am", "other");
        repo.Run("checkout", "-q", main);
        repo.Write("c.txt", "a\nours\nc\n");
        repo.Run("commit", "-q", "-am", "ours");
        repo.RunAllowFailure("merge", "other");
        return repo;
    }

    [Theory]
    [InlineData(GitPlugin.ConflictOursCommand, "a\nours\nc\n")]
    [InlineData(GitPlugin.ConflictTheirsCommand, "a\ntheirs\nc\n")]
    [InlineData(GitPlugin.ConflictBothCommand, "a\nours\ntheirs\nc\n")]
    public async Task A_side_of_the_conflict_block_at_the_caret_replaces_the_block(string command, string expected)
    {
        using var repo = ConflictRepo();
        var text = File.ReadAllText(repo.PathOf("c.txt"));
        Assert.Contains("<<<<<<<", text);
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"), Offset(text, "theirs"));

        Assert.Equal("resolved", await rig.RunAsync(command));

        Assert.Equal(expected, rig.TreeText());
        Assert.Equal(text, File.ReadAllText(repo.PathOf("c.txt"))); // the file is saved by the user, not by the plugin

        rig.Caret = 0; // no block there any more
        Assert.Equal("noConflictMarkers", await rig.RunAsync(command));
    }

    [Fact]
    public async Task A_caret_outside_every_conflict_block_changes_nothing()
    {
        using var repo = ConflictRepo();
        var text = File.ReadAllText(repo.PathOf("c.txt"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"), 0);
        Assert.Equal("noConflictMarkers", await rig.RunAsync(GitPlugin.ConflictBothCommand));
        Assert.Equal(text, rig.TreeText());
    }

    [Fact]
    public async Task Mark_resolved_needs_no_markers_and_a_saved_file_and_then_clears_the_conflict_in_the_index()
    {
        using var repo = ConflictRepo();
        Assert.NotEqual("", repo.Run("ls-files", "-u"));
        var text = File.ReadAllText(repo.PathOf("c.txt"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"), Offset(text, "ours"));
        Assert.Equal("markersLeft", await rig.RunAsync(GitPlugin.MarkResolvedCommand));
        Assert.NotEqual("", repo.Run("ls-files", "-u"));

        Assert.Equal("resolved", await rig.RunAsync(GitPlugin.ConflictBothCommand));
        Assert.Equal("unsaved", await rig.RunAsync(GitPlugin.MarkResolvedCommand));
        Assert.NotEqual("", repo.Run("ls-files", "-u"));

        repo.Write("c.txt", rig.TreeText());
        Assert.Equal("markedResolved", await rig.RunAsync(GitPlugin.MarkResolvedCommand));
        Assert.Equal("", repo.Run("ls-files", "-u"));
        Assert.Equal("a\nours\ntheirs\nc\n", repo.Run("show", ":c.txt"));
        Assert.Equal("notInConflict", await rig.RunAsync(GitPlugin.MarkResolvedCommand));
    }

    [Fact]
    public async Task The_status_panel_opens_files_by_full_path_and_the_status_bar_follows_the_repository()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("sub/b.txt", "b\n"));
        var branch = repo.Run("branch", "--show-current").Trim();
        repo.Write("a.txt", "a2\n");
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));

        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        var root = repo.Run("rev-parse", "--show-toplevel").Trim();
        Assert.Contains(rig.Ui.Panels[^1].Items, i => i.Label == "a.txt" && System.IO.Path.GetFullPath(i.Id) == System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "a.txt")));
        Assert.Equal($"{branch} ~1", rig.Ui.Statuses[^1]!.Text);
        Assert.Contains($"branch {branch}", rig.Ui.Statuses[^1]!.Tooltip!);

        repo.Run("add", "a.txt");
        repo.Write("sub/b.txt", "b2\n");
        repo.Write("new.txt", "n\n");
        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffHeadCommand));
        Assert.Equal($"{branch} +1 ~1 ?1", rig.Ui.Statuses[^1]!.Text);
    }

    // ---- the gaps of P8.6: UTF-16 conflicts, conflicts without markers, hunks by offset, the gutter, autocrlf, a repository that changes outside ----

    static string Crlf(string text) => text.Replace("\n", "\r\n");

    static int HunkCount(string diff) => diff.Split('\n').Count(l => l.StartsWith("@@ "));

    static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 20000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            Assert.True(Environment.TickCount64 < deadline, "The condition was not reached in time.");
            await Task.Delay(25);
        }
    }

    [Fact]
    public void Conflict_blocks_are_found_in_text_that_is_two_or_four_bytes_wide()
    {
        var text = "a\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> other\nb\n";
        foreach (var codec in new[] { TextCodec.Utf16LEBom, TextCodec.Utf16BE, TextCodec.Utf32LE })
        {
            byte[] bytes = [.. codec.Bom, .. codec.Encode(text)];
            Assert.Empty(Conflicts.Find(bytes)); // read as one byte per character there is no marker in it
            var block = Assert.Single(Conflicts.Find(bytes, codec));
            string Slice(long start, long end) => codec.GetString(bytes[(int)start..(int)end]);
            Assert.Equal("ours\n", Slice(block.OursStart, block.OursEnd));
            Assert.Equal("theirs\n", Slice(block.TheirsStart, block.TheirsEnd));
            Assert.Equal("<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> other\n", Slice(block.Start, block.End));
        }
    }

    [Theory]
    [InlineData(GitPlugin.ConflictOursCommand, "a\nours\nc\n")]
    [InlineData(GitPlugin.ConflictTheirsCommand, "a\ntheirs\nc\n")]
    [InlineData(GitPlugin.ConflictBothCommand, "a\nours\ntheirs\nc\n")]
    public async Task A_conflict_block_in_a_utf16_file_is_resolved_like_one_in_utf8(string command, string expected)
    {
        using var repo = new Repo(("c.txt", "x\n"));
        var codec = TextCodec.Utf16LEBom;
        var text = "a\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> other\nc\n";
        File.WriteAllBytes(repo.PathOf("c.txt"), [.. codec.Bom, .. codec.Encode(text)]);
        long caret = codec.Bom.Length + 2L * text.IndexOf("theirs", StringComparison.Ordinal);
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"), caret, codec: codec);

        Assert.Equal("resolved", await rig.RunAsync(command));

        Assert.Equal(expected, Encoding.Unicode.GetString(rig.TreeBytes()[codec.Bom.Length..]));
    }

    [Fact]
    public async Task Mark_resolved_sees_the_markers_that_are_left_in_a_utf16_file()
    {
        using var repo = ConflictRepo();
        var codec = TextCodec.Utf16LE;
        File.WriteAllBytes(repo.PathOf("c.txt"), codec.Encode(File.ReadAllText(repo.PathOf("c.txt"))));
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"), 0, codec: codec);

        Assert.Equal("markersLeft", await rig.RunAsync(GitPlugin.MarkResolvedCommand));
        Assert.NotEqual("", repo.Run("ls-files", "-u"));
    }

    /// <summary>A repository whose merge stopped on <c>c.txt</c>, which one side deleted and the other changed.</summary>
    static Repo DeleteConflictRepo(bool deletedByThem)
    {
        var repo = new Repo(("c.txt", "a\nb\nc\n"), ("keep.txt", "k\n"));
        var main = repo.Run("branch", "--show-current").Trim();
        repo.Run("checkout", "-q", "-b", "other");
        if (deletedByThem) repo.Run("rm", "-q", "c.txt"); else repo.Write("c.txt", "a\nthem\nc\n");
        repo.Run("commit", "-q", "-am", "other");
        repo.Run("checkout", "-q", main);
        if (deletedByThem) repo.Write("c.txt", "a\nus\nc\n"); else repo.Run("rm", "-q", "c.txt");
        repo.Run("commit", "-q", "-am", "ours");
        repo.RunAllowFailure("merge", "other");
        return repo;
    }

    [Theory]
    [InlineData(true, "conflict: deleted by them")]
    [InlineData(false, "conflict: deleted by us")]
    public async Task A_file_one_side_deleted_is_shown_as_a_conflict_and_the_block_commands_say_why_they_cannot_help(bool deletedByThem, string detail)
    {
        using var repo = DeleteConflictRepo(deletedByThem);
        Assert.True(File.Exists(repo.PathOf("c.txt")));
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"));

        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Contains(rig.LatestPanel()!.Items, i => i.Label == "c.txt" && i.Detail == detail);
        Assert.Contains("!1", rig.LatestStatus()!.Text);

        Assert.Equal("noMarkers", await rig.RunAsync(GitPlugin.ConflictBothCommand));
        Assert.Contains(rig.Ui.Notices, n => n.Message.Contains(detail["conflict: ".Length..]) && n.Message.Contains("without markers"));
    }

    [Fact]
    public async Task A_file_one_side_deleted_is_kept_by_marking_it_resolved()
    {
        using var repo = DeleteConflictRepo(deletedByThem: true);
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"));

        Assert.Equal("markedResolved", await rig.RunAsync(GitPlugin.MarkResolvedCommand));

        Assert.Equal("", repo.Run("ls-files", "-u"));
        Assert.Equal("a\nus\nc\n", repo.Run("show", ":c.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_conflict_over_a_deleted_file_is_resolved_by_deleting_the_file(bool deletedByThem)
    {
        using var repo = DeleteConflictRepo(deletedByThem);
        await using var rig = await Rig.CreateAsync(repo.PathOf("c.txt"));

        Assert.Equal("conflictDeleted", await rig.RunAsync(GitPlugin.ConflictDeleteCommand));

        Assert.False(File.Exists(repo.PathOf("c.txt")));
        Assert.Equal("", repo.Run("ls-files", "-u"));
        Assert.DoesNotContain("c.txt", repo.Run("ls-files"));
        Assert.Contains((NoticeLevel.Info, "c.txt is deleted and the conflict is resolved."), rig.Ui.Notices);
        Assert.Equal("notInConflict", await rig.RunAsync(GitPlugin.ConflictDeleteCommand));
        Assert.Equal("keep.txt\n", repo.Run("ls-files"));
    }

    /// <summary>A repository whose merge stopped on a file both sides renamed, each to another name: <c>c.txt</c> is deleted by both and has no file in the working tree.</summary>
    static Repo DeletedByBothRepo()
    {
        var repo = new Repo(("c.txt", "a\nb\nc\nd\ne\nf\n"), ("keep.txt", "k\n"));
        var main = repo.Run("branch", "--show-current").Trim();
        repo.Run("checkout", "-q", "-b", "other");
        repo.Run("mv", "c.txt", "b.txt");
        repo.Run("commit", "-q", "-m", "other");
        repo.Run("checkout", "-q", main);
        repo.Run("mv", "c.txt", "a.txt");
        repo.Run("commit", "-q", "-m", "ours");
        repo.RunAllowFailure("merge", "other");
        return repo;
    }

    [Fact]
    public async Task A_file_both_sides_deleted_is_resolved_from_the_status_panel_after_a_question_and_not_without_a_yes()
    {
        using var repo = DeletedByBothRepo();
        Assert.Contains("DD c.txt", repo.Run("status", "--porcelain=v1"));
        Assert.False(File.Exists(repo.PathOf("c.txt")));
        await using var rig = await Rig.CreateAsync(repo.PathOf("keep.txt"));
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        var item = Assert.Single(rig.LatestPanel()!.Items, i => i.Label == "c.txt");
        Assert.Equal("conflict: deleted by both", item.Detail);

        ConfirmRequest? asked = null;
        rig.Ui.Confirm = request => { asked = request; return false; };
        Assert.True(await rig.UiApi.ActivateItemAsync("git", GitPlugin.StatusPanel, item.Id));
        await T.Until(() => asked is not null);
        Assert.Contains("c.txt", asked!.Message);
        await Task.Delay(300);
        Assert.Contains("c.txt", repo.Run("ls-files", "-u")); // declined: nothing was changed

        rig.Ui.Confirm = _ => true;
        Assert.True(await rig.UiApi.ActivateItemAsync("git", GitPlugin.StatusPanel, item.Id));
        await T.Until(() => !repo.Run("ls-files", "-u").Contains("c.txt"));
        await T.Until(() => { lock (rig.Ui.Panels) return rig.Ui.Panels[^1].Items.All(i => i.Label != "c.txt"); });
        lock (rig.Ui.Notices) Assert.Contains((NoticeLevel.Info, "c.txt is deleted and the conflict is resolved."), rig.Ui.Notices);
        Assert.Contains("a.txt", repo.Run("ls-files", "-u")); // the other conflicts of the merge are not touched
        Assert.Contains("keep.txt", repo.Run("ls-files"));
    }

    [Fact]
    public async Task A_missing_file_that_is_no_conflict_is_only_said_to_be_missing_from_the_status_panel()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("gone.txt", "g\n"));
        File.Delete(repo.PathOf("gone.txt"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        var item = Assert.Single(rig.LatestPanel()!.Items, i => i.Label == "gone.txt");
        var asked = false;
        rig.Ui.Confirm = _ => { asked = true; return true; };

        Assert.True(await rig.UiApi.ActivateItemAsync("git", GitPlugin.StatusPanel, item.Id));

        await T.Until(() => { lock (rig.Ui.Notices) return rig.Ui.Notices.Contains((NoticeLevel.Warning, "gone.txt is not in the working tree, so there is nothing to open.")); });
        Assert.False(asked);
        Assert.Contains("gone.txt", repo.Run("ls-files")); // still tracked: the plugin deleted nothing
    }

    [Fact]
    public async Task A_binary_file_added_by_both_sides_is_a_conflict_without_markers()
    {
        using var repo = new Repo(("keep.txt", "k\n"));
        var main = repo.Run("branch", "--show-current").Trim();
        repo.Run("checkout", "-q", "-b", "other");
        File.WriteAllBytes(repo.PathOf("b.bin"), [1, 0, 2, 0, 3]);
        repo.Run("add", "b.bin");
        repo.Run("commit", "-q", "-m", "other");
        repo.Run("checkout", "-q", main);
        File.WriteAllBytes(repo.PathOf("b.bin"), [9, 0, 8, 0, 7]);
        repo.Run("add", "b.bin");
        repo.Run("commit", "-q", "-m", "ours");
        repo.RunAllowFailure("merge", "other");
        Assert.Contains("AA b.bin", repo.Run("status", "--porcelain=v1"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("b.bin"));

        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Contains(rig.LatestPanel()!.Items, i => i.Label == "b.bin" && i.Detail == "conflict: added by both");
        Assert.Equal("noMarkers", await rig.RunAsync(GitPlugin.ConflictOursCommand));
        Assert.Contains(rig.Ui.Notices, n => n.Message.Contains("added by both"));
    }

    [Fact]
    public async Task Staging_at_an_offset_acts_there_and_not_at_the_caret_and_leaves_no_tab_of_its_own()
    {
        using var repo = new Repo(("f.txt", Original));
        var tree = ThreeChanges();
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), 0); // the caret is in no change

        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand, offset: Offset(tree, "L6")));
        Assert.Equal(Numbered(12, i => i == 6 ? "L6" : null), repo.Run("show", ":f.txt"));
        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand, offset: Offset(tree, "L11")));
        Assert.Equal("unstaged", await rig.RunAsync(GitPlugin.UnstageHunkCommand, offset: Offset(tree, "L6")));
        Assert.Equal(Numbered(12, i => i == 11 ? "L11" : null), repo.Run("show", ":f.txt"));

        // the documents of the index and of HEAD that the clicks needed are closed again
        Assert.NotEmpty(rig.Tools.Virtuals);
        Assert.Equal(rig.Tools.Virtuals.Select(v => v.Document.Id).Order(), rig.Tools.VirtualClosed.Order());
    }

    [Fact]
    public async Task A_document_a_comparison_keeps_open_stays_open_when_a_click_stages_into_it()
    {
        using var repo = new Repo(("f.txt", Original));
        var tree = ThreeChanges();
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), 0, 100);
        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffIndexCommand));

        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand, offset: Offset(tree, "L2")));

        Assert.Empty(rig.Tools.VirtualClosed);
        Assert.Equal(Numbered(12, i => i == 2 ? "L2" : null), rig.VirtualText(0));
    }

    [Fact]
    public async Task The_comparison_has_stage_and_unstage_buttons_and_a_key_so_that_opening_it_again_takes_its_place()
    {
        using var repo = new Repo(("f.txt", Original));
        repo.Write("f.txt", ThreeChanges());
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"));

        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffIndexCommand));
        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffHeadCommand));
        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffIndexCommand));

        var (index, head, again) = (rig.Tools.Diffs[0], rig.Tools.Diffs[1], rig.Tools.Diffs[2]);
        Assert.Equal("git", index.PluginId);
        Assert.Equal([("git.stageHunk", "Stage hunk", "git.diffIndex"), ("git.unstageHunk", "Unstage hunk", "git.diffIndex")], index.Actions!.Select(a => (a.Command, a.Title, a.Refresh)));
        Assert.Equal(["git.diffHead", "git.diffHead"], head.Actions!.Select(a => a.Refresh));
        Assert.NotEqual(index.Key, head.Key);
        Assert.Equal(index.Key, again.Key);
    }

    static string Marks(JsonElement answer) =>
        string.Join(" ", answer.GetProperty("hunks").EnumerateArray().Select(h =>
            $"{h.GetProperty("line").GetInt64()}+{h.GetProperty("lines").GetInt64()} {h.GetProperty("kind").GetString()}{(h.GetProperty("staged").GetBoolean() ? " staged" : "")}"));

    [Fact]
    public async Task The_gutter_gets_the_lines_that_differ_from_the_index_and_those_staged_and_the_user_is_told_nothing()
    {
        using var repo = new Repo(("f.txt", Original));
        var tree = ThreeChanges();
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), Offset(tree, "L6"));

        var answer = await rig.RunJsonAsync(GitPlugin.HunksCommand);
        Assert.Equal("hunks", answer.GetProperty("status").GetString());
        Assert.Equal(rig.Doc.Revision, answer.GetProperty("revision").GetInt64());
        Assert.Equal("1+1 modified 5+1 modified 10+1 modified", Marks(answer));
        Assert.Empty(rig.Ui.Notices);
        Assert.Empty(rig.Tools.Virtuals);

        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal("1+1 modified 5+1 modified staged 10+1 modified", Marks(await rig.RunJsonAsync(GitPlugin.HunksCommand)));
    }

    [Fact]
    public async Task The_gutter_has_no_marks_for_a_file_outside_a_repository_an_untracked_file_or_a_file_in_conflict()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        repo.Write("new.txt", "n\n");
        using var conflict = ConflictRepo();
        var outside = Path.Combine(Path.GetTempPath(), "textida-nogit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "x.txt"), "x\n");
            await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));
            foreach (var file in new[] { repo.PathOf("new.txt"), Path.Combine(outside, "x.txt"), conflict.PathOf("c.txt"), "" })
            {
                var answer = await rig.RunJsonAsync(GitPlugin.HunksCommand, file);
                Assert.Equal("none", answer.GetProperty("status").GetString());
                Assert.Equal(0, answer.GetProperty("hunks").GetArrayLength());
            }
            Assert.Empty(rig.Ui.Notices);
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public void Marks_tell_added_changed_and_removed_lines_and_put_the_staged_ones_where_they_stand_in_the_tree()
    {
        static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
        static string Show(List<GutterMark> marks) => string.Join(" ", marks.Select(m => $"{m.Line}+{m.Lines} {m.Kind}{(m.Staged ? " staged" : "")}"));
        var head = Numbered(6, _ => null);
        var staged = Numbered(6, i => i == 3 ? "L3" : null);

        Assert.Equal("", Show(GutterMarks.Compute(B(head), B(head), B(head), TextCodec.Utf8)));
        Assert.Equal("2+1 modified", Show(GutterMarks.Compute(B(staged), B(head), B(head), TextCodec.Utf8)));
        Assert.Equal("2+0 removed", Show(GutterMarks.Compute(B("l1\nl2\nl4\nl5\nl6\n"), B(head), B(head), TextCodec.Utf8)));
        Assert.Equal("6+1 added", Show(GutterMarks.Compute(B(head + "more\n"), B(head), B(head), TextCodec.Utf8)));
        // line 3 is staged; a new line in front of it moves its mark one line down
        Assert.Equal("0+1 added 3+1 modified staged", Show(GutterMarks.Compute(B("new\n" + staged), B(staged), B(head), TextCodec.Utf8)));
        // a different line ending is no change
        Assert.Equal("", Show(GutterMarks.Compute(B(Crlf(head)), B(head), B(head), TextCodec.Utf8)));
        Assert.Equal("2+1 modified", Show(GutterMarks.Compute(B(Crlf(staged)), B(head), B(head), TextCodec.Utf8)));
    }

    /// <summary>The hunks of <c>git diff -U0</c> of the file as (old start, old count, new start, new count), starts as git prints them (one based, a hunk without lines is the line before it).</summary>
    static List<(int OldStart, int OldCount, int NewStart, int NewCount)> DiffHunks(Repo repo, params string[] arguments)
    {
        var hunks = new List<(int, int, int, int)>();
        foreach (var line in repo.Run(["diff", "-U0", "--no-color", .. arguments]).Split('\n'))
        {
            var m = System.Text.RegularExpressions.Regex.Match(line, @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@");
            if (m.Success) hunks.Add((int.Parse(m.Groups[1].Value), m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1, int.Parse(m.Groups[3].Value), m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1));
        }
        return hunks;
    }

    [Fact]
    public async Task The_marks_and_the_old_lines_of_an_added_a_changed_and_a_removed_block_are_those_of_git_diff_and_of_the_index()
    {
        using var repo = new Repo(("f.txt", Original));
        // line 2 is changed, lines 5 and 6 are removed, two lines are added after line 9
        var tree = "l1\nL2\nl3\nl4\nl7\nl8\nl9\nnew a\nnew b\nl10\nl11\nl12\n";
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"));

        var answer = await rig.RunJsonAsync(GitPlugin.HunksCommand);
        var marks = answer.GetProperty("hunks").EnumerateArray().ToList();
        var expected = DiffHunks(repo, "f.txt");
        Assert.Equal(expected.Count, marks.Count);
        var indexLines = repo.Run("show", ":f.txt").Split('\n');
        for (int i = 0; i < marks.Count; i++)
        {
            var (oldStart, oldCount, newStart, newCount) = expected[i];
            var mark = marks[i];
            // git names a hunk without lines by the line before it, the gutter by the line it stands in front of
            Assert.Equal(newCount == 0 ? newStart : newStart - 1, mark.GetProperty("line").GetInt64());
            Assert.Equal(newCount, mark.GetProperty("lines").GetInt64());
            Assert.Equal(oldCount, mark.GetProperty("oldLines").GetInt64());
            Assert.Equal(oldCount == 0 ? oldStart : oldStart - 1, mark.GetProperty("oldStart").GetInt64());
            Assert.Equal("index", mark.GetProperty("base").GetString());

            var peek = await rig.RunWithAsync(GitPlugin.PeekCommand, $",\"line\":{mark.GetProperty("line").GetInt64()},\"base\":\"index\",\"staged\":false");
            Assert.Equal("peek", peek.GetProperty("status").GetString());
            var old = peek.GetProperty("old").EnumerateArray().Select(l => l.GetString()).ToArray();
            Assert.Equal(indexLines.Skip((int)mark.GetProperty("oldStart").GetInt64()).Take(oldCount), old); // exactly the lines of the index
            Assert.Equal(mark.GetProperty("kind").GetString(), peek.GetProperty("kind").GetString());
            Assert.True(peek.GetProperty("endsWithNewline").GetBoolean());
            Assert.False(peek.GetProperty("truncated").GetBoolean());
        }
        Assert.Equal(["modified", "removed", "added"], marks.Select(m => m.GetProperty("kind").GetString()));
        Assert.Empty(rig.Ui.Notices);
    }

    [Fact]
    public async Task The_peek_of_a_staged_change_shows_head_and_the_head_base_compares_the_tree_with_the_last_commit()
    {
        using var repo = new Repo(("f.txt", Original));
        var tree = Numbered(12, i => i is 3 or 8 ? "L" + i : null);
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), Offset(tree, "L3"));
        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand)); // line 3 is in the index, line 8 only in the tree

        Assert.Equal("2+1 modified staged 7+1 modified", Marks(await rig.RunJsonAsync(GitPlugin.HunksCommand)));
        var stagedPeek = await rig.RunWithAsync(GitPlugin.PeekCommand, ",\"line\":2,\"base\":\"index\",\"staged\":true");
        Assert.Equal(["l3"], stagedPeek.GetProperty("old").EnumerateArray().Select(l => l.GetString()));
        Assert.Equal("head", stagedPeek.GetProperty("base").GetString());
        var plainPeek = await rig.RunWithAsync(GitPlugin.PeekCommand, ",\"line\":7,\"base\":\"index\",\"staged\":false");
        Assert.Equal(["l8"], plainPeek.GetProperty("old").EnumerateArray().Select(l => l.GetString()));

        // against HEAD every difference is one mark, drawn as staged where the index has no difference from the tree
        var head = await rig.RunWithAsync(GitPlugin.HunksCommand, ",\"base\":\"head\"");
        Assert.Equal("2+1 modified staged 7+1 modified", Marks(head));
        Assert.All(head.GetProperty("hunks").EnumerateArray(), h => Assert.Equal("head", h.GetProperty("base").GetString()));
        var headPeek = await rig.RunWithAsync(GitPlugin.PeekCommand, ",\"line\":7,\"base\":\"head\",\"staged\":false");
        Assert.Equal(["l8"], headPeek.GetProperty("old").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task The_peek_has_no_answer_for_a_document_that_changed_for_a_line_without_a_change_and_for_a_file_outside_a_repository()
    {
        using var repo = new Repo(("f.txt", Original));
        repo.Write("f.txt", ThreeChanges());
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"));

        Assert.Equal("none", (await rig.RunWithAsync(GitPlugin.PeekCommand, ",\"line\":1,\"base\":\"index\"", rig.Doc.Revision + 5)).GetProperty("status").GetString());
        Assert.Equal("none", (await rig.RunWithAsync(GitPlugin.PeekCommand, ",\"line\":3,\"base\":\"index\"")).GetProperty("status").GetString());
        Assert.Equal("none", (await rig.RunWithAsync(GitPlugin.PeekCommand, ",\"base\":\"index\"")).GetProperty("status").GetString());
        Assert.Empty(rig.Ui.Notices);
        Assert.Equal("peek", (await rig.RunWithAsync(GitPlugin.PeekCommand, ",\"line\":5,\"base\":\"index\"")).GetProperty("status").GetString());
    }

    [Fact]
    public void The_old_lines_of_a_mark_say_whether_the_last_one_ends_with_a_line_break_and_are_cut_at_the_limit()
    {
        static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
        var noBreak = GutterMarks.Compute(B("a\nb\nX"), B("a\nb\nc"), B("a\nb\nc"), TextCodec.Utf8);
        var mark = Assert.Single(noBreak);
        var old = GutterMarks.OldLinesOf(mark, B("a\nb\nc"), TextCodec.Utf8, 100);
        Assert.Equal(["c"], old.Lines);
        Assert.False(old.EndsWithNewline);
        Assert.False(old.Truncated);

        var withBreak = Assert.Single(GutterMarks.Compute(B("a\nX\nc\n"), B("a\nb\nc\n"), B("a\nb\nc\n"), TextCodec.Utf8));
        old = GutterMarks.OldLinesOf(withBreak, B("a\nb\nc\n"), TextCodec.Utf8, 100);
        Assert.Equal(["b"], old.Lines);
        Assert.True(old.EndsWithNewline);
        Assert.False(old.Truncated);

        var many = Assert.Single(GutterMarks.Compute(B("a\nX\nc\n"), B("a\nb1\nb2\nb3\nc\n"), B("a\nb1\nb2\nb3\nc\n"), TextCodec.Utf8));
        old = GutterMarks.OldLinesOf(many, B("a\nb1\nb2\nb3\nc\n"), TextCodec.Utf8, 2);
        Assert.Equal(["b1", "b2"], old.Lines);
        Assert.True(old.EndsWithNewline);
        Assert.True(old.Truncated);
        Assert.Empty(GutterMarks.OldLinesOf(many, B("a\nb1\nb2\nb3\nc\n"), TextCodec.Utf8, 0).Lines);
    }

    [Fact]
    public async Task Blame_names_the_commit_git_blame_names_and_a_line_nobody_committed_is_said_so()
    {
        using var repo = new Repo(("f.txt", Original));
        repo.Write("f.txt", Numbered(12, i => i == 3 ? "second" : null));
        repo.Run("-c", "user.name=Second Author", "-c", "user.email=second@example.invalid", "commit", "-q", "-a", "-m", "second subject");
        var tree = Numbered(12, i => i == 3 ? "second" : i == 9 ? "unsaved" : null);
        repo.Write("f.txt", tree);
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"));

        string Porcelain(int line, string key) => repo.Run("blame", "--porcelain", "-L", $"{line},{line}", "--", "f.txt").Split('\n').First(l => l.StartsWith(key + " ", StringComparison.Ordinal))[(key.Length + 1)..];

        var changed = await rig.RunWithAsync(GitPlugin.BlameCommand, ",\"line\":2");
        Assert.Equal("blame", changed.GetProperty("status").GetString());
        Assert.Equal("Second Author", changed.GetProperty("author").GetString());
        Assert.Equal(Porcelain(3, "author"), changed.GetProperty("author").GetString());
        Assert.Equal("second subject", changed.GetProperty("summary").GetString());
        Assert.Equal(long.Parse(Porcelain(3, "author-time")), changed.GetProperty("time").GetInt64());
        Assert.Equal(repo.Run("rev-parse", "HEAD").Trim(), changed.GetProperty("sha").GetString());

        var first = await rig.RunWithAsync(GitPlugin.BlameCommand, ",\"line\":0");
        Assert.Equal(("Test", "first"), (first.GetProperty("author").GetString(), first.GetProperty("summary").GetString()));

        // the text the editor holds is blamed, saved or not
        var unsaved = await rig.RunWithAsync(GitPlugin.BlameCommand, ",\"line\":8");
        Assert.Equal("uncommitted", unsaved.GetProperty("status").GetString());
        Assert.Equal("none", (await rig.RunWithAsync(GitPlugin.BlameCommand, ",\"line\":500")).GetProperty("status").GetString());
        Assert.Empty(rig.Ui.Notices);
    }

    [Fact]
    public async Task Under_autocrlf_a_tree_with_crlf_shows_the_hunks_of_git_diff_and_a_staged_hunk_is_stored_with_lf()
    {
        using var repo = new Repo(("f.txt", Original)); // the blob has LF
        repo.Run("config", "core.autocrlf", "true");
        var tree = Crlf(ThreeChanges());
        File.WriteAllBytes(repo.PathOf("f.txt"), Encoding.UTF8.GetBytes(tree));
        Assert.Equal(3, HunkCount(repo.Run("diff", "-U0", "--no-color"))); // for git the line endings are no change
        await using var rig = await Rig.CreateAsync(repo.PathOf("f.txt"), Offset(tree, "L6"), 100);
        Assert.Equal("opened", await rig.RunAsync(GitPlugin.DiffIndexCommand));
        Assert.Equal(HunkHeaders(repo.Run("diff", "-U0", "--no-color")), await HunksAsync(rig, 0));

        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));

        var blob = repo.Run("show", ":f.txt");
        Assert.Equal(Numbered(12, i => i == 6 ? "L6" : null), blob); // no CR in it, in the lines of the hunk either
        Assert.Equal(1, HunkCount(repo.Run("diff", "--cached", "-U0", "--no-color")));
        Assert.Equal(2, HunkCount(repo.Run("diff", "-U0", "--no-color")));
        Assert.Equal(blob, rig.VirtualText(0)); // the index document shows what git stored

        rig.Caret = Offset(tree, "L2");
        Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal("unstaged", await rig.RunAsync(GitPlugin.UnstageHunkCommand));
        Assert.Equal(blob, repo.Run("show", ":f.txt"));
    }

    [Fact]
    public async Task Under_autocrlf_a_new_file_with_crlf_is_staged_with_lf_and_without_autocrlf_the_bytes_stay_as_they_are()
    {
        using var repo = new Repo(("keep.txt", "k\n"));
        repo.Run("config", "core.autocrlf", "true");
        File.WriteAllBytes(repo.PathOf("n.txt"), Encoding.UTF8.GetBytes("one\r\ntwo\r\n"));
        await using (var rig = await Rig.CreateAsync(repo.PathOf("n.txt"), 1))
            Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal("one\ntwo\n", repo.Run("show", ":n.txt"));

        repo.Run("config", "core.autocrlf", "false");
        File.WriteAllBytes(repo.PathOf("m.txt"), Encoding.UTF8.GetBytes("one\r\ntwo\r\n"));
        await using (var rig = await Rig.CreateAsync(repo.PathOf("m.txt"), 1))
            Assert.Equal("staged", await rig.RunAsync(GitPlugin.StageHunkCommand));
        Assert.Equal("one\r\ntwo\r\n", repo.Run("show", ":m.txt"));
    }

    [Fact]
    public async Task The_panel_and_the_status_bar_follow_a_change_made_outside_the_editor_without_a_command()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("b.txt", "b\n"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), watchMs: 150);
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Empty(rig.LatestPanel()!.Items);

        repo.Write("b.txt", "b2\n"); // another program edits a file
        await WaitUntilAsync(() => rig.LatestPanel()!.Items.Any(i => i.Label == "b.txt" && i.Detail == "In tree: modified"));
        await WaitUntilAsync(() => rig.LatestStatus()!.Text.EndsWith("~1"));
        Assert.False(rig.LatestPanel()!.Reveal); // the refresh does not pull the panel in front of the user

        repo.Run("add", "b.txt"); // another program stages it: only the index file changes
        await WaitUntilAsync(() => rig.LatestPanel()!.Items.Any(i => i.Label == "b.txt" && i.Detail == "Staged: modified"));
        await WaitUntilAsync(() => rig.LatestStatus()!.Text.EndsWith("+1"));

        repo.Run("checkout", "-q", "-b", "side"); // and switches the branch: only HEAD changes
        await WaitUntilAsync(() => rig.LatestStatus()!.Text.StartsWith("side "));
    }

    [Fact]
    public async Task Every_repository_with_an_open_file_is_watched_and_the_watch_ends_with_its_last_file()
    {
        using var one = new Repo(("a.txt", "a\n"), ("a2.txt", "a2\n"));
        using var two = new Repo(("b.txt", "b\n"));
        await using var rig = await Rig.CreateAsync(one.PathOf("a.txt"), watchMs: 150);
        var inTwo = rig.Open(two.PathOf("b.txt"));
        // the panel ends up on the second repository; before, only the last one asked was watched
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Equal("status", await rig.RunForAsync(inTwo, two.PathOf("b.txt"), GitPlugin.StatusCommand));
        Assert.Equal("Git: " + Path.GetFileName(two.Dir), rig.LatestPanel()!.Title);

        one.Write("a.txt", "x\n");
        one.Write("a2.txt", "y\n");
        await WaitUntilAsync(() => rig.LatestStatus()!.Text.EndsWith("~2")); // the first repository, which is not the one of the panel
        Assert.Equal("Git: " + Path.GetFileName(two.Dir), rig.LatestPanel()!.Title);

        // its only open file closes (twice: a second notice is harmless): nobody looks at the repository any more
        Assert.Equal("closed", await rig.RunAsync(GitPlugin.ClosedCommand));
        Assert.Equal("closed", await rig.RunAsync(GitPlugin.ClosedCommand));
        await Task.Delay(700); // a refresh that was already on its way is over
        int shown = rig.StatusCount();
        one.Write("a.txt", "z\n");
        await Task.Delay(1500);
        Assert.Equal(shown, rig.StatusCount());

        two.Write("b.txt", "b2\n"); // the second one is still watched
        await WaitUntilAsync(() => rig.LatestStatus()!.Text.EndsWith("~1"));
        Assert.Contains(rig.LatestPanel()!.Items, i => i.Label == "b.txt");
    }

    [Fact]
    public async Task A_repository_stays_watched_while_any_of_its_files_is_open()
    {
        using var repo = new Repo(("a.txt", "a\n"), ("b.txt", "b\n"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"), watchMs: 150);
        var second = rig.Open(repo.PathOf("b.txt"));
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        Assert.Equal("hunks", await rig.RunForAsync(second, repo.PathOf("b.txt"), GitPlugin.HunksCommand)); // the look of the gutter keeps the watch as well

        Assert.Equal("closed", await rig.RunAsync(GitPlugin.ClosedCommand)); // one of the two files
        repo.Write("b.txt", "b2\n");
        await WaitUntilAsync(() => rig.LatestStatus()!.Text.EndsWith("~1"));

        Assert.Equal("closed", await rig.RunForAsync(second, repo.PathOf("b.txt"), GitPlugin.ClosedCommand)); // the last one
        await Task.Delay(700); // a refresh that was already on its way is over
        int shown = rig.StatusCount();
        repo.Write("a.txt", "a2\n");
        await Task.Delay(1500);
        Assert.Equal(shown, rig.StatusCount());
    }

    [Fact]
    public async Task Without_the_option_the_repository_is_not_watched()
    {
        using var repo = new Repo(("a.txt", "a\n"));
        await using var rig = await Rig.CreateAsync(repo.PathOf("a.txt"));
        Assert.Equal("status", await rig.RunAsync(GitPlugin.StatusCommand));
        var shown = rig.Ui.Panels.Count;

        repo.Write("new.txt", "n\n");
        await Task.Delay(1500);

        Assert.Equal(shown, rig.Ui.Panels.Count);
    }

    [Fact]
    public async Task A_burst_of_changes_is_one_call_after_the_quiet_time_and_the_git_directory_counts_only_with_its_index()
    {
        var root = Path.Combine(Path.GetTempPath(), "textida-watch-" + Guid.NewGuid().ToString("N"));
        var gitDir = Path.Combine(root, ".git");
        Directory.CreateDirectory(Path.Combine(gitDir, "objects"));
        int calls = 0;
        try
        {
            using var watcher = new RepoWatcher(root, gitDir, 400, () => Interlocked.Increment(ref calls));
            for (int i = 0; i < 8; i++)
            {
                File.WriteAllText(Path.Combine(root, $"f{i}.txt"), "x");
                await Task.Delay(30);
            }
            Assert.Equal(0, Volatile.Read(ref calls)); // each change started the quiet time again
            await WaitUntilAsync(() => Volatile.Read(ref calls) == 1);
            await Task.Delay(1000);
            Assert.Equal(1, Volatile.Read(ref calls));

            File.WriteAllText(Path.Combine(gitDir, "objects", "x"), "x"); // what git itself writes while it works
            File.WriteAllText(Path.Combine(gitDir, "index.lock"), "x");
            File.WriteAllText(Path.Combine(gitDir, "COMMIT_EDITMSG"), "x");
            await Task.Delay(1000);
            Assert.Equal(1, Volatile.Read(ref calls));

            File.WriteAllText(Path.Combine(gitDir, "index"), "x");
            await WaitUntilAsync(() => Volatile.Read(ref calls) == 2);
            File.WriteAllText(Path.Combine(gitDir, "HEAD"), "ref: refs/heads/x");
            await WaitUntilAsync(() => Volatile.Read(ref calls) == 3);
        }
        finally { Directory.Delete(root, true); }
    }
}
