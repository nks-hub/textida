using System.Text;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// Several carets over the bytes of a paged document: the character steps, the plans for typing and deleting, the transaction that applies them
/// (one revision, one undo step) and the search that adds carets, in the encodings the paged backend reads and over the blocks of a mapped file.
/// </summary>
public class ByteCaretsTests
{
    private static readonly TestFormat[] AllFormats = [TestFormat.Utf8, TestFormat.Utf8Bom, TestFormat.Utf16LE, TestFormat.Utf16BE];

    public static IEnumerable<object[]> Names() => AllFormats.Select(f => new object[] { f.Name });

    private static TestFormat Format(string name) => AllFormats.First(f => f.Name == name);

    private static Doc Open(TestFormat format, string text) =>
        Doc.FromBytes(format.Bytes(text), PagedBackendFactory.Instance, new DocumentOptions { Format = format.Format });

    private static string Text(Doc doc, TestFormat format) => format.Text(doc.ReadBytes(new ByteRange(0, doc.ByteLength)));

    /// <summary>Byte offset of a character index of <paramref name="text"/>, behind the BOM.</summary>
    private static long At(TestFormat format, string text, int index) => format.Bom.Length + format.Raw(text[..index]).Length;

    /// <summary>Empty carets at character indexes of <paramref name="text"/>; <paramref name="primary"/> indexes <paramref name="indexes"/>.</summary>
    private static SelectionSet Carets(TestFormat format, string text, int primary, params int[] indexes) =>
        SelectionSet.Create([.. indexes.Select(i => TextSelection.At(At(format, text, i)))], primary);

    private static EditResult Apply(Doc doc, SelectionSet before, List<CaretEdit> edits, out CaretPlan plan, string? key = null)
    {
        plan = ByteCarets.Assemble(before, edits);
        return ByteCarets.Apply(doc, before, plan, key);
    }

    private static void AssertBytes(byte[] expected, Doc doc) =>
        Assert.True(expected.AsSpan().SequenceEqual(doc.ReadBytes(new ByteRange(0, doc.ByteLength))), "the bytes of the document differ from the bytes of the model");

    // ---- the character next to an offset ----

    [Theory]
    [MemberData(nameof(Names))]
    public void Next_and_Previous_step_over_one_character_and_a_CRLF_in_every_encoding(string name)
    {
        var format = Format(name);
        string[] characters = ["a", "é", "😀", "b", "\r\n", "c", "\n", "\r", "日", "d"];
        using var doc = Open(format, string.Concat(characters));
        var t = ByteText.Of(doc);
        var expected = new List<long> { format.Bom.Length };
        foreach (var c in characters) expected.Add(expected[^1] + format.Raw(c).Length);

        var forward = new List<long> { t.Bom };
        for (int i = 0; i < characters.Length; i++) forward.Add(ByteCarets.Next(t, forward[^1]));
        Assert.Equal(expected, forward);
        Assert.Equal(doc.ByteLength, ByteCarets.Next(t, doc.ByteLength));

        var back = new List<long> { doc.ByteLength };
        for (int i = 0; i < characters.Length; i++) back.Add(ByteCarets.Previous(t, back[^1]));
        expected.Reverse();
        Assert.Equal(expected, back);
        Assert.Equal(t.Bom, ByteCarets.Previous(t, t.Bom));
    }

    // ---- typing at every caret ----

    [Theory]
    [MemberData(nameof(Names))]
    public void Typing_at_every_caret_is_one_revision_and_one_undo_step_that_gives_back_all_carets(string name)
    {
        var format = Format(name);
        string text = "žluť\r\n日本語\r\n😀a\r\nz";
        using var doc = Open(format, text);
        // after ž, before 日, before 😀, after a; the second one is primary
        var before = Carets(format, text, 1, 1, 6, 11, 14);
        byte[] typed = format.Raw("Ž✓");
        long revision = doc.Revision;

        var result = Apply(doc, before, ByteCarets.PlanReplace(before, _ => typed), out var plan);

        Assert.True(result.Applied);
        Assert.Equal(revision + 1, doc.Revision);
        Assert.Equal(1, doc.UndoDepth);
        string expected = text[..1] + "Ž✓" + text[1..6] + "Ž✓" + text[6..11] + "Ž✓" + text[11..14] + "Ž✓" + text[14..];
        Assert.Equal(expected, Text(doc, format));
        var after = new[] { 1 + 2, 6 + 2 + 2, 11 + 4 + 2, 14 + 6 + 2 }.Select(i => TextSelection.At(At(format, expected, i))).ToArray();
        Assert.Equal(after, plan.After.Items);
        Assert.Equal(1, plan.After.Primary);

        var undone = doc.Undo()!;
        Assert.Equal(text, Text(doc, format));
        Assert.Equal(ByteCarets.PrimaryLast(before), undone.Selections);
        var redone = doc.Redo()!;
        Assert.Equal(expected, Text(doc, format));
        Assert.Equal(ByteCarets.PrimaryLast(plan.After), redone.Selections);
    }

    [Fact]
    public void Typing_over_selections_replaces_each_of_them_and_edits_with_one_key_share_an_undo_step()
    {
        string text = "one two three";
        using var doc = Open(TestFormat.Utf8, text);
        var sels = SelectionSet.Create([new TextSelection(0, 3), new TextSelection(4, 7), TextSelection.At(13)], 2);
        Assert.True(Apply(doc, sels, ByteCarets.PlanReplace(sels, _ => "é"u8.ToArray()), out var first, "run").Applied);
        Assert.Equal("é é threeé", Text(doc, TestFormat.Utf8));

        Assert.True(Apply(doc, first.After, ByteCarets.PlanReplace(first.After, _ => "x"u8.ToArray()), out _, "run").Applied);
        Assert.Equal("éx éx threeéx", Text(doc, TestFormat.Utf8));
        Assert.Equal(1, doc.UndoDepth); // the key joins the second edit to the first
        doc.Undo();
        Assert.Equal(text, Text(doc, TestFormat.Utf8));
    }

    // ---- deleting at every caret ----

    [Theory]
    [MemberData(nameof(Names))]
    public void Backspace_and_Delete_remove_a_surrogate_pair_and_a_CRLF_whole_and_carets_that_meet_become_one(string name)
    {
        var format = Format(name);
        string text = "a😀\r\nb";
        using var doc = Open(format, text);
        var back = Carets(format, text, 1, 3, 5); // behind the emoji, before b
        Assert.True(Apply(doc, back, ByteCarets.PlanDelete(ByteText.Of(doc), back, backwards: true), out var planBack).Applied);
        Assert.Equal("ab", Text(doc, format));
        Assert.Single(planBack.After.Items);
        Assert.Equal(TextSelection.At(At(format, "ab", 1)), planBack.After.PrimarySelection);
        doc.Undo();
        Assert.Equal(text, Text(doc, format));

        var forward = Carets(format, text, 1, 3, 5); // before the CR, before b
        Assert.True(Apply(doc, forward, ByteCarets.PlanDelete(ByteText.Of(doc), forward, backwards: false), out var planForward).Applied);
        Assert.Equal("a😀", Text(doc, format));
        Assert.Single(planForward.After.Items);

        // at the edges of the text nothing is removed and the caret stays
        using var small = Open(format, "xy");
        var both = Carets(format, "xy", 1, 0, 2);
        long revision = small.Revision;
        Assert.True(Apply(small, both, ByteCarets.PlanDelete(ByteText.Of(small), both, backwards: true), out _).Applied);
        Assert.Equal("x", Text(small, format));
        Assert.True(small.Revision > revision);
        var none = Carets(format, "x", 0, 0);
        revision = small.Revision;
        Assert.True(Apply(small, none, ByteCarets.PlanDelete(ByteText.Of(small), none, backwards: true), out _).Applied);
        Assert.Equal(revision, small.Revision); // nothing to remove: no revision, no undo step
    }

    [Fact]
    public void Cut_removes_the_selected_text_only_and_keeps_the_carets_without_a_selection()
    {
        string text = "alpha beta gamma";
        using var doc = Open(TestFormat.Utf8, text);
        var sels = SelectionSet.Create([new TextSelection(0, 5), TextSelection.At(8), new TextSelection(11, 16)], 2);
        Assert.True(Apply(doc, sels, ByteCarets.PlanDeleteSelected(sels), out var plan).Applied);
        Assert.Equal(" beta ", Text(doc, TestFormat.Utf8));
        Assert.Equal([0L, 3L, 6L], plan.After.Items.Select(s => s.Caret));
    }

    // ---- plans that collide ----

    [Fact]
    public void Edits_that_overlap_an_earlier_one_are_dropped_and_two_insertions_at_one_place_keep_the_first()
    {
        var one = SelectionSet.Create([TextSelection.At(1), TextSelection.At(2)], 1);
        var plan = ByteCarets.Assemble(one, [new CaretEdit(1, 3, [], 0, 0), new CaretEdit(2, 1, [], 0, 1)]);
        Assert.Single(plan.Kept);
        Assert.Single(plan.After.Items);

        var same = ByteCarets.Assemble(one, [new CaretEdit(2, 0, "x"u8.ToArray(), 1, 0), new CaretEdit(2, 0, "y"u8.ToArray(), 1, 1)]);
        Assert.Single(same.Kept);
        Assert.Equal("x"u8.ToArray(), same.Kept[0].Insert);

        // an insertion right behind a deletion is fine
        var touching = ByteCarets.Assemble(one, [new CaretEdit(0, 2, [], 0, 0), new CaretEdit(2, 0, "z"u8.ToArray(), 1, 1)]);
        Assert.Equal(2, touching.Kept.Count);
    }

    [Fact]
    public void The_primary_caret_stays_the_primary_one_after_an_edit_and_a_merge()
    {
        using var doc = Open(TestFormat.Utf8, "a b c d");
        var before = SelectionSet.Create([TextSelection.At(1), TextSelection.At(3), TextSelection.At(5), TextSelection.At(7)], 2);
        Assert.True(Apply(doc, before, ByteCarets.PlanReplace(before, _ => "x"u8.ToArray()), out var plan).Applied);
        Assert.Equal("ax bx cx dx", Text(doc, TestFormat.Utf8));
        Assert.Equal(2, plan.After.Primary);
        Assert.Equal(TextSelection.At(8), plan.After.PrimarySelection);

        // the characters before carets that touch go: the carets meet, and the merged one holds the primary
        var close = SelectionSet.Create([TextSelection.At(2), TextSelection.At(3), TextSelection.At(6)], 1);
        using var doc2 = Open(TestFormat.Utf8, "abcdefg");
        Assert.True(Apply(doc2, close, ByteCarets.PlanDelete(ByteText.Of(doc2), close, backwards: true), out var merged).Applied);
        Assert.Equal("adeg", Text(doc2, TestFormat.Utf8));
        Assert.Equal([1L, 3L], merged.After.Items.Select(s => s.Caret));
        Assert.Equal(0, merged.After.Primary);
    }

    // ---- over the blocks of a mapped file ----

    [Fact]
    public void Carets_at_the_boundaries_of_the_blocks_of_a_mapped_file_edit_exactly_what_a_byte_array_does()
    {
        var sb = new StringBuilder();
        for (int i = 0; sb.Length < 1_500_000; i++) sb.Append(i % 3 == 0 ? $"řádek {i} é日本\r\n" : $"řádek {i} 😀 x\n");
        byte[] original = Encoding.UTF8.GetBytes(sb.ToString());
        using var file = new TempFile(original);
        using var doc = Doc.Open(file.Path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });

        var offsets = new SortedSet<long>();
        foreach (long block in new[] { 128 * 1024L, 1024 * 1024L })
            for (long o = block - 3; o <= block + 3; o++)
                if (doc.IsCharBoundary(o) && !(original[o - 1] == '\r' && original[o] == '\n')) offsets.Add(o);
        offsets.Add(doc.ByteLength);
        Assert.True(offsets.Count > 8);
        var before = SelectionSet.Create([.. offsets.Select(TextSelection.At)], offsets.Count / 2);

        byte[] typed = "Ž😀"u8.ToArray();
        byte[] Model(byte[] insert)
        {
            var bytes = original.ToList();
            foreach (long at in offsets.Reverse()) bytes.InsertRange((int)at, insert);
            return [.. bytes];
        }

        Assert.True(Apply(doc, before, ByteCarets.PlanReplace(before, _ => typed), out var plan).Applied);
        AssertBytes(Model(typed), doc);
        Assert.Equal(offsets.Count, plan.After.Count);

        // Backspace at the carets that typing left removes the emoji (4 bytes) and leaves the Ž
        Assert.True(Apply(doc, plan.After, ByteCarets.PlanDelete(ByteText.Of(doc), plan.After, backwards: true), out _).Applied);
        AssertBytes(Model(typed[..2]), doc);

        while (doc.CanUndo) doc.Undo();
        AssertBytes(original, doc);
    }

    // ---- words and occurrences ----

    [Fact]
    public void TryWordAt_finds_the_word_around_an_offset_in_multibyte_text()
    {
        var format = TestFormat.Utf8;
        string text = "x žluťoučký_kůň, 42";
        using var doc = Open(format, text);
        var t = ByteText.Of(doc);
        int word = text.IndexOf('ž'), end = word + "žluťoučký_kůň".Length;
        foreach (int index in new[] { word, word + 4, end })
        {
            Assert.True(ByteCarets.TryWordAt(t, At(format, text, index), out long s, out long e));
            Assert.Equal((At(format, text, word), At(format, text, end)), (s, e));
        }
        Assert.False(ByteCarets.TryWordAt(t, At(format, text, end + 1), out _, out _)); // between the comma and the space
        Assert.True(ByteCarets.TryWordAt(t, t.Length, out long numberStart, out long numberEnd));
        Assert.Equal((At(format, text, text.Length - 2), t.Length), (numberStart, numberEnd));
    }

    private static List<long> FindAll(Doc doc, string needle, bool wholeWord)
    {
        var hits = new List<long>();
        ByteCarets.Find(ByteText.Of(doc), Encoding.UTF8.GetBytes(needle), 0, doc.ByteLength, wholeWord, hit => { hits.Add(hit); return true; });
        return hits;
    }

    [Fact]
    public void Find_with_whole_word_skips_words_that_contain_the_needle()
    {
        string text = "čfoo foo fooč foo food foo_x foo, (foo)\nfoo";
        using var doc = Open(TestFormat.Utf8, text);
        long Bytes(Match m) => Encoding.UTF8.GetByteCount(text[..m.Index]);
        var whole = Regex.Matches(text, @"(?<![\p{L}\p{N}_])foo(?![\p{L}\p{N}_])").Select(Bytes).ToList();
        Assert.Equal(5, whole.Count);
        Assert.Equal(whole, FindAll(doc, "foo", wholeWord: true));
        Assert.Equal(Regex.Matches(text, "foo").Select(Bytes).ToList(), FindAll(doc, "foo", wholeWord: false));
    }

    [Fact]
    public void Find_reads_in_blocks_without_losing_or_repeating_a_match_at_a_block_boundary()
    {
        // the first byte shifts the matches against the 1 MiB blocks, so one of them crosses the boundary
        using var doc = Open(TestFormat.Utf8, "x" + string.Concat(Enumerable.Repeat("abc ", 400_000)) + "abc");
        var hits = FindAll(doc, "abc ", wholeWord: false);
        Assert.Equal(400_000, hits.Count);
        for (int i = 0; i < hits.Count; i++) Assert.Equal(1 + 4L * i, hits[i]);

        using var overlapping = Open(TestFormat.Utf8, new string('a', 2_000_001));
        Assert.Equal(1_000_000, FindAll(overlapping, "aa", wholeWord: false).Count); // matches do not overlap
    }

    [Fact]
    public void Find_takes_only_matches_that_start_on_a_unit_of_the_encoding_and_stays_in_the_range()
    {
        using var doc = Open(TestFormat.Utf16LE, "ĀȀĀȀ"); // FF FE, then 00 01 00 02 00 01 00 02
        var t = ByteText.Of(doc);
        var hits = new List<long>();
        bool Collect(long hit) { hits.Add(hit); return true; }
        ByteCarets.Find(t, [0x01, 0x00], 0, doc.ByteLength, false, Collect); // appears at odd offsets only
        Assert.Empty(hits);
        ByteCarets.Find(t, [0x00, 0x02], 0, doc.ByteLength, false, Collect);
        Assert.Equal([4L, 8L], hits);
        hits.Clear();
        ByteCarets.Find(t, [0x00, 0x02], 5, 9, false, Collect); // the second match ends at 10: outside the range
        Assert.Empty(hits);
        ByteCarets.Find(t, [0x00, 0x02], 5, 10, false, Collect);
        Assert.Equal([8L], hits);
    }

    [Fact]
    public void Find_can_be_stopped_by_the_callback_and_cancelled()
    {
        using var doc = Open(TestFormat.Utf8, string.Concat(Enumerable.Repeat("ab", 100_000)));
        var hits = new List<long>();
        ByteCarets.Find(ByteText.Of(doc), "ab"u8.ToArray(), 0, doc.ByteLength, false, hit => { hits.Add(hit); return hits.Count < 3; });
        Assert.Equal([0L, 2L, 4L], hits);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => ByteCarets.Find(ByteText.Of(doc), "ab"u8.ToArray(), 0, doc.ByteLength, false, _ => true, cts.Token));
    }
}

/// <summary>A sparse file of 4.5 GiB with small text islands behind the 2 GiB and 4 GiB marks: offsets that no 32 bit number holds.</summary>
public class ByteCaretsHugeTests
{
    private const long Size = 4_500_000_000;
    private const string IslandText = "\nfoo žluť\r\nfoo 日本\nxfoo foo\n";
    private const string WholeFoo = @"(?<![\p{L}\p{N}_])foo(?![\p{L}\p{N}_])";
    private static readonly long[] Starts = [100, (1L << 31) - 30, (1L << 32) - 30, 4_400_000_000];

    private static void CreateSparse(string path, byte[] island)
    {
        File.Create(path).Dispose();
        if (OperatingSystem.IsWindows()) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("fsutil", $"sparse setflag \"{path}\"") { CreateNoWindow = true, UseShellExecute = false })!.WaitForExit();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        fs.SetLength(Size);
        foreach (long start in Starts)
        {
            fs.Position = start;
            fs.Write(island);
        }
    }

    [Fact]
    [Trait("Category", "Large")]
    public void Carets_found_by_search_in_a_multi_GiB_file_edit_and_undo_across_the_2_and_4_GiB_marks()
    {
        using var dir = new TempDir();
        string path = dir.File("huge.txt");
        if (new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path))!).AvailableFreeSpace < 2L << 30) return;
        byte[] island = Encoding.UTF8.GetBytes(IslandText);
        CreateSparse(path, island);
        using var doc = Doc.Open(path, PagedBackendFactory.Instance, new DocumentOptions { Format = TestFormat.Utf8.Format });
        Assert.Equal(Size, doc.ByteLength);

        long[] relative = Regex.Matches(IslandText, WholeFoo).Select(m => (long)Encoding.UTF8.GetByteCount(IslandText[..m.Index])).ToArray();
        Assert.Equal(3, relative.Length);
        var hits = new List<long>();
        ByteCarets.Find(ByteText.Of(doc), "foo"u8.ToArray(), 0, doc.ByteLength, true, hit => { hits.Add(hit); return true; });
        Assert.Equal(Starts.SelectMany(s => relative.Select(r => s + r)).ToList(), hits);

        var before = SelectionSet.Create([.. hits.Select(h => new TextSelection(h, h + 3))], hits.Count - 1);
        byte[] typed = "Ž"u8.ToArray();
        var plan = ByteCarets.Assemble(before, ByteCarets.PlanReplace(before, _ => typed));
        Assert.True(ByteCarets.Apply(doc, before, plan, null).Applied);
        Assert.Equal(Size - hits.Count, doc.ByteLength);
        Assert.Equal(1, doc.UndoDepth);

        byte[] replaced = Encoding.UTF8.GetBytes(Regex.Replace(IslandText, WholeFoo, "Ž"));
        for (int k = 0; k < Starts.Length; k++)
        {
            long now = Starts[k] - 3 * k; // every earlier island lost three bytes
            Assert.Equal(replaced, doc.ReadBytes(new ByteRange(now, now + replaced.Length)));
        }
        Assert.Equal(hits.Count, plan.After.Count);
        Assert.True(plan.After.Items[^1].Caret > 4_000_000_000);

        doc.Undo();
        Assert.Equal(Size, doc.ByteLength);
        foreach (long start in Starts) Assert.Equal(island, doc.ReadBytes(new ByteRange(start, start + island.Length)));
        doc.Redo();
        Assert.Equal(Size - hits.Count, doc.ByteLength);
    }
}
