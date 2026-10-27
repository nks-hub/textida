using NksHub.Textida.Plugins.WebData;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P4.6.4: the key paths of YAML and JSON text, with the place of every key.</summary>
public class WebDataKeyTests
{
    private const string Yaml = """
        # translations
        cs:
          app:
            title: "Aplikace"   # the name
            items:
              - name: A
                tags: [x, y]
              - B
          note: |
            multi
            line: text
          empty:
        'a.b': 1
        other: 1
        """;

    private static string[] Paths(KeyParse parse) => [.. parse.Nodes.Select(n => n.Path)];

    [Fact]
    public void Yaml_gives_nested_mappings_sequences_and_block_scalars_their_paths()
    {
        var parse = YamlKeys.Parse(Yaml);
        Assert.Equal(["cs", "cs.app", "cs.app.title", "cs.app.items", "cs.app.items[0]", "cs.app.items[0].name", "cs.app.items[0].tags", "cs.app.items[1]",
            "cs.note", "cs.empty", "[\"a.b\"]", "other"], Paths(parse));
        Assert.Equal(new[] { "cs.app.title", "cs.app.items[0].name", "cs.app.items[0].tags", "cs.app.items[1]", "cs.note", "cs.empty", "[\"a.b\"]", "other" }.Order(), parse.Leaves().Order());
    }

    [Fact]
    public void The_key_under_the_caret_is_the_innermost_one_and_a_scalar_that_goes_on_belongs_to_its_key()
    {
        var parse = YamlKeys.Parse(Yaml);
        string PathAt(string marker, int shift = 0) => parse.At(Yaml.IndexOf(marker, StringComparison.Ordinal) + shift)?.Path ?? "";
        Assert.Equal("cs.app.title", PathAt("Aplikace", 2));
        Assert.Equal("cs.app.title", PathAt("title"));
        Assert.Equal("cs.app.items[0].name", PathAt("name: A", 6));
        Assert.Equal("cs.app.items[0].tags", PathAt("x, y"));
        Assert.Equal("cs.app.items[1]", PathAt("- B", 2));
        Assert.Equal("cs.note", PathAt("multi", 2));
        Assert.Equal("cs.note", PathAt("line: text", 1)); // a line of the block scalar is not a key
        Assert.Equal("cs.app", PathAt("items:", -3));
        Assert.Equal("other", PathAt("other: 1", 8));
    }

    [Fact]
    public void The_key_range_is_the_key_as_written()
    {
        var parse = YamlKeys.Parse(Yaml);
        string Written(string path) { var n = parse.Nodes.Single(x => x.Path == path); return Yaml[n.KeyStart..n.KeyEnd]; }
        Assert.Equal("title", Written("cs.app.title"));
        Assert.Equal("'a.b'", Written("[\"a.b\"]"));
        Assert.Equal("name", Written("cs.app.items[0].name"));
        Assert.Equal("-", Written("cs.app.items[1]"));
    }

    [Fact]
    public void A_sequence_may_stand_at_the_indentation_of_its_key_and_a_dash_can_carry_a_mapping()
    {
        var parse = YamlKeys.Parse("list:\n- a\n- b: 1\n  c: 2\nnext: x\n---\nagain: 1\n");
        Assert.Equal(["list", "list[0]", "list[1]", "list[1].b", "list[1].c", "next", "again"], Paths(parse));
    }

    [Fact]
    public void Json_gives_objects_and_arrays_their_paths_and_the_quoted_key_as_the_range()
    {
        const string json = "{\"cs\": {\"app\": {\"title\": \"A\", \"list\": [1, {\"x\": true}]}}, \"a.b\": 2, // note\n \"tail\": [],}";
        var parse = JsonKeys.Parse(json);
        Assert.Null(parse.ErrorAt);
        Assert.Equal(["cs", "cs.app", "cs.app.title", "cs.app.list", "cs.app.list[0]", "cs.app.list[1]", "cs.app.list[1].x", "[\"a.b\"]", "tail"], Paths(parse));
        var title = parse.Nodes.Single(n => n.Path == "cs.app.title");
        Assert.Equal("\"title\"", json[title.KeyStart..title.KeyEnd]);
        Assert.Equal("cs.app.title", parse.At(json.IndexOf("\"A\"", StringComparison.Ordinal) + 1)!.Path);
        Assert.Equal("cs.app.list[1].x", parse.At(json.IndexOf("true", StringComparison.Ordinal))!.Path);
        Assert.Equal("cs.app", parse.At(json.IndexOf("}}", StringComparison.Ordinal) + 1)!.Path);
        Assert.DoesNotContain("cs.app.list", parse.Leaves());
        Assert.Contains("tail", parse.Leaves());
    }

    [Fact]
    public void A_syntax_error_keeps_the_keys_before_it_and_says_where()
    {
        var parse = JsonKeys.Parse("{\"a\": 1, \"b\": }");
        Assert.Equal(["a", "b"], Paths(parse));
        Assert.Equal(14, parse.ErrorAt);
        Assert.NotNull(JsonKeys.Parse("{\"a\": [1, 2").ErrorAt);
        Assert.Null(JsonKeys.Parse("").ErrorAt);
    }

    [Fact]
    public void Two_translation_files_differ_by_the_leaf_keys_each_one_lacks()
    {
        var en = YamlKeys.Parse("app:\n  title: T\n  save: S\n  only_en: x\nlist:\n  - a\n");
        var cs = YamlKeys.Parse("app:\n  title: N\n  save: U\n  only_cs: y\nlist:\n  - a\n  - b\n");
        Assert.Equal(["app.only_en"], en.Leaves().Except(cs.Leaves()));
        Assert.Equal(["app.only_cs", "list[1]"], cs.Leaves().Except(en.Leaves()).Order());
    }
}
