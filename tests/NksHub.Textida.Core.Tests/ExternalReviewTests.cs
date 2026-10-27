using System.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P6.1.1: the hunks of a change from outside, as the reverts that bring the base back.</summary>
public class ExternalReviewTests
{
    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    /// <summary>Applies the hunks that are not kept and returns the bytes of the document.</summary>
    private static byte[] Revert(Doc doc, FileChange change, params int[] keep)
    {
        var selection = new ChangeSelection();
        foreach (int id in keep) selection.RejectHunk(change, id);
        var report = new ChangeApplier(_ => doc).Apply(ChangeBuilder.Proposal("t", "review", change), selection);
        Assert.NotEqual(ChangeStatus.Conflict, report.Files[0].Status);
        return doc.ReadBytes(new ByteRange(0, doc.ByteLength));
    }

    [Fact]
    public void Every_changed_block_is_a_hunk_and_reverting_all_gives_back_the_base_bytes()
    {
        var old = Utf8("one\ntwo ěšč\nthree\nfour\nfive\nsix\n");
        var now = Utf8("one\nTWO ěšč\nthree\nfour\nfive\nSIX\nseven\n");
        using var doc = Doc.FromBytes(now, ReferenceBackendFactory.Instance);

        var change = ExternalReview.BuildRevert(doc, old)!;

        Assert.Equal(2, change.Hunks.Count);
        Assert.Equal("TWO ěšč\n", change.Hunks[0].OldText);
        Assert.Equal("two ěšč\n", change.Hunks[0].NewText);
        Assert.Equal(old, Revert(doc, change));
    }

    [Fact]
    public void Keeping_a_hunk_leaves_its_bytes_and_everything_outside_the_reverted_hunks_alone()
    {
        var old = Utf8("a\nb\nc\nd\ne\nf\n");
        var now = Utf8("A\nb\nc\nD\ne\nF\n");
        using var doc = Doc.FromBytes(now, ReferenceBackendFactory.Instance);
        var change = ExternalReview.BuildRevert(doc, old)!;
        Assert.Equal(3, change.Hunks.Count);

        Assert.Equal(Utf8("A\nb\nc\nd\ne\nf\n"), Revert(doc, change, keep: change.Hunks[0].Id));
    }

    [Fact]
    public void Reverting_is_one_undo_step()
    {
        var old = Utf8("a\nb\nc\n");
        using var doc = Doc.FromBytes(Utf8("A\nb\nC\n"), ReferenceBackendFactory.Instance);
        int depth = doc.UndoDepth;
        Revert(doc, ExternalReview.BuildRevert(doc, old)!);
        Assert.Equal(depth + 1, doc.UndoDepth);
    }

    [Theory]
    [InlineData("a\nb", "a\nb\n")]
    [InlineData("a\r\nb\r\n", "a\nb\n")]
    [InlineData("a\nb\n", "a\r\nb\r\n")]
    [InlineData("a\nb\n", "a\nb")]
    [InlineData("", "text\n")]
    [InlineData("text\n", "")]
    public void A_difference_the_line_diff_cannot_see_still_reverts_exactly(string now, string old)
    {
        using var doc = Doc.FromBytes(Utf8(now), ReferenceBackendFactory.Instance);
        var change = ExternalReview.BuildRevert(doc, Utf8(old))!;
        Assert.NotNull(change);
        Assert.Equal(Utf8(old), Revert(doc, change));
    }

    [Fact]
    public void The_same_content_has_nothing_to_review()
    {
        using var doc = Doc.FromBytes(Utf8("same\n"), ReferenceBackendFactory.Instance);
        Assert.Null(ExternalReview.BuildRevert(doc, Utf8("same\n")));
    }

    [Fact]
    public void The_shown_change_is_the_revert_the_other_way_round()
    {
        using var doc = Doc.FromBytes(Utf8("x\nNEW\n"), ReferenceBackendFactory.Instance);
        var revert = ExternalReview.BuildRevert(doc, Utf8("x\nOLD\n"))!;
        var shown = ExternalReview.AsChange(revert);
        Assert.Equal(("OLD\n", "NEW\n"), (shown.Hunks[0].OldText, shown.Hunks[0].NewText));
        Assert.Equal(revert.Hunks[0].Id, shown.Hunks[0].Id);
    }
}
