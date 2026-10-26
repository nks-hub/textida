using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P2.2.3: normalisation of selection sets and selections that follow the document through anchors.</summary>
public class SelectionTests
{
    private static List<TextSelection> Parse(string spec) =>
        [.. spec.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':')).Select(p => new TextSelection(long.Parse(p[0]), long.Parse(p[^1])))];

    private static string Show(SelectionSet set) => string.Join(' ', set.Items.Select((s, i) => (i == set.Primary ? "*" : "") + s.Anchor + ":" + s.Caret));

    [Theory]
    [InlineData("0:3 5:8", 0, "*0:3 5:8")]                  // apart: stay separate
    [InlineData("5:8 0:3", 1, "*0:3 5:8")]                  // sorted, primary follows its selection
    [InlineData("0:3 2:5", 0, "*0:5")]                      // overlap
    [InlineData("0:3 3:5", 1, "*0:5")]                      // touching
    [InlineData("0:5 1:2", 1, "*0:5")]                      // contained
    [InlineData("4 4", 0, "*4:4")]                          // equal carets
    [InlineData("4 5", 0, "*4:4 5:5")]                      // neighbouring carets are two
    [InlineData("3 0:3", 0, "*0:3")]                        // caret on the end of a selection
    [InlineData("3 3:6", 1, "*3:6")]                        // caret on the start of a selection
    [InlineData("4 2:6", 0, "*2:6")]                        // caret inside a selection
    [InlineData("8:2 5:3", 1, "*8:2")]                      // backward selection stays backward ...
    [InlineData("2:8 3:5", 1, "*2:8")]                      // ... forward stays forward
    [InlineData("8:2 3:5", 1, "*2:8")]                      // direction of the primary member wins
    [InlineData("0:2 1:4 9:9 10:12", 3, "0:4 9:9 *10:12")]  // several groups, primary in the last
    [InlineData("0:2 9:9", 7, "0:2 *9:9")]                  // primary index is clamped
    public void Selections_merge_by_the_table(string input, int primary, string expected) =>
        Assert.Equal(expected, Show(SelectionSet.Create(Parse(input), primary)));

    [Fact]
    public void An_empty_input_is_refused() =>
        Assert.Throws<ArgumentException>(() => SelectionSet.Create([], 0));

    private static Doc New(string text) =>
        Doc.FromBytes(Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance, new DocumentOptions { Format = NksHub.Textida.Core.Encodings.TextCodec.Utf8 });

    private static byte[] U(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Text_inserted_before_the_selections_leaves_them_on_the_same_text()
    {
        using var doc = New("one two three");
        var anchored = new AnchoredSelections(doc, SelectionSet.Create(Parse("0:3 4:7 8:13"), 1));
        Assert.True(doc.Insert(0, U(">>> ")).Applied);
        var now = anchored.Current()!;
        Assert.Equal("4:7 *8:11 12:17", Show(now));
        Assert.Equal(["one", "two", "three"], now.Items.Select(s => doc.ReadText(s.ToRange())));
        Assert.Equal(1, now.Primary);
    }

    [Fact]
    public void Text_inserted_at_an_edge_goes_outside_and_a_caret_moves_behind_it()
    {
        using var doc = New("abcdef");
        var anchored = new AnchoredSelections(doc, SelectionSet.Create(Parse("1:3 5"), 0));
        Assert.True(doc.Apply([ByteEdit.InsertAt(1, U("[")), ByteEdit.InsertAt(3, U("]")), ByteEdit.InsertAt(5, U("|"))]).Applied);
        var now = anchored.Current()!;
        Assert.Equal("a[bc]de|f", Encoding.UTF8.GetString(doc.ReadBytes(new(0, doc.ByteLength))));
        Assert.Equal("bc", doc.ReadText(now.Items[0].ToRange()));
        Assert.Equal(8, now.Items[1].Caret); // behind the inserted bar
    }

    [Fact]
    public void A_backward_selection_keeps_its_direction()
    {
        using var doc = New("0123456789");
        var anchored = new AnchoredSelections(doc, SelectionSet.Create(Parse("6:2"), 0));
        doc.Insert(0, U("xx"));
        var s = anchored.Current()!.PrimarySelection;
        Assert.Equal(new TextSelection(8, 4), s);
    }

    [Fact]
    public void A_deletion_that_cuts_one_end_leaves_a_caret_and_one_that_cuts_both_drops_the_selection()
    {
        using var doc = New("0123456789");
        var anchored = new AnchoredSelections(doc, SelectionSet.Create(Parse("1:3 5:8 9"), 2));
        doc.Delete(new(2, 6)); // end of the first and start of the second are inside the deleted part
        var now = anchored.Current()!;
        Assert.Equal("1:1 4:4 *5:5", Show(now));

        var gone = new AnchoredSelections(doc, SelectionSet.Create(Parse("1:3"), 0));
        doc.Delete(new(0, 4));
        Assert.Null(gone.Current());
    }

    [Fact]
    public void One_transaction_over_many_carets_is_one_undo_step_that_gives_all_carets_back()
    {
        using var doc = New("aXbXcX");
        var before = Parse("1:2 3:4 5:6").Select(s => s.ToRange()).ToList();
        var edits = before.Select(r => new ByteEdit(r, U("--"))).ToList();
        var after = new List<ByteRange> { new(3, 3), new(6, 6), new(9, 9) };
        var anchored = new AnchoredSelections(doc, SelectionSet.Create(Parse("1:2 3:4 5:6"), 0));
        Assert.True(doc.Apply(edits, new EditOptions { SelectionsBefore = before, SelectionsAfter = after }).Applied);
        Assert.Equal("a--b--c--", Encoding.UTF8.GetString(doc.ReadBytes(new(0, doc.ByteLength))));
        Assert.Equal(1, doc.UndoDepth);
        Assert.Equal(["--", "--", "--"], anchored.Current()!.Items.Select(s => doc.ReadText(s.ToRange())));

        var undone = doc.Undo()!;
        Assert.Equal("aXbXcX", Encoding.UTF8.GetString(doc.ReadBytes(new(0, doc.ByteLength))));
        Assert.Equal(before, undone.Selections);
        var redone = doc.Redo()!;
        Assert.Equal("a--b--c--", Encoding.UTF8.GetString(doc.ReadBytes(new(0, doc.ByteLength))));
        Assert.Equal(after, redone.Selections);
    }

    [Fact]
    public void Anchors_survive_a_replacement_from_another_source_and_its_undo()
    {
        using var doc = New("foo bar foo");
        var anchored = new AnchoredSelections(doc, SelectionSet.Create(Parse("0:3 8:11"), 0));
        doc.Replace(new(4, 7), U("quux"), new EditOptions { Source = "replace" });
        Assert.Equal(["foo", "foo"], anchored.Current()!.Items.Select(s => doc.ReadText(s.ToRange())));
        doc.Undo();
        Assert.Equal(["foo", "foo"], anchored.Current()!.Items.Select(s => doc.ReadText(s.ToRange())));
    }
}
