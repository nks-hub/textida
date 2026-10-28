using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>P9.2.1: the most undo steps a document keeps.</summary>
public class UndoLimitTests
{
    private static Doc Open(string text = "") => Doc.FromBytes(System.Text.Encoding.UTF8.GetBytes(text), ReferenceBackendFactory.Instance);

    private static string Text(Doc d) => d.ReadText(new ByteRange(0, d.ByteLength));

    /// <summary>Appends one letter per step.</summary>
    private static void Type(Doc d, string letters)
    {
        foreach (char c in letters) d.Insert(d.ByteLength, new[] { (byte)c });
    }

    private static int Undos(Doc d)
    {
        int n = 0;
        while (d.CanUndo) { d.Undo(); n++; }
        return n;
    }

    [Fact]
    public void Without_a_limit_every_step_is_kept_and_a_limit_keeps_the_newest_ones()
    {
        using var all = Open();
        Type(all, "abcdef");
        Assert.Equal(0, all.UndoLimit);
        Assert.Equal(6, Undos(all));
        Assert.Equal("", Text(all));

        using var limited = Open();
        limited.UndoLimit = 3;
        Type(limited, "abcdef");
        Assert.Equal(3, Undos(limited));
        Assert.Equal("abc", Text(limited)); // the steps that were dropped stay applied
    }

    [Fact]
    public void Lowering_the_limit_drops_the_oldest_steps_at_once_and_raising_it_does_not_bring_them_back()
    {
        using var d = Open();
        Type(d, "abcdef");
        d.UndoLimit = 2;
        d.UndoLimit = 100;
        Type(d, "gh");
        Assert.Equal(4, Undos(d));
        Assert.Equal("abcd", Text(d));
        d.UndoLimit = 0;
        Assert.Equal(0, d.UndoLimit);
    }

    [Fact]
    public void The_depth_still_counts_a_step_that_pushes_the_oldest_one_out()
    {
        using var d = Open();
        d.UndoLimit = 2;
        int depth = d.UndoDepth;
        for (int i = 1; i <= 5; i++)
        {
            Type(d, "x");
            Assert.Equal(depth + i, d.UndoDepth); // a journal tells a new step from a merged edit by this
        }
        d.Undo();
        Assert.Equal(depth + 4, d.UndoDepth);
        d.Redo();
        Assert.Equal(depth + 5, d.UndoDepth);
    }

    [Fact]
    public void A_document_whose_saved_state_was_dropped_stays_modified_even_when_everything_left_is_undone()
    {
        using var d = Open("base");
        d.UndoLimit = 2;
        Assert.False(d.IsModified);
        Type(d, "123");
        Assert.True(d.IsModified);
        Assert.Equal(2, Undos(d));
        Assert.Equal("base1", Text(d)); // the opened state cannot be reached any more
        Assert.True(d.IsModified);

        d.MarkClean();
        Assert.False(d.IsModified);
        Type(d, "ab");
        Assert.Equal(2, Undos(d));
        Assert.False(d.IsModified); // the saved state is where the stack ends, and it is still there
    }

    [Fact]
    public void A_saved_state_that_is_still_in_the_stack_is_reached_by_undo_and_one_that_was_pushed_out_is_not()
    {
        using var d = Open();
        d.UndoLimit = 3;
        Type(d, "ab");
        d.MarkClean();
        Type(d, "c"); // the stack is a, b, c; the saved state is after b
        d.Undo();
        Assert.False(d.IsModified);
        Type(d, "de"); // a is pushed out, the stack is b, d, e: the step that made the saved state is still there
        d.Undo();
        d.Undo();
        Assert.False(d.IsModified);
        Type(d, "fgh"); // the stack is f, g, h and b is gone: the saved state lies beyond it
        Assert.True(d.IsModified);
        Undos(d);
        Assert.True(d.IsModified);
    }

    [Fact]
    public void A_step_that_is_redone_pushes_the_oldest_one_out_when_the_limit_was_lowered_meanwhile()
    {
        using var d = Open();
        Type(d, "abc");
        d.Undo();
        d.Undo(); // the stack is a; b and c wait to be redone
        d.UndoLimit = 1; // nothing to cut: one step is on the stack
        d.Redo();
        d.Redo();
        Assert.Equal("abc", Text(d));
        Assert.Equal(1, Undos(d)); // only c is left
        Assert.Equal("ab", Text(d));
    }

    [Fact]
    public void A_group_of_edits_is_one_step_whatever_its_size()
    {
        using var d = Open();
        d.UndoLimit = 2;
        for (int i = 0; i < 3; i++)
            using (d.BeginUndoGroup())
            {
                Type(d, "xyz");
            }
        Assert.Equal(2, Undos(d));
        Assert.Equal("xyz", Text(d));
    }
}
