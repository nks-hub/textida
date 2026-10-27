using System.Text.Json;
using NksHub.Textida.PluginHost.Conversions;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P4.2.1 and P4.11.1 without a window: the converter engine, the install-time checks of converters and recipes, and the recipe runner.</summary>
public class ConversionTests
{
    private const string Header = "\"schemaVersion\":1,\"id\":\"acme.conv\",\"name\":\"Conv\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1}";

    private static string Manifest(string contributes) => "{" + Header + ",\"contributes\":{" + contributes + "}}";

    private static PluginLoadResult Load(string manifest)
    {
        var dir = T.TempDir();
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), manifest);
        return PluginManifestLoader.Load(dir, 1, 1 << 20);
    }

    private static ConverterContribution Converter(params (string Find, string Replace, bool Regex)[] rules) =>
        new() { Id = "c", Title = "C", Rules = [.. rules.Select(r => new ConverterRule { Find = r.Find, Replace = r.Replace, Regex = r.Regex })] };

    private static VariableContext Context(params (string Name, string Value)[] user) =>
        new() { FilePath = null, Now = new DateTime(2026, 10, 27, 13, 5, 9), User = user.ToDictionary(u => u.Name, u => u.Value) };

    // ---- converters ----

    [Fact]
    public void Rules_run_in_order_each_on_the_result_of_the_one_before()
    {
        var converter = Converter(("a", "b", false), ("b", "c", false));
        Assert.Equal("cc c", TextConversion.Apply(converter, "ab b").Text);
    }

    [Fact]
    public void An_entity_table_replaces_literal_text_without_reading_it_as_a_pattern()
    {
        var converter = Converter(("&", "&amp;", false), ("<", "&lt;", false), ("a.c", "X", false));
        Assert.Equal("a &amp; b &lt;i> X abc", TextConversion.Apply(converter, "a & b <i> a.c abc").Text);
    }

    [Fact]
    public void A_regex_rule_uses_the_dialect_of_the_search_with_groups_and_case_codes_and_can_ignore_case()
    {
        var converter = new ConverterContribution
        {
            Rules =
            [
                new ConverterRule { Find = @"(\w+)@(\w+)", Replace = @"\U$1\E at $2", Regex = true },
                new ConverterRule { Find = "AT", Replace = "@", IgnoreCase = true },
            ],
        };
        Assert.Equal("ANN @ home", TextConversion.Apply(converter, "ann@home").Text);
    }

    [Fact]
    public void A_pattern_that_cannot_run_gives_a_problem_and_no_text()
    {
        var result = TextConversion.Apply(Converter(("(", "x", true)), "text");
        Assert.False(result.Ok);
        Assert.Null(result.Text);
        Assert.NotEmpty(result.Problem!);
    }

    [Fact]
    public void A_converter_with_samples_loads_as_a_data_plugin_and_its_command_is_a_converter_action()
    {
        var result = Load(Manifest("\"commands\":[{\"id\":\"acme.conv.amp\",\"title\":\"Amp\"}]," +
            "\"converters\":[{\"id\":\"amp\",\"title\":\"Amp\",\"command\":\"acme.conv.amp\",\"rules\":[{\"find\":\"&\",\"replace\":\"&amp;\"}]," +
            "\"samples\":[{\"input\":\"a&b\",\"expected\":\"a&amp;b\"},{\"input\":\"none\",\"expected\":\"none\"}]}]"));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Null(result.Plugin!.Spec);
        Assert.Equal(new DataAction("converter", "amp"), result.Plugin.CommandData["acme.conv.amp"]);
    }

    [Theory]
    [InlineData("{\"id\":\"c\",\"title\":\"C\",\"rules\":[]}", "no rules")]
    [InlineData("{\"id\":\"c\",\"title\":\"C\",\"rules\":[{\"find\":\"\",\"replace\":\"x\"}]}", "empty find")]
    [InlineData("{\"id\":\"c\",\"title\":\"C\",\"rules\":[{\"find\":\"(\",\"replace\":\"x\",\"regex\":true}]}", "regular expression")]
    [InlineData("{\"id\":\"c\",\"title\":\"C\",\"rules\":[{\"find\":\"a\",\"replace\":\"b\"}],\"samples\":[{\"input\":\"a\",\"expected\":\"a\"}]}", "sample 1")]
    [InlineData("{\"id\":\"c\",\"title\":\"C\",\"rules\":[{\"find\":\"a\",\"replace\":\"b\"}],\"command\":\"acme.conv.none\"}", "unknown command")]
    [InlineData("{\"id\":\"C!\",\"title\":\"C\",\"rules\":[{\"find\":\"a\",\"replace\":\"b\"}]}", "lower case")]
    public void An_invalid_converter_is_refused_when_the_plugin_is_read(string converter, string expected)
    {
        var result = Load(Manifest("\"converters\":[" + converter + "]"));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Two_converters_with_one_id_are_refused()
    {
        var one = "{\"id\":\"c\",\"title\":\"C\",\"rules\":[{\"find\":\"a\",\"replace\":\"b\"}]}";
        var result = Load(Manifest("\"converters\":[" + one + "," + one + "]"));
        Assert.Contains(result.Errors, e => e.Contains("Duplicate converter", StringComparison.Ordinal));
    }

    // ---- recipes: the runner ----

    private static RecipeContribution Recipe(params RecipeStep[] steps) => new() { Id = "r", Title = "R", Steps = [.. steps] };
    private static RecipeStep Columns(int from, int to = 0) => new() { Kind = RecipeStep.Columns, From = from, To = to };
    private static RecipeStep Convert(string id) => new() { Kind = RecipeStep.Convert, Converter = id };

    private static ConverterContribution Upper() =>
        new() { Id = "up", Title = "Up", Rules = [new ConverterRule { Find = @"\w+", Replace = @"\U$&", Regex = true }] };

    private static string Apply(string text, IEnumerable<RecipeChange> changes)
    {
        foreach (var c in changes.OrderByDescending(c => c.Start)) text = text[..c.Start] + c.Text + text[(c.Start + c.Length)..];
        return text;
    }

    [Fact]
    public void Select_a_column_change_it_and_only_that_column_changes()
    {
        const string text = "id=ab;x\nid=cd;y\nid=ef;z";
        var result = RecipeRunner.Run(Recipe(Columns(4, 5), Convert("up")), [Upper()], text, [], Context(), "\n");
        Assert.True(result.Ok);
        Assert.Equal(3, result.Changes.Count);
        Assert.Equal("id=AB;x\nid=CD;y\nid=EF;z", Apply(text, result.Changes));
        Assert.Equal([(3, 2), (11, 2), (19, 2)], result.Changes.Select(c => (c.Start, c.Length)));
    }

    [Fact]
    public void Columns_skip_short_lines_count_characters_and_work_with_crlf()
    {
        const string text = "abcdef\r\na\r\n\U0001F600bc\U0001F600d";
        var result = RecipeRunner.Run(Recipe(Columns(2, 3), Convert("up")), [Upper()], text, [], Context(), "\r\n");
        Assert.Equal("aBCdef\r\na\r\n\U0001F600BC\U0001F600d", Apply(text, result.Changes));
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public void The_recipe_works_inside_the_selection_only_and_a_selection_ending_at_a_line_start_does_not_touch_that_line()
    {
        const string text = "aa\nbb\ncc\n";
        var result = RecipeRunner.Run(Recipe(Columns(1), Convert("up")), [Upper()], text, [(0, 6)], Context(), "\n");
        Assert.Equal("AA\nBB\ncc\n", Apply(text, result.Changes));
    }

    [Fact]
    public void Matches_narrow_the_parts_and_replace_changes_only_what_is_left()
    {
        const string text = "x=1, y=22, z=3";
        var recipe = Recipe(
            new RecipeStep { Kind = RecipeStep.Matches, Find = @"\d+", Regex = true },
            new RecipeStep { Kind = RecipeStep.Replace, Find = "2", Replacement = "two" });
        var result = RecipeRunner.Run(recipe, [], text, [], Context(), "\n");
        Assert.Equal("x=1, y=twotwo, z=3", Apply(text, result.Changes));
        Assert.Single(result.Changes);
    }

    [Fact]
    public void A_set_step_resolves_variables_exactly_as_a_clip_does_with_the_selection_of_each_part()
    {
        const string template = "${author}:${selection}:${date}:$$";
        var context = Context(("author", "Ann"));
        var recipe = Recipe(new RecipeStep { Kind = RecipeStep.Matches, Find = "item", Regex = false }, new RecipeStep { Kind = RecipeStep.Set, Text = template });
        var result = RecipeRunner.Run(recipe, [], "an item here", [], context, "\n");

        var asClip = Variables.Expand(template, new VariableScope(context.WithSelection("item"))).Text;
        Assert.Equal("Ann:item:2026-10-27:$", asClip);
        Assert.Equal(asClip, Assert.Single(result.Changes).Text);
    }

    [Fact]
    public void A_user_variable_beats_a_built_in_one_in_a_recipe_as_it_does_in_a_clip()
    {
        var recipe = Recipe(new RecipeStep { Kind = RecipeStep.Set, Text = "${date}" });
        var result = RecipeRunner.Run(recipe, [], "x", [], Context(("date", "my date")), "\n");
        Assert.Equal("my date", Assert.Single(result.Changes).Text);
    }

    [Fact]
    public void A_variable_without_a_value_stops_the_recipe_and_nothing_is_changed()
    {
        var recipe = Recipe(new RecipeStep { Kind = RecipeStep.Set, Text = "${file.name}" });
        var result = RecipeRunner.Run(recipe, [], "x", [], Context(), "\n");
        Assert.False(result.Ok);
        Assert.Contains("file.name", result.Problem);
        Assert.Empty(result.Changes);
    }

    [Fact]
    public void A_part_that_stays_the_same_is_not_a_change_and_the_new_text_uses_the_given_line_break()
    {
        var recipe = Recipe(new RecipeStep { Kind = RecipeStep.Set, Text = "a\nb" });
        var result = RecipeRunner.Run(recipe, [], "a\r\nb", [], Context(), "\r\n");
        Assert.Empty(result.Changes);
        Assert.Equal("a\r\nb", RecipeRunner.Run(recipe, [], "z", [], Context(), "\r\n").Changes.Single().Text);
    }

    // ---- recipes: install time ----

    [Fact]
    public void A_recipe_loads_with_its_converter_and_its_command_is_a_recipe_action()
    {
        var result = Load(Manifest("\"commands\":[{\"id\":\"acme.conv.col\",\"title\":\"Col\"}]," +
            "\"converters\":[{\"id\":\"up\",\"title\":\"Up\",\"rules\":[{\"find\":\"a\",\"replace\":\"A\"}]}]," +
            "\"recipes\":[{\"id\":\"col\",\"title\":\"Col\",\"command\":\"acme.conv.col\",\"steps\":[{\"kind\":\"columns\",\"from\":3,\"to\":5},{\"kind\":\"convert\",\"converter\":\"up\"}]}]"));
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(new DataAction("recipe", "col"), result.Plugin!.CommandData["acme.conv.col"]);
    }

    [Theory]
    [InlineData("[]", "no steps")]
    [InlineData("[{\"kind\":\"banana\"}]", "unknown kind")]
    [InlineData("[{\"kind\":\"columns\",\"from\":0},{\"kind\":\"set\",\"text\":\"x\"}]", "from")]
    [InlineData("[{\"kind\":\"columns\",\"from\":5,\"to\":2},{\"kind\":\"set\",\"text\":\"x\"}]", "to must be")]
    [InlineData("[{\"kind\":\"matches\",\"find\":\"(\",\"regex\":true},{\"kind\":\"set\",\"text\":\"x\"}]", "regular expression")]
    [InlineData("[{\"kind\":\"matches\",\"find\":\"\"},{\"kind\":\"set\",\"text\":\"x\"}]", "empty find")]
    [InlineData("[{\"kind\":\"convert\",\"converter\":\"nope\"}]", "unknown converter")]
    [InlineData("[{\"kind\":\"set\",\"text\":\"${oops\"}]", "unclosed")]
    [InlineData("[{\"kind\":\"set\",\"text\":\"x\"},{\"kind\":\"columns\",\"from\":1}]", "selects after")]
    [InlineData("[{\"kind\":\"columns\",\"from\":1}]", "never changes")]
    public void An_invalid_recipe_is_refused_when_the_plugin_is_read(string steps, string expected)
    {
        var result = Load(Manifest("\"recipes\":[{\"id\":\"r\",\"title\":\"R\",\"steps\":" + steps + "}]"));
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_manifest_round_trips_through_json_with_converter_samples_and_recipe_steps()
    {
        var manifest = new PluginManifest { Id = "acme.conv", Name = "C", Version = "1.0.0" };
        manifest.Contributes.Converters.Add(new ConverterContribution { Id = "c", Title = "C", Rules = [new ConverterRule { Find = "a", Replace = "b", IgnoreCase = true }], Samples = [new ConverterSample { Input = "a", Expected = "b" }] });
        manifest.Contributes.Recipes.Add(new RecipeContribution { Id = "r", Title = "R", Steps = [new RecipeStep { Kind = RecipeStep.Set, Text = "x" }] });
        var json = JsonSerializer.Serialize(manifest, PluginManifestJson.Default.PluginManifest);
        var back = JsonSerializer.Deserialize(json, PluginManifestJson.Default.PluginManifest)!;
        Assert.True(back.Contributes.Converters[0].Rules[0].IgnoreCase);
        Assert.Equal("b", back.Contributes.Converters[0].Samples[0].Expected);
        Assert.Equal("set", back.Contributes.Recipes[0].Steps[0].Kind);
    }
}
