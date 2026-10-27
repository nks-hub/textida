using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.6.2: replacing bytes with a preview, one undo step and a revision check.</summary>
public class ByteReplaceTests
{
    private static Doc Make(byte[] bytes, TextCodec? codec = null) => Doc.FromBytes(bytes, new PagedBackendFactory(startIndexing: false), new() { Format = codec ?? TextCodec.Cp1252 });

    private static byte[] All(Doc d) => d.ReadBytes(new ByteRange(0, d.ByteLength));

    private static BytePattern Hex(string text) => BytePattern.ParseHex(text);

    [Fact]
    public void The_preview_shows_old_and_new_bytes_and_the_length_change_and_replace_all_is_one_undo_step()
    {
        byte[] original = [1, 2, 0xAA, 0xBB, 3, 0xAA, 0xBB, 4, 5, 0xAA, 0xBB];
        using var d = Make(original);
        int depth = d.UndoDepth;

        var plan = ByteReplace.Plan(d, Hex("AA BB"), new byte[] { 0xC1, 0xC2, 0xC3 }, overwrite: false);

        Assert.Equal(3, plan.Matches);
        Assert.True(plan.ChangesLength);
        Assert.Equal(3, plan.LengthDelta);
        Assert.Equal(original.Length + 3, plan.Inspection.SizeAfter);
        Assert.Equal([2L, 5L, 9L], plan.Inspection.Ranges.Select(r => r.Start));
        Assert.All(plan.Inspection.Ranges, r => { Assert.Equal(new byte[] { 0xAA, 0xBB }, r.OldHead); Assert.Equal(new byte[] { 0xC1, 0xC2, 0xC3 }, r.NewHead); });
        Assert.Equal(original, All(d)); // a preview changes nothing

        Assert.Equal(ConflictReason.NotConfirmed, ByteReplace.Apply(d, plan).Conflict);
        plan.Inspection.Confirm();
        Assert.True(ByteReplace.Apply(d, plan).Applied);
        Assert.Equal([1, 2, 0xC1, 0xC2, 0xC3, 3, 0xC1, 0xC2, 0xC3, 4, 5, 0xC1, 0xC2, 0xC3], All(d));
        Assert.Equal(depth + 1, d.UndoDepth);

        Assert.NotNull(d.Undo());
        Assert.Equal(original, All(d));
    }

    [Fact]
    public void A_preview_is_refused_over_a_newer_revision_even_when_no_match_is_touched()
    {
        using var d = Make([0xAA, 0xBB, 0, 0, 0xAA, 0xBB]);
        var plan = ByteReplace.Plan(d, Hex("AA BB"), new byte[] { 1, 2 }, overwrite: false);
        plan.Inspection.Confirm();
        Assert.True(d.Insert(3, new byte[] { 9 }).Applied);

        var result = ByteReplace.Apply(d, plan);

        Assert.False(result.Applied);
        Assert.Equal([0xAA, 0xBB, 0, 9, 0, 0xAA, 0xBB], All(d));
    }

    [Fact]
    public void Overwrite_mode_forbids_a_change_of_length_and_allows_the_same_length()
    {
        using var d = Make([1, 0xAA, 0xBB, 2]);
        var longer = ByteReplace.Plan(d, Hex("AA BB"), new byte[] { 1, 2, 3 }, overwrite: true);
        Assert.Equal(ReplaceBlock.LengthInOverwrite, longer.Blocked);
        longer.Inspection.Confirm();
        Assert.False(ByteReplace.Apply(d, longer).Applied);
        Assert.Equal([1, 0xAA, 0xBB, 2], All(d));

        var same = ByteReplace.Plan(d, Hex("AA BB"), new byte[] { 7, 8 }, overwrite: true);
        Assert.Equal(ReplaceBlock.None, same.Blocked);
        Assert.False(same.ChangesLength);
        same.Inspection.Confirm();
        Assert.True(ByteReplace.Apply(d, same).Applied);
        Assert.Equal([1, 7, 8, 2], All(d));
    }

    [Fact]
    public void Overlapping_matches_are_replaced_from_the_left()
    {
        using var d = Make("aaaaa"u8.ToArray());
        var plan = ByteReplace.Plan(d, BytePattern.FromBytes("aa"u8), "b"u8.ToArray(), overwrite: false);
        Assert.Equal(2, plan.Matches);
        plan.Inspection.Confirm();
        ByteReplace.Apply(d, plan);
        Assert.Equal("bba"u8.ToArray(), All(d));
    }

    [Fact]
    public void A_regex_with_matches_of_different_lengths_is_replaced_and_an_empty_replacement_deletes()
    {
        using var d = Make("x12y345z6"u8.ToArray());
        var plan = ByteReplace.Plan(d, ByteRegex.Parse(@"\d+", 5), "#"u8.ToArray(), overwrite: false);
        Assert.Equal(3, plan.Matches);
        plan.Inspection.Confirm();
        ByteReplace.Apply(d, plan);
        Assert.Equal("x#y#z#"u8.ToArray(), All(d));

        var delete = ByteReplace.Plan(d, BytePattern.FromBytes("#"u8), ReadOnlyMemory<byte>.Empty, overwrite: false);
        delete.Inspection.Confirm();
        ByteReplace.Apply(d, delete);
        Assert.Equal("xyz"u8.ToArray(), All(d));
    }

    [Fact]
    public void Bytes_may_split_characters_and_a_range_limits_the_search()
    {
        using var d = Make(Encoding.UTF8.GetBytes("ř ř ř"), TextCodec.Utf8);
        var plan = ByteReplace.Plan(d, BytePattern.FromBytes([0xC5]), new byte[] { 0xC4 }, overwrite: true, within: new ByteRange(3, 6));
        Assert.Equal(1, plan.Matches);
        plan.Inspection.Confirm();
        Assert.True(ByteReplace.Apply(d, plan).Applied);
        Assert.Equal([0xC5, 0x99, 0x20, 0xC4, 0x99, 0x20, 0xC5, 0x99], All(d));
    }

    [Fact]
    public void Nothing_found_is_an_empty_plan_that_applies_as_nothing()
    {
        using var d = Make([1, 2, 3]);
        var plan = ByteReplace.Plan(d, Hex("FF"), new byte[] { 0 }, overwrite: false);
        Assert.Equal(0, plan.Matches);
        plan.Inspection.Confirm();
        Assert.True(ByteReplace.Apply(d, plan).Applied);
        Assert.Equal(0, d.UndoDepth);
    }

    [Fact]
    public void Exact_gives_the_bytes_of_a_pattern_without_wildcards()
    {
        Assert.Equal(new byte[] { 0x50, 0x4B }, Hex("50 4B").Exact);
        Assert.Null(Hex("50 ??").Exact);
        Assert.Null(Hex("5?").Exact);
    }

    [Fact]
    public void Fifteen_hundred_matches_are_listed_in_part_but_all_replaced_in_one_step()
    {
        var bytes = Enumerable.Range(0, 4500).Select(i => (byte)(i % 3 == 0 ? 0xFE : 0x01)).ToArray();
        using var d = Make(bytes);
        var plan = ByteReplace.Plan(d, Hex("FE"), new byte[] { 0xFD, 0xFD }, overwrite: false);
        Assert.Equal(1500, plan.Matches);
        Assert.Equal(1500, plan.Inspection.RangeCount);
        Assert.Equal(ByteInspection.MaxListed, plan.Inspection.Ranges.Count);
        plan.Inspection.Confirm();
        Assert.True(ByteReplace.Apply(d, plan).Applied);
        Assert.Equal(6000, d.ByteLength);
        Assert.NotNull(d.Undo());
        Assert.Equal(bytes, All(d));
    }
}
