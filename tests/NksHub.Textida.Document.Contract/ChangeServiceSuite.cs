using System.Text;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Documents;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.Contract;

public abstract partial class DocumentContractSuite
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);
    private const string Digits = "0123456789abcdef";

    // ---- change log: P1.2.4 ----

    [Fact]
    public void RangesMapForwardThroughTheChangeLog()
    {
        using var d = New(B(Digits));
        d.Insert(2, B("XX"));
        d.Delete(new(10, 12));
        Assert.Equal(new ByteRange(7, 10), d.MapRange(new(5, 8), 0).Range);
        Assert.Equal(new ByteRange(12, 14), d.MapRange(new(12, 14), 0).Range);
        Assert.Equal(new ByteRange(4, 6), d.MapRange(new(2, 4), 0).Range);
        Assert.Equal(new ByteRange(0, 2), d.MapRange(new(0, 2), 0).Range);
        Assert.Equal(new ByteRange(5, 5), d.MapRange(new(3, 3), 0).Range);
        Assert.Equal(new ByteRange(1, 2), d.MapRange(new(1, 2), 2).Range);
        Assert.True(d.MapRange(new(3, 5), 2).Ok);
    }

    [Fact]
    public void PointsMapForwardAndSayWhenTheirTextIsGone()
    {
        using var d = New(B(Digits));
        d.Insert(2, B("XX"));
        d.Delete(new(10, 12));
        PointMapResult Map(long offset, AnchorGravity gravity = AnchorGravity.Left, long from = 0) => d.MapPoint(offset, gravity, from);
        Assert.Equal(0, Map(0).Offset);
        Assert.Equal(2, Map(2).Offset);
        Assert.Equal(4, Map(2, AnchorGravity.Right).Offset);
        Assert.Equal(5, Map(3).Offset);
        Assert.Equal(10, Map(8).Offset);
        Assert.Equal(10, Map(10).Offset);
        Assert.Equal(16, Map(16).Offset);
        var gone = Map(9);
        Assert.False(gone.Ok);
        Assert.Equal(ConflictReason.Overlap, gone.Conflict);
        Assert.Equal(new PointMapResult(true, 3, ConflictReason.None), Map(3, from: 2));
    }

    [Fact]
    public void APointBeyondTheLogOrTheFutureIsRejected()
    {
        using var d = New(B(Digits), logCapacity: 2);
        d.Insert(0, B("a"));
        d.Insert(0, B("b"));
        d.Insert(0, B("c"));
        Assert.Equal(ConflictReason.LogTruncated, d.MapPoint(5, AnchorGravity.Left, 0).Conflict);
        Assert.Equal(7, d.MapPoint(5, AnchorGravity.Left, 1).Offset);
        Assert.Equal(0, d.MapPoint(0, AnchorGravity.Left, 1).Offset);
        Assert.Equal(2, d.MapPoint(0, AnchorGravity.Right, 1).Offset);
        Assert.Equal(5, d.MapPoint(5, AnchorGravity.Left, 3).Offset);
        Assert.Equal(ConflictReason.FutureRevision, d.MapPoint(5, AnchorGravity.Left, 4).Conflict);
    }

    [Fact]
    public void OverlapWithAChangeIsAConflictNotAGuess()
    {
        using var d = New(B(Digits));
        d.Insert(2, B("XX"));
        d.Delete(new(10, 12));
        foreach (var range in new ByteRange[] { new(9, 13), new(1, 3), new(2, 2), new(8, 11) })
        {
            var r = d.MapRange(range, 0);
            Assert.False(r.Ok, range.ToString());
            Assert.Equal(ConflictReason.Overlap, r.Conflict);
        }
        Assert.Equal(new ByteRange(11, 11), d.MapRange(new(11, 11), 0).Range);
        Assert.Equal(new ByteRange(10, 10), d.MapRange(new(10, 10), 0).Range);
    }

    [Fact]
    public void ARangeBeyondTheLogOrTheFutureIsRejected()
    {
        using var d = New(B(Digits), logCapacity: 2);
        d.Insert(0, B("a"));
        d.Insert(0, B("b"));
        d.Insert(0, B("c"));
        Assert.Equal(1, d.OldestMappableRevision);
        Assert.Equal(ConflictReason.LogTruncated, d.MapRange(new(5, 6), 0).Conflict);
        Assert.True(d.MapRange(new(5, 6), 1).Ok);
        Assert.Equal(ConflictReason.FutureRevision, d.MapRange(new(5, 6), 4).Conflict);
        var r = d.Replace(new(5, 6), B("Q"), new EditOptions { BaseRevision = 0 });
        Assert.False(r.Applied);
        Assert.Equal(ConflictReason.LogTruncated, r.Conflict);
        Assert.Equal(3, d.Revision);
    }

    [Fact]
    public void AnEditBasedOnAnOlderRevisionLandsWhereTheTextMoved()
    {
        using var d = New(B(Digits));
        d.Insert(0, B("ZZ"));
        var r = d.Replace(new(4, 6), B("!!"), new EditOptions { BaseRevision = 0, Source = "search" });
        Assert.True(r.Applied);
        Assert.Equal(B("ZZ0123!!6789abcdef"), Bytes(d));
        Assert.Equal(2, d.Revision);
    }

    [Fact]
    public void AnEditOverlappingTheUsersChangeIsRejectedAndNothingIsApplied()
    {
        using var d = New(B(Digits));
        d.Insert(0, B("ZZ"));
        d.Delete(new(8, 10));
        var before = Bytes(d);
        var r = d.Apply([ByteEdit.InsertAt(0, B("ok")), new ByteEdit(new(7, 9), B("no"))], new EditOptions { BaseRevision = 0 });
        Assert.False(r.Applied);
        Assert.Equal(ConflictReason.Overlap, r.Conflict);
        Assert.Equal(before, Bytes(d));
        Assert.Equal(2, d.Revision);
    }

    [Fact]
    public void TwoSourcesOnTheSameRevisionConflictOnlyWhenTheyTouchTheSameBytes()
    {
        using var d = New(B(Digits));
        var a = d.Replace(new(4, 6), B("AA"), new EditOptions { BaseRevision = 0, Source = "recipe" });
        var b = d.Replace(new(5, 7), B("BB"), new EditOptions { BaseRevision = 0, Source = "mcp" });
        var c = d.Replace(new(8, 10), B("CC"), new EditOptions { BaseRevision = 0, Source = "mcp" });
        Assert.True(a.Applied);
        Assert.False(b.Applied);
        Assert.Equal(ConflictReason.Overlap, b.Conflict);
        Assert.True(c.Applied);
        Assert.Equal(B("0123AA67CCabcdef"), Bytes(d));
    }

    [Fact]
    public void EditsOfOneTransactionMustNotOverlap()
    {
        using var d = New(B(Digits));
        Assert.Equal(ConflictReason.EditsOverlap, d.Apply([new ByteEdit(new(2, 5), B("a")), new ByteEdit(new(4, 6), B("b"))]).Conflict);
        Assert.Equal(ConflictReason.EditsOverlap, d.Apply([ByteEdit.InsertAt(3, B("a")), ByteEdit.InsertAt(3, B("b"))]).Conflict);
        Assert.True(d.Apply([ByteEdit.InsertAt(3, B("a")), new ByteEdit(new(3, 5), B("b")), ByteEdit.InsertAt(5, B("c"))]).Applied);
        Assert.Equal(B("012abc56789abcdef"), Bytes(d));
        Assert.Equal(1, d.Revision);
    }

    // ---- anchors: P1.2.5 ----

    public static TheoryData<string, int, int, string, int, int> AnchorTable => new()
    {
        // edit, start, end, insert, expected left-gravity offset, expected right-gravity offset (-1 = invalid)
        { "insert at the anchor", 3, 3, "XY", 3, 5 },
        { "insert before", 2, 2, "XY", 5, 5 },
        { "insert after", 4, 4, "XY", 3, 3 },
        { "delete starting at the anchor", 3, 5, "", 3, 3 },
        { "delete ending at the anchor", 1, 3, "", 1, 1 },
        { "delete around the anchor", 2, 4, "", -1, -1 },
        { "delete everything", 0, 6, "", -1, -1 },
        { "replace starting at the anchor", 3, 5, "XYZ", 3, 3 },
        { "replace ending at the anchor", 1, 3, "XYZ", 4, 4 },
        { "replace around the anchor", 2, 4, "XYZ", -1, -1 },
        { "replace the whole file", 0, 6, "Q", -1, -1 },
    };

    [Theory]
    [MemberData(nameof(AnchorTable))]
    public void AnchorsFollowASingleEdit(string name, int start, int end, string insert, int left, int right)
    {
        using var d = New(B("abcdef"));
        var l = d.CreateAnchor(3, AnchorGravity.Left);
        var r = d.CreateAnchor(3, AnchorGravity.Right);
        Assert.True(d.Replace(new(start, end), B(insert)).Applied, name);
        Assert.Equal(left >= 0, l.IsValid);
        Assert.Equal(right >= 0, r.IsValid);
        if (left >= 0) Assert.Equal(left, l.Offset);
        if (right >= 0) Assert.Equal(right, r.Offset);
    }

    [Fact]
    public void AnchorsAtTheEdgesOfAWholeFileReplacement()
    {
        using var d = New(B("abcdef"));
        var start = d.CreateAnchor(0);
        var mid = d.CreateAnchor(3);
        var end = d.CreateAnchor(6);
        d.Replace(new(0, 6), B("new"));
        Assert.Equal((true, 0L), (start.IsValid, start.Offset));
        Assert.False(mid.IsValid);
        Assert.Equal((true, 3L), (end.IsValid, end.Offset));
    }

    [Fact]
    public void AnchorsMoveBackWithUndoAndRedoButDeletedOnesStayInvalid()
    {
        using var d = New(B("abcdef"));
        var a = d.CreateAnchor(3);
        var inside = d.CreateAnchor(3, AnchorGravity.Right);
        d.Insert(1, B("QQ"));
        Assert.Equal(5, a.Offset);
        d.Undo();
        Assert.Equal(3, a.Offset);
        d.Redo();
        Assert.Equal(5, a.Offset);
        d.Undo();
        d.Delete(new(2, 4));
        Assert.False(a.IsValid);
        Assert.False(inside.IsValid);
        d.Undo();
        Assert.False(a.IsValid);
        Assert.Equal(B("abcdef"), Bytes(d));
    }

    [Fact]
    public void AnchorsCreatedAtAnOlderRevisionAreMappedOrInvalid()
    {
        using var d = New(B("abcdef"), logCapacity: 3);
        d.Insert(0, B("12"));
        d.Delete(new(4, 7));
        var moved = d.CreateAnchor(6, AnchorGravity.Left, atRevision: 0);
        Assert.True(moved.IsValid);
        Assert.Equal(5, moved.Offset);
        var gone = d.CreateAnchor(3, AnchorGravity.Left, atRevision: 0);
        Assert.False(gone.IsValid);
        d.Insert(0, B("x"));
        d.Insert(0, B("y"));
        Assert.False(d.CreateAnchor(1, AnchorGravity.Left, atRevision: 0).IsValid);
        Assert.False(d.CreateAnchor(1, AnchorGravity.Left, atRevision: 99).IsValid);
        d.Insert(0, B("z"));
        Assert.True(d.CreateAnchor(1, AnchorGravity.Left, atRevision: d.Revision - 1).IsValid);
    }

    [Fact]
    public void AMultiRangeEditMovesAnchorsByAllEditsBeforeThem()
    {
        using var d = New(B("abcdefgh"));
        var a = d.CreateAnchor(3);
        var b = d.CreateAnchor(6);
        d.Apply([ByteEdit.InsertAt(1, B("X")), new ByteEdit(new(4, 5), B("")), ByteEdit.InsertAt(7, B("YY"))]);
        Assert.Equal(4, a.Offset);
        Assert.Equal(6, b.Offset);
    }

    [Theory]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    public void RandomAnchorsMatchATokenOracle(int seed)
    {
        var r = new Random(seed);
        using var d = New(B(new string('a', 40)));
        var oracle = new TokenOracle(40);
        var anchors = new List<(Anchor Real, TokenOracle.Gap Gap)>();
        var spansUndo = new List<List<(int A, int D, int I)>>();
        var spansRedo = new List<List<(int A, int D, int I)>>();
        for (int step = 0; step < 500; step++)
        {
            int op = r.Next(12);
            if (op < 2 || anchors.Count == 0)
            {
                int pos = r.Next(oracle.Length + 1);
                var g = r.Next(2) == 0 ? AnchorGravity.Left : AnchorGravity.Right;
                anchors.Add((d.CreateAnchor(pos, g), oracle.Track(pos, g)));
            }
            else if (op < 9)
            {
                var edits = new List<(int A, int D, int I)>();
                int at = 0;
                for (int n = r.Next(1, 4); n > 0 && at <= oracle.Length; n--)
                {
                    int a = at + r.Next(0, Math.Max(1, oracle.Length - at + 1));
                    if (a > oracle.Length) break;
                    int len = r.Next(3) == 0 ? 0 : r.Next(0, Math.Min(4, oracle.Length - a) + 1);
                    int ins = r.Next(3) == 0 ? 0 : r.Next(1, 4);
                    if (len == 0 && ins == 0) ins = 1;
                    edits.Add((a, len, ins));
                    at = a + len + 1;
                }
                var result = d.Apply([.. edits.Select(e => new ByteEdit(new(e.A, e.A + e.D), B(new string('b', e.I))))]);
                Assert.True(result.Applied);
                oracle.Apply(edits);
                spansUndo.Add(edits);
                spansRedo.Clear();
            }
            else if (op < 11 && spansUndo.Count > 0)
            {
                var edits = spansUndo[^1];
                spansUndo.RemoveAt(spansUndo.Count - 1);
                spansRedo.Add(edits);
                Assert.NotNull(d.Undo());
                long shift = 0;
                var inverse = new List<(int, int, int)>();
                foreach (var (a, dl, il) in edits)
                {
                    inverse.Add((a + (int)shift, il, dl));
                    shift += il - dl;
                }
                oracle.Apply(inverse);
            }
            else if (spansRedo.Count > 0)
            {
                var edits = spansRedo[^1];
                spansRedo.RemoveAt(spansRedo.Count - 1);
                spansUndo.Add(edits);
                Assert.NotNull(d.Redo());
                oracle.Apply(edits);
            }
            Assert.Equal(oracle.Length, d.ByteLength);
            foreach (var (real, mark) in anchors)
            {
                var expected = oracle.Position(mark);
                Assert.Equal(expected is not null, real.IsValid);
                if (expected is { } p) Assert.Equal(p, real.Offset);
            }
        }
    }

    /// <summary>Independent anchor model: every byte has a token; an anchor is the gap between two tokens and is found again by them.</summary>
    private sealed class TokenOracle
    {
        public sealed class Gap(int left, int right, AnchorGravity gravity)
        {
            public int Left = left, Right = right;
            public AnchorGravity Gravity = gravity;
            public bool Valid = true;
        }

        private List<int> _tokens;
        private int _next;

        public TokenOracle(int length)
        {
            _tokens = [.. Enumerable.Range(0, length)];
            _next = length;
        }

        public int Length => _tokens.Count;

        public Gap Track(int pos, AnchorGravity g)
        {
            var m = new Gap(pos == 0 ? -1 : _tokens[pos - 1], pos == _tokens.Count ? -2 : _tokens[pos], g);
            _marks.Add(m);
            _positions[m] = pos;
            return m;
        }

        private readonly List<Gap> _marks = [];

        public int? Position(Gap m) => m.Valid ? _positions[m] : null;
        private readonly Dictionary<Gap, int> _positions = [];

        public void Apply(IEnumerable<(int A, int D, int I)> edits)
        {
            var next = new List<int>();
            int at = 0;
            foreach (var (a, d, i) in edits)
            {
                next.AddRange(_tokens.GetRange(at, a - at));
                for (int k = 0; k < i; k++) next.Add(_next++);
                at = a + d;
            }
            next.AddRange(_tokens.GetRange(at, _tokens.Count - at));
            _tokens = next;
            var index = new Dictionary<int, int>();
            for (int k = 0; k < next.Count; k++) index[next[k]] = k;
            foreach (var m in _marks.Where(m => m.Valid))
            {
                bool l = m.Left == -1 || index.ContainsKey(m.Left), rr = m.Right == -2 || index.ContainsKey(m.Right);
                int iL = m.Left == -1 ? 0 : l ? index[m.Left] + 1 : -1;
                int iR = m.Right == -2 ? next.Count : rr ? index[m.Right] : -1;
                if (!l && !rr) m.Valid = false;
                else if (l && rr) _positions[m] = iL == iR || m.Gravity == AnchorGravity.Left ? iL : iR;
                else _positions[m] = l ? iL : iR;
            }
            foreach (var m in _marks.Where(m => m.Valid))
            {
                // re-anchor on the neighbours of the new position so later edits compare against current tokens
                int p = _positions[m];
                m.Left = p == 0 ? -1 : next[p - 1];
                m.Right = p == next.Count ? -2 : next[p];
            }
        }
    }

    // ---- undo and redo: P1.2.6 ----

    private static string TempPath(TempDir dir, string name = "doc.txt") => dir.File(name);

    [Fact]
    public void UndoAfterASaveReturnsExactlyTheSavedStateAndClearsTheMarker()
    {
        using var dir = new TempDir();
        using var d = New(B("abc"));
        Assert.True(d.IsModified);
        Assert.True(d.Save(TempPath(dir)).Saved);
        Assert.False(d.IsModified);
        var saved = Bytes(d);
        d.Insert(3, B("d"));
        d.Insert(4, B("e"));
        Assert.True(d.IsModified);
        d.Undo();
        Assert.True(d.IsModified);
        d.Undo();
        Assert.False(d.IsModified);
        Assert.Equal(saved, Bytes(d));
        d.Undo();
        Assert.Null(d.Undo());
        d.Redo();
        Assert.True(d.IsModified);
        Assert.Equal(saved, File.ReadAllBytes(TempPath(dir)));
    }

    [Fact]
    public void ASavedStateThatNoEditCanReachAgainStaysModified()
    {
        using var dir = new TempDir();
        using var d = New(B("abc"));
        d.Insert(3, B("d"));
        d.Save(TempPath(dir));
        d.Undo();
        Assert.True(d.IsModified);
        d.Insert(0, B("x"));
        d.Undo();
        Assert.Equal(B("abc"), Bytes(d));
        Assert.True(d.IsModified);
    }

    [Fact]
    public void CoalescedTypingIsOneUndoStepUnlessASaveIntervenes()
    {
        using var dir = new TempDir();
        using var d = New(B(""));
        var typing = new EditOptions { CoalesceKey = "typing" };
        d.Insert(0, B("a"), typing);
        d.Insert(1, B("b"), typing);
        d.Insert(2, B("c"), typing);
        d.Insert(3, B("!"));
        d.Undo();
        Assert.Equal(B("abc"), Bytes(d));
        d.Undo();
        Assert.Equal(B(""), Bytes(d));
        Assert.False(d.CanUndo);
        d.Redo();
        Assert.Equal(B("abc"), Bytes(d));

        d.Save(TempPath(dir));
        d.Insert(3, B("d"), typing);
        d.Insert(4, B("e"), typing);
        d.Undo();
        Assert.Equal(B("abc"), Bytes(d));
        Assert.False(d.IsModified);
    }

    [Fact]
    public void AGroupUndoesAsOneStep()
    {
        using var d = New(B("0123456789"));
        using (d.BeginUndoGroup())
        {
            d.Insert(0, B("A"));
            using (d.BeginUndoGroup()) d.Delete(new(5, 7));
            d.Insert(2, B("BB"));
        }
        d.Insert(0, B("Z"));
        Assert.Equal(4, d.Revision);
        d.Undo();
        d.Undo();
        Assert.Equal(B("0123456789"), Bytes(d));
        Assert.False(d.CanUndo);
        Assert.Equal(8, d.Revision);
        d.Redo();
        Assert.Equal(B("A0BB1236789"), Bytes(d));
        d.Redo();
        Assert.Equal(B("ZA0BB1236789"), Bytes(d));
    }

    [Fact]
    public void TypingIntoSeveralCursorsIsOneRevisionAndOneUndoThatRestoresEveryCursor()
    {
        using var d = New(B("aaa\nbbb\nccc"));
        ByteRange[] before = [new(3, 3), new(7, 7), new(11, 11)];
        ByteRange[] after = [new(4, 4), new(9, 9), new(14, 14)];
        var r = d.Apply([.. before.Select(c => ByteEdit.InsertAt(c.Start, B("x")))], new EditOptions { SelectionsBefore = before, SelectionsAfter = after });
        Assert.True(r.Applied);
        Assert.Equal(1, d.Revision);
        Assert.Equal(B("aaax\nbbbx\ncccx"), Bytes(d));
        var undo = d.Undo()!;
        Assert.Equal(B("aaa\nbbb\nccc"), Bytes(d));
        Assert.Equal(before, undo.Selections);
        Assert.False(d.CanUndo);
        var redo = d.Redo()!;
        Assert.Equal(B("aaax\nbbbx\ncccx"), Bytes(d));
        Assert.Equal(after, redo.Selections);
    }

    [Fact]
    public void AMixedMultiRangeEditUndoesAndRedoesExactly()
    {
        using var d = New(B(Digits));
        var original = Bytes(d);
        d.Apply([new ByteEdit(new(1, 3), B("")), ByteEdit.InsertAt(5, B("QQQ")), new ByteEdit(new(8, 10), B("z")), new ByteEdit(new(12, 16), B("long long"))]);
        var edited = Bytes(d);
        Assert.Equal(B("034QQQ567zablong long"), edited);
        d.Undo();
        Assert.Equal(original, Bytes(d));
        d.Redo();
        Assert.Equal(edited, Bytes(d));
    }

    [Fact]
    public void AnEditThatConflictsLeavesNoUndoStepBehind()
    {
        using var d = New(B(Digits));
        d.Delete(new(0, 3));
        Assert.Equal(ConflictReason.Overlap, d.Replace(new(1, 2), B("y"), new EditOptions { BaseRevision = 0 }).Conflict);
        d.Undo();
        Assert.False(d.CanUndo);
        Assert.Equal(B(Digits), Bytes(d));
    }

    // ---- identity, fingerprint and files: P1.2.3 ----

    /// <summary>Overwrites a file the way another program would; false when the OS refuses because the document maps it.</summary>
    private bool Overwrite(string path, byte[] bytes, bool keepLength = false)
    {
        try
        {
            if (keepLength || bytes.Length >= new FileInfo(path).Length)
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                fs.Write(bytes);
                return true;
            }
            File.WriteAllBytes(path, bytes);
            return true;
        }
        catch (IOException) when (Factory is IBackendFactory { MapsFiles: true })
        {
            return false;
        }
    }

    private Doc OpenFile(string path, TestFormat f, int logCapacity = 4096) =>
        Doc.Open(path, Factory, new DocumentOptions { Format = f.Format, ChangeLogCapacity = logCapacity });

    private static byte[] Messy(TestFormat f)
    {
        switch (f.Name)
        {
            case "cp1250": return [.. "a\r\nb\nc\rd"u8, 0x81, 0x8D, 0x90, 0xE9, 0xFF, .. "\r\n\r"u8];
            case "utf16le": return [.. f.Bom, 0x61, 0, 0x0D, 0, 0x0A, 0, 0x00, 0xD8, 0x62, 0, 0x0A, 0, 0x00, 0xDC, 0x3D, 0xD8, 0x00, 0xDE, 0x0D, 0, 0x41];
            case "utf16be": return [.. f.Bom, 0, 0x61, 0, 0x0D, 0, 0x0A, 0xD8, 0x00, 0, 0x62, 0, 0x0A, 0xDC, 0x00, 0xD8, 0x3D, 0xDE, 0x00, 0, 0x0D, 0x41];
            default: return [.. f.Bom, .. "a\r\nb\nc\rd"u8, 0xE2, 0x82, (byte)'x', 0xFF, 0xC3, 0xA9, 0x80, .. "\r\n\r"u8];
        }
    }

    [Fact]
    public void OpenChangeOneCharSaveChangesOnlyThatRange()
    {
        foreach (var f in Formats)
        {
            using var dir = new TempDir();
            string path = dir.File("f.txt");
            var original = Messy(f);
            File.WriteAllBytes(path, original);
            using var d = OpenFile(path, f);
            Assert.Equal(original, Bytes(d));
            Assert.False(d.IsModified);
            Assert.Equal(path, d.FilePath);
            Assert.Equal(original.Length, d.Fingerprint!.Size);

            var range = new ByteRange(f.Bom.Length, f.Bom.Length + f.Format.Alignment);
            var replacement = f.Raw("Z");
            Assert.True(d.Replace(range, replacement).Applied);
            Assert.True(d.IsModified);
            Assert.True(d.Save().Saved);
            Assert.False(d.IsModified);

            byte[] expected = [.. original[..(int)range.Start], .. replacement, .. original[(int)range.End..]];
            var saved = File.ReadAllBytes(path);
            Assert.Equal(expected, saved);
            Assert.Equal(original.Length, saved.Length);
            for (int i = 0; i < saved.Length; i++)
                if (i < range.Start || i >= range.End) Assert.Equal(original[i], saved[i]);

            Assert.Equal(saved.Length, d.Fingerprint!.Size);
            Assert.Null(d.CheckExternalChange());
            Assert.Equal(expected, Bytes(d));
            Assert.True(d.Insert(f.Bom.Length, f.Raw("č")).Applied);
            Assert.True(d.Save().Saved);
            Assert.True(d.Undo() is not null);
            Assert.True(d.Save().Saved);
            Assert.Equal(expected, File.ReadAllBytes(path));
            Assert.Equal([Path.GetFileName(path)], Directory.GetFiles(dir.Path).Select(Path.GetFileName).ToArray());
        }
    }

    [Fact]
    public void ChangeByAnotherProgramRaisesAConflictEventWithRevisionAndFingerprint()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        using var d = OpenFile(path, TestFormat.Utf8);
        d.Insert(0, B("x"));
        var events = new List<ExternalChange>();
        d.ExternalChangeDetected += (_, e) => events.Add(e);
        Assert.Null(d.CheckExternalChange());

        Assert.True(Overwrite(path, B("written by someone else")));
        var change = d.CheckExternalChange()!;
        Assert.Equal(ExternalChangeKind.Modified, change.Kind);
        Assert.Equal(1, change.Revision);
        Assert.Equal(d.Id, change.DocumentId);
        Assert.Equal(16, change.Expected.Size);
        Assert.Equal(23, change.Actual!.Size);
        Assert.NotEqual(change.Expected.Sha256, change.Actual.Sha256);
        Assert.Single(events);
        d.CheckExternalChange();
        Assert.Single(events);
    }

    [Fact]
    public void AChangeWithTheSameSizeAndTimeIsFoundByTheHash()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        var time = File.GetLastWriteTimeUtc(path);
        using var d = OpenFile(path, TestFormat.Utf8);
        Assert.True(Overwrite(path, B("fedcba9876543210")));
        File.SetLastWriteTimeUtc(path, time);
        Assert.Equal(time, File.GetLastWriteTimeUtc(path));
        Assert.Equal(ExternalChangeKind.Modified, d.CheckExternalChange()!.Kind);
    }

    [Fact]
    public void ATouchWithoutNewContentIsNotAConflict()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        using var d = OpenFile(path, TestFormat.Utf8);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(3));
        Assert.Null(d.CheckExternalChange());
        Assert.True(d.Save().Saved);
    }

    [Fact]
    public void RenamedTruncatedOrDeletedFilesAreConflictsAndSaveDoesNotOverwrite()
    {
        foreach (string how in new[] { "rename", "truncate", "delete" })
        {
            using var dir = new TempDir();
            string path = dir.File("f.txt");
            File.WriteAllBytes(path, B(Digits));
            using var d = OpenFile(path, TestFormat.Utf8);
            d.Insert(0, B("x"));
            try
            {
                if (how == "rename") File.Move(path, dir.File("moved.txt"));
                else if (how == "truncate") Assert.True(Overwrite(path, B("0123")), "truncate");
                else File.Delete(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Xunit.Sdk.TrueException && Factory.MapsFiles)
            {
                continue; // the OS itself refuses to change a mapped file, so the guard has nothing to catch
            }

            var result = d.Save();
            Assert.False(result.Saved, how);
            Assert.Equal(how == "truncate" ? ExternalChangeKind.Modified : ExternalChangeKind.Missing, result.Conflict!.Kind);
            Assert.True(d.IsModified);
            if (how == "truncate") Assert.Equal(B("0123"), File.ReadAllBytes(path));
            else Assert.False(File.Exists(path));
            Assert.DoesNotContain(Directory.GetFiles(dir.Path), p => p.EndsWith(".tmp"));
        }
    }

    [Fact]
    public void SaveConflictKeepsTheOtherProgramsFileUntilTheUserOverwrites()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        using var d = OpenFile(path, TestFormat.Utf8);
        d.Insert(0, B("mine "));
        Assert.True(Overwrite(path, B("theirs theirs theirs")));

        var result = d.Save();
        Assert.False(result.Saved);
        Assert.Equal(1, result.Conflict!.Revision);
        Assert.Equal(B("theirs theirs theirs"), File.ReadAllBytes(path));
        Assert.True(d.Save(overwriteExternalChanges: true).Saved);
        // a mapped file shows the other writer's bytes through the mapping, so only unmapped backends can be compared here
        if (!Factory.MapsFiles) Assert.Equal(B("mine " + Digits), File.ReadAllBytes(path));
        Assert.False(d.IsModified);
        Assert.Null(d.CheckExternalChange());
    }

    [Fact]
    public void AChangeBetweenTheCheckAndTheWriteWithSameSizeAndTimeStillConflicts()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        var time = File.GetLastWriteTimeUtc(path);
        using var d = OpenFile(path, TestFormat.Utf8);
        d.Insert(0, B("mine "));
        d.BeforeSaveCommit = () =>
        {
            Assert.True(Overwrite(path, B("fedcba9876543210")));
            File.SetLastWriteTimeUtc(path, time);
        };

        var result = d.Save();
        Assert.False(result.Saved);
        Assert.Equal(B("fedcba9876543210"), File.ReadAllBytes(path));
        Assert.Equal([Path.GetFileName(path)], Directory.GetFiles(dir.Path).Select(Path.GetFileName).ToArray());
        Assert.True(d.IsModified);
    }

    [Fact]
    public void SavingAnUnnamedDocumentBindsItAndSavingElsewhereKeepsTheOriginal()
    {
        using var dir = new TempDir();
        using var d = New(B("hello"));
        Assert.Null(d.FilePath);
        Assert.Throws<InvalidOperationException>(() => d.Save());
        Assert.True(d.Save(dir.File("a.txt")).Saved);
        Assert.Equal(dir.File("a.txt"), d.FilePath);
        d.Insert(5, B("!"));
        Assert.True(d.Save(dir.File("b.txt")).Saved);
        Assert.Equal(B("hello"), File.ReadAllBytes(dir.File("a.txt")));
        Assert.Equal(B("hello!"), File.ReadAllBytes(dir.File("b.txt")));
        Assert.Equal(dir.File("b.txt"), d.FilePath);
        d.Insert(6, B("?"));
        Assert.True(d.Save().Saved);
        Assert.Equal(B("hello!?"), File.ReadAllBytes(dir.File("b.txt")));
    }

    [Fact]
    public void TheWatcherReportsAChangeFromAnotherWriter()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        using var d = OpenFile(path, TestFormat.Utf8);
        d.Insert(0, B("x"));
        var seen = new TaskCompletionSource<ExternalChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        d.ExternalChangeDetected += (_, e) => seen.TrySetResult(e);
        d.StartWatching(TimeSpan.FromMilliseconds(50));
        Assert.True(Overwrite(path, B("external content that is long")));
        Assert.True(seen.Task.Wait(TimeSpan.FromSeconds(10)), "no event");
        Assert.Equal(1, seen.Task.Result.Revision);
        Assert.Equal(ExternalChangeKind.Modified, seen.Task.Result.Kind);
    }

    [Fact]
    public void OurOwnSaveIsNotReportedAsAnExternalChange()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        using var d = OpenFile(path, TestFormat.Utf8);
        int count = 0;
        d.ExternalChangeDetected += (_, _) => Interlocked.Increment(ref count);
        d.StartWatching(TimeSpan.FromMilliseconds(30));
        d.Insert(0, B("x"));
        Assert.True(d.Save().Saved);
        Thread.Sleep(600);
        Assert.Equal(0, count);
    }

    [Fact]
    public void SnapshotsSurviveSavingOverTheOpenFile()
    {
        using var dir = new TempDir();
        string path = dir.File("f.txt");
        File.WriteAllBytes(path, B(Digits));
        using var d = OpenFile(path, TestFormat.Utf8);
        var s0 = d.GetSnapshot();
        d.Insert(0, B("AB"));
        var s1 = d.GetSnapshot();
        Assert.True(d.Save().Saved);
        d.Insert(0, B("C"));
        var s2 = d.GetSnapshot();
        Assert.Equal(B(Digits), s0.ReadBytes(new(0, s0.ByteLength)));
        Assert.Equal(B("AB" + Digits), s1.ReadBytes(new(0, s1.ByteLength)));
        Assert.Equal(B("CAB" + Digits), s2.ReadBytes(new(0, s2.ByteLength)));
        Assert.True(d.Save().Saved);
        Assert.Equal(B("CAB" + Digits), File.ReadAllBytes(path));
    }

    // ---- backends: P1.2.1 / P1.2.10 ----

    [Fact]
    public void CapabilitiesAreDeclared()
    {
        var caps = Factory.Capabilities;
        Assert.False(string.IsNullOrEmpty(caps.Name));
        Assert.True(caps.MaxByteLength > 0);
        using var d = New(B("x"));
        Assert.Equal(caps, d.Capabilities);
    }

    [Fact]
    public void ABackendThatCannotHoldAFormatSaysSoInsteadOfCorruptingIt()
    {
        if (Supports(TestFormat.Utf16LE)) return;
        Assert.Throws<NotSupportedException>(() => New(TestFormat.Utf16LE.Bytes("a"), TestFormat.Utf16LE));
    }
}
