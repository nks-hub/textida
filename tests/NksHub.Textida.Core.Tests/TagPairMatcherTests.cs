using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

/// <summary>Pairing of the Latte block tags <c>{if}…{/if}</c>.</summary>
public class TagPairMatcherTests
{
    private static readonly string[] Template =
    [
        "{foreach $items as $item}",               // 0
        "  {if $item->ok}yes{else}no{/if}",         // 1
        "  {* {if} in a comment *}",                // 2
        "  <p title=\"{if $a}x{/if}\">{$item}</p>", // 3
        "  {label name /}",                         // 4
        "{/foreach}",                               // 5
        "{if $x}",                                  // 6
        "  {block}a{/}",                            // 7
    ];

    private static readonly Tokenizer Latte = BuiltinGrammars.Tokenizer("latte");

    private static TextPosition? Pair(int line, int column) => TagPairMatcher.FindPair(Template, Latte, line, column);

    [Fact]
    public void An_opening_tag_finds_its_closing_tag_and_back()
    {
        Assert.Equal(new TextPosition(5, 0), Pair(0, 1));
        Assert.Equal(new TextPosition(0, 0), Pair(5, 3));
        Assert.Equal(new TextPosition(1, 27), Pair(1, 3));
        Assert.Equal(new TextPosition(1, 2), Pair(1, 30));
    }

    [Fact]
    public void The_caret_may_sit_right_behind_the_closing_brace()
    {
        Assert.Equal(new TextPosition(0, 0), Pair(5, 10));
    }

    [Fact]
    public void Tags_in_comments_and_self_closing_tags_do_not_count_and_a_string_does_not_hide_a_tag()
    {
        Assert.Null(Pair(2, 6));
        Assert.Null(Pair(4, 4));
        Assert.Equal(new TextPosition(3, 20), Pair(3, 14));
    }

    [Fact]
    public void A_bare_close_tag_ends_the_innermost_block_and_an_unclosed_tag_has_no_partner()
    {
        Assert.Equal(new TextPosition(7, 10), Pair(7, 3));
        Assert.Equal(new TextPosition(7, 2), Pair(7, 11));
        Assert.Null(Pair(6, 1));
    }

    [Fact]
    public void Text_outside_any_tag_has_no_partner()
    {
        Assert.Null(Pair(1, 20));
        Assert.Null(Pair(1, 17));
    }
}
