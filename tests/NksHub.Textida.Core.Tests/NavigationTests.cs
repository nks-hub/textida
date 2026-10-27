using System.Diagnostics;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Files;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class RelatedFilesTests
{
    private static string Make(JournalTempDir dir, params string[] names)
    {
        foreach (var n in names) File.WriteAllText(dir.Combine(n), "x");
        return dir.Path;
    }

    [Theory]
    [InlineData("x.c", "x.h")]
    [InlineData("x.h", "x.c")]
    [InlineData("x.cpp", "x.hpp")]
    [InlineData("x.hpp", "x.cpp")]
    [InlineData("x.go", "x_test.go")]
    [InlineData("x_test.go", "x.go")]
    [InlineData("x.ts", "x.spec.ts")]
    [InlineData("x.axaml", "x.axaml.cs")]
    [InlineData("Foo.cs", "FooTests.cs")]
    public void Every_builtin_rule_finds_the_pair_in_both_directions(string from, string to)
    {
        using var dir = new JournalTempDir();
        Make(dir, from, to);
        Assert.Equal([dir.Combine(to)], RelatedFiles.Find(dir.Combine(from), []));
    }

    [Fact]
    public void A_custom_rule_finds_a_pair_the_builtin_rules_do_not_know()
    {
        using var dir = new JournalTempDir();
        Make(dir, "shader.vert", "shader.frag");
        Assert.Empty(RelatedFiles.Find(dir.Combine("shader.vert"), []));
        var rules = RelatedFiles.ParseRules(".vert|.frag; broken ;|x;.a|.a");
        Assert.Equal([new RelatedRule(".vert", ".frag")], rules);
        Assert.Equal([dir.Combine("shader.frag")], RelatedFiles.Find(dir.Combine("shader.vert"), rules));
    }

    [Fact]
    public void Two_candidates_are_both_listed_and_a_missing_pair_gives_nothing()
    {
        using var dir = new JournalTempDir();
        Make(dir, "a.h", "a.c", "a.cpp", "lonely.c");
        Assert.Equal([dir.Combine("a.c"), dir.Combine("a.cpp")], RelatedFiles.Find(dir.Combine("a.h"), []).Order());
        Assert.Empty(RelatedFiles.Find(dir.Combine("lonely.c"), []));
    }
}

public class PathAtCaretTests
{
    [Theory]
    [InlineData("see src/App/main.cs for it", 8, "src/App/main.cs", null, null)]
    [InlineData("see src/App/main.cs:42 for it", 12, "src/App/main.cs", 42, null)]
    [InlineData("at src/a.cs:7:3)", 6, "src/a.cs", 7, 3)]
    [InlineData("C:\\work\\a.txt", 3, "C:\\work\\a.txt", null, null)]
    [InlineData("C:\\work\\a.txt:12", 3, "C:\\work\\a.txt", 12, null)]
    [InlineData("open \"my docs/a b.txt\" now", 12, "my docs/a b.txt", null, null)]
    [InlineData("a/b.txt.", 3, "a/b.txt", null, null)]
    public void Reads_the_path_around_the_caret_with_an_optional_line_and_column(string text, int column, string path, int? line, int? col)
    {
        var found = PathAtCaret.Find(text, column);
        Assert.Equal(new PathReference(path, line, col), found);
    }

    [Fact]
    public void Blank_text_has_no_path()
    {
        Assert.Null(PathAtCaret.Find("   ", 1));
        Assert.Null(PathAtCaret.Find("", 0));
    }
}

public class PlacesTests
{
    private static Doc Open(string text) => Doc.FromBytes(Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance);

    [Fact]
    public void Edits_in_three_documents_give_three_places_newest_first_and_one_per_line()
    {
        using var a = Open("one\ntwo\n");
        using var b = Open("alpha\nbeta\n");
        using var c = Open("x\ny\n");
        var list = new PlaceList();
        foreach (var d in new[] { a, b, c }) list.Attach(d);
        a.Insert(1, "1"u8.ToArray());
        b.Insert(7, "!"u8.ToArray());
        c.Insert(0, "-"u8.ToArray());
        c.Insert(1, "-"u8.ToArray()); // same line again: replaces, does not add

        Assert.Equal(3, list.Places.Count);
        Assert.Same(c, list.Places[0].Document);
        Assert.Same(b, list.Places[1].Document);
        Assert.Equal(1, list.Places[1].Line);
        Assert.Same(a, list.Places[2].Document);
    }

    [Fact]
    public void A_place_keeps_its_text_when_lines_are_inserted_above_it()
    {
        using var doc = Open("l0\nl1\nl2\nl3\n");
        var list = new PlaceList();
        list.Attach(doc);
        doc.Insert(doc.ByteLength - 3, "X"u8.ToArray()); // inside "l3"
        var place = list.Places[0];
        Assert.Equal(3, place.Line);
        doc.Insert(0, "new1\nnew2\nnew3\n"u8.ToArray());
        place.Refresh();
        Assert.Equal(6, place.Line);
        doc.Undo();
        doc.Undo();
        place.Refresh();
        Assert.Equal(3, place.Line);
    }

    [Fact]
    public void Places_of_a_closed_file_survive_as_file_positions_and_follow_a_rename()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("a.txt");
        File.WriteAllText(file, "a\nb\nc\n");
        var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        var list = new PlaceList();
        list.Attach(doc);
        doc.Insert(4, "!"u8.ToArray());
        var place = list.Places[0];
        list.Detach(doc);
        doc.Dispose();
        Assert.False(place.IsLive);
        Assert.Equal(file, place.Path);
        Assert.Equal(2, place.Line);

        var renamed = dir.Combine("b.txt");
        list.Rename(file, renamed);
        Assert.Equal(renamed, place.Path);
    }

    [Fact]
    public void An_unnamed_document_leaves_no_place_behind_when_it_closes()
    {
        using var doc = Open("text");
        var list = new PlaceList();
        list.Attach(doc);
        doc.Insert(0, "x"u8.ToArray());
        list.Detach(doc);
        Assert.Empty(list.Places);
    }

    [Fact]
    public void Jump_history_goes_back_and_forward_and_a_new_jump_drops_the_forward_part()
    {
        using var doc = Open("0\n1\n2\n3\n4\n");
        Place At(int line) => Place.At(doc, line * 2);
        var jumps = new JumpHistory();
        jumps.Jumped(At(0));
        jumps.Jumped(At(2));
        var back = jumps.Back(At(4));
        Assert.Equal(2, back!.Line);
        back = jumps.Back(At(2));
        Assert.Equal(0, back!.Line);
        Assert.Null(jumps.Back(At(0)));
        Assert.Equal(2, jumps.Forward(At(0))!.Line);
        jumps.Jumped(At(3));
        Assert.Null(jumps.Forward(At(1)));
    }

    [Theory]
    [InlineData("/a/b/c.txt", "/a/b", "/x/y", "/x/y/c.txt")]
    [InlineData("/a/b", "/a/b", "/x/y", "/x/y")]
    [InlineData("/a/bc/c.txt", "/a/b", "/x/y", null)]
    public void Rebase_moves_what_is_inside_a_moved_folder_and_nothing_else(string path, string oldPath, string newPath, string? expected)
    {
        string N(string? p) => p is null ? "" : p.Replace('/', Path.DirectorySeparatorChar);
        var result = PathMove.Rebase(N(path), N(oldPath), N(newPath));
        Assert.Equal(expected is null ? null : N(expected), result);
    }
}

public class FileOperationsTests
{
    [Fact]
    public void Create_duplicate_rename_and_move_do_what_they_say_and_refuse_to_overwrite()
    {
        using var dir = new JournalTempDir();
        string file = FileOperations.CreateFile(dir.Path, "a.txt");
        File.WriteAllText(file, "hello");
        Assert.Equal(FileFailure.AlreadyExists, Assert.Throws<FileOperationException>(() => FileOperations.CreateFile(dir.Path, "a.txt")).Kind);
        Assert.Equal("hello", File.ReadAllText(file));

        string copy = FileOperations.Duplicate(file);
        Assert.Equal(dir.Combine("a copy.txt"), copy);
        Assert.Equal("hello", File.ReadAllText(copy));
        Assert.Equal(dir.Combine("a copy 2.txt"), FileOperations.Duplicate(file));

        string renamed = FileOperations.Rename(file, "b.txt");
        Assert.False(File.Exists(file));
        Assert.Equal("hello", File.ReadAllText(renamed));
        Assert.Equal(FileFailure.AlreadyExists, Assert.Throws<FileOperationException>(() => FileOperations.Rename(renamed, "a copy.txt")).Kind);

        string folder = FileOperations.CreateFolder(dir.Path, "sub");
        string moved = FileOperations.Move(renamed, folder);
        Assert.Equal(Path.Combine(folder, "b.txt"), moved);
        Assert.True(File.Exists(moved));
        string movedFolder = FileOperations.Move(folder, dir.Path, "sub2");
        Assert.True(File.Exists(Path.Combine(movedFolder, "b.txt")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    public void A_name_with_a_separator_or_nothing_is_refused_before_anything_happens(string name)
    {
        using var dir = new JournalTempDir();
        Assert.Equal(FileFailure.InvalidName, Assert.Throws<FileOperationException>(() => FileOperations.CreateFile(dir.Path, name)).Kind);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void A_folder_cannot_be_moved_into_itself()
    {
        using var dir = new JournalTempDir();
        string a = FileOperations.CreateFolder(dir.Path, "a");
        string inner = FileOperations.CreateFolder(a, "inner");
        Assert.Equal(FileFailure.InvalidName, Assert.Throws<FileOperationException>(() => FileOperations.Move(a, inner)).Kind);
        Assert.True(Directory.Exists(inner));
    }

    [Fact]
    public void A_locked_file_is_reported_with_its_reason_and_stays_whole_where_it_was()
    {
        if (!OperatingSystem.IsWindows()) return; // other systems let a rename go through an open handle
        using var dir = new JournalTempDir();
        string file = dir.Combine("locked.txt");
        File.WriteAllText(file, "precious");
        string target = FileOperations.CreateFolder(dir.Path, "target");
        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var failure = Assert.Throws<FileOperationException>(() => FileOperations.Move(file, target));
            Assert.Equal(FileFailure.InUse, failure.Kind);
            Assert.Equal(file, failure.Path);
        }
        Assert.Equal("precious", File.ReadAllText(file));
        Assert.Empty(Directory.GetFileSystemEntries(target));
    }

    [Theory]
    [InlineData(unchecked((int)0x80070020), FileFailure.InUse)]
    [InlineData(unchecked((int)0x80070070), FileFailure.DiskFull)]
    [InlineData(unchecked((int)0x80070005), FileFailure.AccessDenied)]
    [InlineData(28, FileFailure.DiskFull)]
    [InlineData(16, FileFailure.InUse)]
    [InlineData(12345, FileFailure.Other)]
    public void Win32_errors_and_errno_values_become_a_reason(int code, FileFailure expected)
    {
        Assert.Equal(expected, FileOperations.Classify(new IOException("x", code)));
        Assert.Equal(FileFailure.AccessDenied, FileOperations.Classify(new UnauthorizedAccessException()));
    }
}

public class FileTreeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Only_opened_folders_are_read_and_folders_come_before_files()
    {
        using var dir = new JournalTempDir();
        Directory.CreateDirectory(dir.Combine("b"));
        Directory.CreateDirectory(dir.Combine("a"));
        File.WriteAllText(dir.Combine("z.txt"), "");
        File.WriteAllText(Path.Combine(dir.Path, "a", "inner.txt"), "");
        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();
        Assert.Equal(["a", "b", "z.txt"], tree.Rows.Select(r => r.Entry.Name));
        Assert.False(tree.IsLoaded(dir.Combine("a")));

        await tree.ExpandAsync(dir.Combine("a"));
        Assert.Equal(["a", "inner.txt", "b", "z.txt"], tree.Rows.Select(r => r.Entry.Name));
        Assert.Equal([0, 1, 0, 0], tree.Rows.Select(r => r.Depth));
        tree.Collapse(dir.Combine("a"));
        Assert.Equal(3, tree.Rows.Count);
        Assert.True(tree.IsLoaded(dir.Combine("a")));
    }

    [Fact]
    public async Task A_file_made_by_another_program_shows_up_after_the_watcher_reports_it()
    {
        using var dir = new JournalTempDir();
        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();
        var changed = new TaskCompletionSource<IReadOnlyCollection<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        tree.DiskChanged += d => changed.TrySetResult(d);
        tree.Watch();
        File.WriteAllText(dir.Combine("from-elsewhere.txt"), "x");
        var dirs = await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        foreach (var d in dirs.Where(tree.IsLoaded)) await tree.RefreshAsync(d);
        Assert.Contains(tree.Rows, r => r.Entry.Name == "from-elsewhere.txt");
    }

    [Fact]
    public async Task Reveal_opens_every_folder_above_a_path()
    {
        using var dir = new JournalTempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "a", "b"));
        File.WriteAllText(Path.Combine(dir.Path, "a", "b", "deep.txt"), "");
        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();
        Assert.True(await tree.RevealAsync(Path.Combine(dir.Path, "a", "b", "deep.txt")));
        Assert.True(tree.IndexOf(Path.Combine(dir.Path, "a", "b", "deep.txt")) >= 0);
        Assert.False(await tree.RevealAsync(Path.GetTempPath()));
    }

    [Fact]
    [Trait("Category", "Perf")]
    public async Task A_folder_with_a_hundred_thousand_files_opens_after_one_listing()
    {
        using var dir = new JournalTempDir();
        // a first listing pays for JIT and thread pool start-up; that is not what the gate measures
        using (var warm = new FileTree(dir.Path)) await warm.LoadRootAsync();

        string big = dir.Combine("big");
        Directory.CreateDirectory(big);
        Parallel.For(0, 100_000, i => File.WriteAllBytes(Path.Combine(big, $"f{i:D6}.txt"), []));
        File.WriteAllText(dir.Combine("readme.txt"), "");

        // the median of five listings of the root, so one stall of the disk right after 100000 creations does not decide
        var roots = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            using var probe = new FileTree(dir.Path);
            var timer = Stopwatch.StartNew();
            await probe.LoadRootAsync();
            roots.Add(timer.ElapsedMilliseconds);
            Assert.Equal(2, probe.Rows.Count); // the folder is not read until it is opened
        }
        roots.Sort();
        long root = roots[roots.Count / 2];

        using var tree = new FileTree(dir.Path);
        await tree.LoadRootAsync();
        var sw = Stopwatch.StartNew();
        await tree.ExpandAsync(big);
        long opened = sw.ElapsedMilliseconds;
        Assert.Equal(100_002, tree.Rows.Count);
        output.WriteLine($"root folder {string.Join("/", roots)} ms, 100000 files opened in {opened} ms");
        Assert.True(root < 500, $"root listing median {root} ms ({string.Join("/", roots)})");
    }
}

public class MovedDocumentTests
{
    private static readonly Func<string, IBackendFactory?> Backends = JournalFormatTests.ReferenceResolver;

    [Fact]
    public void A_moved_document_keeps_text_undo_and_unsaved_state_and_recovery_finds_it_under_the_new_path()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("old.txt");
        File.WriteAllText(file, "one\ntwo\n");
        using var session = JournalSession.Start(dir.Combine("journal"), Backends, new JournalOptions { Fsync = FsyncPolicy.Manual });
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        doc.Insert(0, "X"u8.ToArray());
        var moved = dir.Combine("new.txt");
        File.Move(file, moved);
        int undoDepth = doc.UndoDepth;
        var moves = 0;
        doc.Moved += (_, _) => moves++;
        doc.MoveTo(moved);

        Assert.Equal(moved, doc.FilePath);
        Assert.Equal(1, moves);
        Assert.True(doc.IsModified);
        Assert.Equal(undoDepth, doc.UndoDepth);
        Assert.Null(doc.CheckExternalChange());
        doc.Insert(1, "Y"u8.ToArray());

        session.Flush();
        var back = Assert.Single(JournalRecovery.Recover(session.JournalPath!, Backends).Documents);
        Assert.Equal(RecoveryStatus.Restored, back.Status);
        Assert.Equal(moved, back.Path);
        using var restored = back.Document!;
        Assert.Equal(moved, restored.FilePath);
        Assert.Equal("XYone\ntwo\n", Encoding.UTF8.GetString(restored.ReadBytes(new ByteRange(0, restored.ByteLength))));
        Assert.True(restored.IsModified);
    }

    [Fact]
    public void A_move_before_a_save_and_a_save_after_it_recover_from_the_saved_file()
    {
        using var dir = new JournalTempDir();
        var file = dir.Combine("a.txt");
        File.WriteAllText(file, "text");
        using var session = JournalSession.Start(dir.Combine("journal"), Backends, new JournalOptions { Fsync = FsyncPolicy.Manual });
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        session.Track(doc);
        var moved = dir.Combine("b.txt");
        File.Move(file, moved);
        doc.MoveTo(moved);
        doc.Insert(0, "!"u8.ToArray());
        doc.Save();
        doc.Insert(0, "?"u8.ToArray());

        session.Flush();
        var back = Assert.Single(JournalRecovery.Recover(session.JournalPath!, Backends).Documents);
        Assert.Equal(moved, back.Path);
        using var restored = back.Document!;
        Assert.Equal("?!text", Encoding.UTF8.GetString(restored.ReadBytes(new ByteRange(0, restored.ByteLength))));
    }
}
