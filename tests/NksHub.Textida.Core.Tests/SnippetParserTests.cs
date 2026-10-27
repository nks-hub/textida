using NksHub.Textida.Core.Snippets;

namespace NksHub.Textida.Core.Tests;

/// <summary>Snippet syntax: stops, defaults, choices, mirrors, variables, escapes, invalid input and the indentation of later lines.</summary>
public class SnippetParserTests
{
    [Fact]
    public void Stops_defaults_choices_and_the_final_caret_are_read_in_tab_order()
    {
        var s = SnippetParser.Parse("f(${2:b}, ${1|x,y|})$0;");
        Assert.Equal("f(b, x);", s.Text);
        Assert.Equal([1, 2], s.Stops.Select(x => x.Number));
        Assert.Equal(["x", "y"], s.Stops[0].Choices);
        Assert.Equal(new SnippetSpan(2, 1), s.Stops[1].Spans[0]);
        Assert.Equal(7, s.FinalOffset);
    }

    [Fact]
    public void The_same_number_is_one_stop_and_every_place_shows_the_default()
    {
        var s = SnippetParser.Parse("$1 = ${1:name};");
        Assert.Equal("name = name;", s.Text);
        Assert.Equal([new SnippetSpan(0, 4), new SnippetSpan(7, 4)], s.Stops.Single().Spans);
    }

    [Fact]
    public void Variables_escapes_and_invalid_input_follow_the_rules()
    {
        var s = SnippetParser.Parse(@"\$1 $TM_FILENAME ${missing} $this ${1:a ${x:y}} ${3 $", n => n == "TM_FILENAME" ? "a.cs" : null);
        Assert.Equal("$1 a.cs missing $this a y ${3 $", s.Text);
        Assert.Equal(["missing"], s.Missing);
    }

    [Fact]
    public void Later_lines_take_the_indentation_and_tabs_become_the_unit_and_stops_move_along()
    {
        var s = SnippetParser.Parse("if (${1:x}) {\n\t$0\n}").Reindent("  ", "    ");
        Assert.Equal("if (x) {\n      \n  }", s.Text);
        Assert.Equal(new SnippetSpan(4, 1), s.Stops.Single().Spans[0]);
        Assert.Equal(15, s.FinalOffset);
    }
}
