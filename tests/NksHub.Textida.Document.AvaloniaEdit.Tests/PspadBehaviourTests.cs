using System.Text;
using AvaloniaEdit.Document;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Document.AvaloniaEdit.Tests;

/// <summary>
/// P2.2.6: what the "pspad" profile switches on, as the cases of <c>08-editor-behavior</c> (chapters 1 and 2) state them: Home and End, Backspace in an indentation,
/// paired characters. The profile itself (keys, settings) is tested in the Workbench tests; here the behaviour behind each setting.
/// </summary>
public class PspadBehaviourTests
{
    private static (TextDocument Doc, int Caret) Open(string marked)
    {
        int caret = marked.IndexOf('|');
        return (new TextDocument(marked.Remove(caret, 1)), caret);
    }

    // ---- Home and End (ExtHome, ExtEnd) ----

    [Fact]
    public void Home_jumps_between_the_first_non_blank_character_and_the_line_start()
    {
        var (d, caret) = Open("    foo|");
        var carets = Carets.One(Sel.At(caret));
        int[] seen = new int[3];
        for (int i = 0; i < 3; i++)
        {
            carets = MultiCaret.Move(d, carets, CaretMove.LineStartEnhanced, false);
            seen[i] = carets.PrimarySel.Caret;
        }
        Assert.Equal([4, 0, 4], seen);
    }

    [Fact]
    public void End_jumps_between_the_last_non_blank_character_and_the_real_line_end()
    {
        var (d, caret) = Open("foo   |");
        var carets = Carets.One(Sel.At(caret));
        int[] seen = new int[3];
        for (int i = 0; i < 3; i++)
        {
            carets = MultiCaret.Move(d, carets, CaretMove.LineEndEnhanced, false);
            seen[i] = carets.PrimarySel.Caret;
        }
        Assert.Equal([3, 6, 3], seen);
    }

    [Theory]
    [InlineData("  a|b\n    c|d", CaretMove.LineStartEnhanced, "  |ab\n    |cd")]
    [InlineData("a|b  \n  |", CaretMove.LineEndEnhanced, "ab|  \n  |")]
    [InlineData("  |ab", CaretMove.LineStartEnhanced, "|  ab")]
    [InlineData("ab|", CaretMove.LineEndEnhanced, "ab|")]
    public void Home_and_End_work_at_every_caret(string input, CaretMove move, string expected)
    {
        var sels = new List<Sel>();
        var sb = new StringBuilder();
        foreach (char c in input) { if (c == '|') sels.Add(Sel.At(sb.Length)); else sb.Append(c); }
        var d = new TextDocument(sb.ToString());
        var moved = MultiCaret.Move(d, Carets.Create(sels, 0), move, false);
        var marked = new StringBuilder(d.Text);
        foreach (var s in moved.Items.OrderByDescending(s => s.Caret)) marked.Insert(s.Caret, '|');
        Assert.Equal(expected, marked.ToString());
    }

    // ---- Backspace in an indentation ----

    [Theory]
    [InlineData("        |x", 4, 4)]       // eight spaces, tab 4: back to the previous stop
    [InlineData("      |x", 4, 2)]         // column 6: back to 4
    [InlineData("  |x", 4, 2)]             // column 2: to 0
    [InlineData(" |x", 4, 1)]
    [InlineData("\t|x", 4, 1)]             // a tab goes whole
    [InlineData("\t  |x", 4, 2)]
    [InlineData("    |", 4, 4)]            // a blank line
    [InlineData("      |x", 2, 2)]         // another tab size
    public void Backspace_at_the_end_of_an_indentation_removes_back_to_the_previous_tab_stop(string input, int tab, int removed)
    {
        var (d, caret) = Open(input);
        Assert.True(IndentBackspace.TryPlan(d, Sel.At(caret), tab, out var edit, out var result));
        Assert.Equal(new ViewEdit(caret - removed, removed, ""), edit);
        Assert.Equal(Sel.At(caret - removed), result);
    }

    [Theory]
    [InlineData("a   |x")]       // text before the blanks
    [InlineData("|    x")]       // line start
    [InlineData("    x|")]       // not after blanks
    public void Backspace_elsewhere_is_the_ordinary_one(string input)
    {
        var (d, caret) = Open(input);
        Assert.False(IndentBackspace.TryPlan(d, Sel.At(caret), 4, out _, out _));
        Assert.False(IndentBackspace.TryPlan(d, new Sel(caret - 1, caret), 4, out _, out _));
    }

    [Fact]
    public void Backspace_in_the_indentation_of_several_lines_removes_a_stop_from_each()
    {
        var d = new TextDocument("        a\n      b\n  c");
        var carets = Carets.Create([Sel.At(8), Sel.At(16), Sel.At(20)], 0);
        var plan = MultiEdit.PlanDelete(d, carets, backwards: true, word: false, pairs: false, unindentTab: 4);
        Assert.Equal([new ViewEdit(4, 4, ""), new ViewEdit(14, 2, ""), new ViewEdit(18, 2, "")], plan.Select(p => p.Edit));
    }

    // ---- paired characters: the cases of chapter 2 ----

    private static string Type(string marked, char typed, bool markup = false)
    {
        var (d, caret) = Open(marked);
        var rule = new PspadPairsRule(markup);
        string text = d.Text;
        if (!rule.TryPlan(d, Sel.At(caret), typed, out var edit, out var result))
        {
            d.Insert(caret, typed.ToString());
            return d.Text.Insert(caret + 1, "|");
        }
        d.Replace(edit.Start, edit.Length, edit.Text);
        return d.Text.Insert(result.Caret, "|");
    }

    [Theory]
    [InlineData("foo|", '(', "foo(|)")]                  // at the end of the line
    [InlineData("foo| bar", '(', "foo(|) bar")]           // a blank follows
    [InlineData("foo|bar", '(', "foo(|bar")]              // a letter follows: no pair
    [InlineData("x = |", '"', "x = \"|\"")]                // a quote after a blank
    [InlineData("a|", '"', "a\"|")]                        // a line of two characters
    [InlineData("|", '"', "\"|\"")]                        // an empty line
    [InlineData("ab|", '"', "ab\"|")]                      // a quote after a letter
    [InlineData("(|)", ')', "()|")]                        // typed over
    [InlineData("x|)", ')', "x)|")]                        // typed over, though the editor did not add it
    [InlineData("\"a|\"", '"', "\"a\"|")]
    [InlineData("a|b", '[', "a[|b")]
    [InlineData("x = |", '\'', "x = '|'")]
    [InlineData("x = |", '`', "x = `|`")]
    [InlineData("f|", '{', "f{|}")]
    public void Paired_characters_follow_the_rules_of_the_original(string input, char typed, string expected) =>
        Assert.Equal(expected, Type(input, typed));

    [Fact]
    public void A_bracket_of_angle_pairs_in_markup_only()
    {
        Assert.Equal("<|>", Type("|", '<', markup: true));
        Assert.Equal("<|", Type("|", '<', markup: false));
        Assert.Equal("<a>|", Type("<a|>", '>', markup: true));
    }

    [Fact]
    public void A_selection_is_replaced_by_the_character_not_wrapped()
    {
        var d = new TextDocument("say wordhere");
        var rule = new PspadPairsRule(false);
        // a letter follows the selection: no pair, so the editor inserts the character itself over the selection
        Assert.False(rule.TryPlan(d, new Sel(4, 8), '(', out _, out _));
        var d2 = new TextDocument("say word");
        Assert.True(rule.TryPlan(d2, new Sel(4, 8), '(', out var edit, out var result));
        Assert.Equal(new ViewEdit(4, 4, "()"), edit);
        Assert.Equal(Sel.At(5), result);
    }

    // ---- the partner of a renamed tag as part of one transaction, and typing runs as one undo step ----

    private sealed class Probe(TextDocument d, Tokenizer tokenizer) : ITokenProbe
    {
        public IReadOnlyList<Token> TokensOfLine(int line)
        {
            var state = tokenizer.InitialState;
            List<Token> tokens = [];
            for (int n = 1; n <= line; n++)
            {
                var l = d.GetLineByNumber(n);
                tokens = [];
                state = tokenizer.TokenizeLine(d.GetText(l.Offset, l.Length), state, tokens);
            }
            return tokens;
        }
    }

    private static (Doc Doc, AvaloniaEditBackend Backend) Document(string text)
    {
        var doc = Doc.FromBytes(Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, new DocumentOptions { Format = TextCodec.Utf8 });
        return (doc, (AvaloniaEditBackend)doc.Backend);
    }

    [Fact]
    public void Typing_over_a_tag_name_renames_the_closing_tag_in_the_same_undo_step()
    {
        var (doc, backend) = Document("<div>x</div>");
        var d = backend.TextDocument;
        var keys = new SmartKeys(d, new Probe(d, BuiltinGrammars.Tokenizer("html")), "html", new SmartKeyOptions());
        var carets = Carets.One(new Sel(1, 4)); // "div" selected
        var plan = MultiEdit.PlanTyped(d, carets, "s", [], e => keys.TryPlanRename(e, out var partner) ? partner : null);
        var applied = MultiEdit.Apply(doc, backend, carets, plan, "typing#1");
        Assert.True(applied.Edit.Applied);
        Assert.Equal("<s>x</s>", d.Text);
        Assert.Equal(Encoding.UTF8.GetBytes("<s>x</s>"), doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
        Assert.Equal([Sel.At(2)], applied.Carets.Items);
        Assert.Equal(1, doc.UndoDepth);
        doc.Undo();
        Assert.Equal("<div>x</div>", d.Text);
    }

    [Fact]
    public void A_typing_run_with_one_key_is_one_undo_step_that_restores_the_carets_before_the_first_key()
    {
        var (doc, backend) = Document("a b");
        var carets = Carets.Create([Sel.At(1), Sel.At(3)], 0);
        Carets Type(Carets c, string s) => MultiEdit.Apply(doc, backend, c, MultiEdit.PlanText(c, s), "typing#1").Carets;
        var one = Type(carets, "x");
        var two = Type(one, "y");
        var three = Type(two, "z");
        Assert.Equal("axyz bxyz", backend.TextDocument.Text);
        Assert.Equal(1, doc.UndoDepth);
        var undone = doc.Undo()!;
        Assert.Equal("a b", backend.TextDocument.Text);
        Assert.Equal([new ByteRange(3, 3), new ByteRange(1, 1)], undone.Selections); // the primary caret last
        var redone = doc.Redo()!;
        Assert.Equal("axyz bxyz", backend.TextDocument.Text);
        Assert.Equal(three.Items.Select(s => s.Caret).Order(), redone.Selections.Select(r => (int)r.Start).Order());
    }

    [Fact]
    public void Another_key_starts_another_undo_step()
    {
        var (doc, backend) = Document("a b");
        var carets = Carets.Create([Sel.At(1), Sel.At(3)], 0);
        var one = MultiEdit.Apply(doc, backend, carets, MultiEdit.PlanText(carets, "x"), "typing#1").Carets;
        MultiEdit.Apply(doc, backend, one, MultiEdit.PlanText(one, "y"), "typing#2");
        Assert.Equal(2, doc.UndoDepth);
    }
}
