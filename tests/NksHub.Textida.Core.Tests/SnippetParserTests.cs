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

    // ---- nested placeholders ----

    [Fact]
    public void A_stop_inside_the_default_of_another_is_a_place_of_its_own_and_its_mirrors_show_the_default()
    {
        var s = SnippetParser.Parse("for (${1:int ${2:i} = 0}; $2 < ${3:n}; $2++)");
        Assert.Equal("for (int i = 0; i < n; i++)", s.Text);
        Assert.Equal([1, 2, 3], s.Stops.Select(x => x.Number));
        Assert.Equal([new SnippetSpan(5, 9)], s.Stops[0].Spans);
        Assert.Equal([new SnippetSpan(9, 1), new SnippetSpan(16, 1), new SnippetSpan(23, 1)], s.Stops[1].Spans);
        Assert.Equal([new SnippetSpan(20, 1)], s.Stops[2].Spans);
    }

    [Fact]
    public void Placeholders_nest_to_any_depth_up_to_the_limit_and_a_deeper_one_stays_in_the_text()
    {
        var s = SnippetParser.Parse("${1:a ${2:b ${3:c ${4:d}}}}");
        Assert.Equal("a b c d", s.Text);
        Assert.Equal([new SnippetSpan(0, 7), new SnippetSpan(2, 5), new SnippetSpan(4, 3), new SnippetSpan(6, 1)], s.Stops.Select(x => x.Spans.Single()));

        // depth 2: ${3:...} stands in the default of a default of a default
        var shallow = SnippetParser.Parse("${1:a ${2:b ${3:c}}}", null, "\n", new SnippetLimits(2, 100, TimeSpan.FromSeconds(1)));
        Assert.Equal("a b ${3:c}", shallow.Text);
        Assert.Equal([1, 2], shallow.Stops.Select(x => x.Number));
    }

    [Fact]
    public void When_the_default_with_stops_stands_after_a_plain_mirror_it_is_the_place_that_is_edited()
    {
        var s = SnippetParser.Parse("$1 ${1:foo ${2:bar}}");
        Assert.Equal("foo bar foo bar", s.Text);
        Assert.Equal([new SnippetSpan(8, 7), new SnippetSpan(0, 7)], s.Stops[0].Spans); // the one that holds stop 2 first
        Assert.Equal([new SnippetSpan(12, 3)], s.Stops[1].Spans);
    }

    [Fact]
    public void A_stop_that_holds_itself_or_a_loop_of_stops_ends_and_never_repeats_for_ever()
    {
        Assert.Equal("", SnippetParser.Parse("${1:${1:x}}").Text);
        Assert.Equal(" ", SnippetParser.Parse("${1:${2:${1:y}}} $2").Text);
    }

    [Fact]
    public void A_tangled_or_exploding_snippet_is_refused_with_a_limit_error_instead_of_taking_for_ever()
    {
        var unclosed = string.Concat(Enumerable.Repeat("${1:${1:${1:", 4000));
        Assert.Throws<SnippetLimitException>(() => SnippetParser.Parse(unclosed));

        var explode = string.Concat(Enumerable.Range(1, 11).Select(n => $"${{{n}:${n + 1}${n + 1}${n + 1}${n + 1}}}")) + "${12:xxxxxxxxxxxxxxxxxxxxx}";
        var e = Assert.Throws<SnippetLimitException>(() => SnippetParser.Parse(explode));
        Assert.Contains("65536", e.Message);
        Assert.Throws<SnippetLimitException>(() => SnippetParser.Parse("${1:abcdef}", null, "\n", new SnippetLimits(8, 5, TimeSpan.FromSeconds(1))));
    }

    // ---- transforms ----

    [Fact]
    public void A_transform_of_a_stop_is_a_mirror_that_shows_the_changed_text_and_changes_it_again_when_asked()
    {
        var s = SnippetParser.Parse("${1:hello_world} ${1/(\\w+)_(\\w+)/${1:/camelcase}${2:/pascalcase}/}");
        Assert.Equal("hello_world helloWorld", s.Text);
        var stop = Assert.Single(s.Stops);
        Assert.Equal([new SnippetSpan(0, 11), new SnippetSpan(12, 10, stop.Spans[1].Transform)], stop.Spans);
        Assert.Null(stop.Spans[0].Transform);
        Assert.NotNull(stop.Spans[1].Transform);
        Assert.Equal("fooBar", stop.Spans[1].Transform!.Apply("foo_bar"));
    }

    [Fact]
    public void The_format_changes_the_case_and_tests_whether_a_group_took_part()
    {
        string Apply(string template, string input) =>
            SnippetParser.Parse(template, n => n == "V" ? input : null).Text;

        Assert.Equal("ABC abc ABC", Apply("${V/(.*)/${1:/upcase} ${1:/downcase} ${1:/capitalize}/}", "aBC"));
        Assert.Equal("helloWorld HelloWorld", Apply("${V/(.*)/${1:/camelcase} ${1:/pascalcase}/}", "hello_world"));
        Assert.Equal("X", Apply("${V/(a)|(x)/${1:+A}${2:?X:no}/}", "x"));
        Assert.Equal("Ano", Apply("${V/(a)|(x)/${1:+A}${2:?X:no}/}", "a"));
        Assert.Equal("none", Apply("${V/(y)?/${1:-none}/}", ""));
        Assert.Equal("[y]", Apply("${V/(y)/[${1:else}]/}", "y"));
        Assert.Equal("[else]", Apply("${V/(y)?/[${1:else}]/}", ""));
    }

    [Fact]
    public void The_g_option_changes_every_match_and_without_it_only_the_first_and_i_ignores_case()
    {
        string Apply(string options, string input) => SnippetParser.Parse("${V/a/_/" + options + "}", n => n == "V" ? input : null).Text;
        Assert.Equal("_bab", Apply("", "abab"));
        Assert.Equal("_b_b", Apply("g", "abab"));
        Assert.Equal("_b_B", Apply("gi", "AbaB"));
        Assert.Equal("Ab_b", Apply("", "Abab"));
    }

    [Fact]
    public void A_transform_of_a_variable_changes_its_value_and_a_missing_variable_is_reported()
    {
        var s = SnippetParser.Parse("${TM_FILENAME/(.*)\\..+$/$1/} ${TM_FILENAME/.*\\.(.+)$/${1:/upcase}/} ${nope/a/b/}", n => n == "TM_FILENAME" ? "my_file.cs" : null);
        Assert.Equal("my_file CS nope", s.Text);
        Assert.Equal(["nope"], s.Missing);
        Assert.Empty(s.Stops);
    }

    [Fact]
    public void A_slash_is_escaped_in_the_regex_and_in_the_format_and_a_dollar_in_the_format_too()
    {
        var s = SnippetParser.Parse("${P/^.*\\/(.*)$/<$1\\/\\$>/}", n => n == "P" ? "a/b/c.txt" : null);
        Assert.Equal("<c.txt/$>", s.Text);
    }

    [Fact]
    public void A_stop_with_only_transformed_places_is_edited_in_the_first_of_them()
    {
        var s = SnippetParser.Parse("${1/(.*)/<$1>/} ${1/(.*)/[$1]/}");
        var stop = Assert.Single(s.Stops);
        Assert.Equal(" []", s.Text);
        Assert.Null(stop.Spans[0].Transform);
        Assert.NotNull(stop.Spans[1].Transform);
    }

    [Fact]
    public void A_transform_that_does_not_parse_stays_in_the_text_as_it_is()
    {
        Assert.Equal("${1/(/x/}", SnippetParser.Parse("${1/(/x/}").Text); // invalid regular expression
        Assert.Equal("${1/a/b/z}", SnippetParser.Parse("${1/a/b/z}").Text); // unknown option
        Assert.Equal("${1/a/b/g", SnippetParser.Parse("${1/a/b/g").Text); // not closed
        Assert.Equal("${1/a/b", SnippetParser.Parse("${1/a/b").Text);
    }

    [Fact]
    public void A_transform_that_runs_too_long_stops_the_snippet_with_a_limit_error()
    {
        var limits = new SnippetLimits(8, 1000, TimeSpan.FromMilliseconds(10));
        var e = Assert.Throws<SnippetLimitException>(() => SnippetParser.Parse("${V/(x+x+)+y/!/}", n => new string('x', 60), "\n", limits));
        Assert.Contains("too long", e.Message);
    }

    [Fact]
    public void A_template_with_only_a_variable_transform_is_a_snippet_and_a_plain_variable_is_not()
    {
        Assert.True(SnippetParser.HasStops("name: ${TM_FILENAME/(.*)\\..+/$1/}"));
        Assert.True(SnippetParser.HasStops("${1/a/b/}"));
        Assert.False(SnippetParser.HasStops("${file.name} ${date} \\${1}"));
    }

    [Fact]
    public void Indenting_later_lines_keeps_the_transform_of_a_mirror()
    {
        var s = SnippetParser.Parse("a\n${1:x} ${1/x/y/}").Reindent("  ", "    ");
        var stop = Assert.Single(s.Stops);
        Assert.Equal("a\n  x y", s.Text);
        Assert.Null(stop.Spans[0].Transform);
        Assert.Equal("y", stop.Spans[1].Transform!.Apply("x"));
        Assert.Equal((4, 1), (stop.Spans[0].Start, stop.Spans[0].Length));
        Assert.Equal((6, 1), (stop.Spans[1].Start, stop.Spans[1].Length));
    }
}
