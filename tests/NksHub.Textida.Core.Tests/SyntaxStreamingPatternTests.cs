using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.Core.Tests;

public class SyntaxStreamingPatternTests
{
    private static readonly string[] Samples =
    [
        "", "a", "aaab ab abab", "AB aB AbCDe abCde ABCDE", "x\na\r\n", "\0" + "41 A",
        " \t#nullable enable", "[section]\r\n key = 001.2 ; comment", "{ \"name\" : \"escaped\\\"value\" }",
        "const x = /[/a-z\\]]+/gi; return /x/; 0xff 1.4e+3", "`hello ${name} ${ {a: 1} }`",
        "<script type='text/javascript'>var n=1;</script><style>div:hover { color: #aabbcc; }</style>",
        "<!DOCTYPE html><!-- comment --><![CDATA[ x < 1 ]]><item a=\"&quot;\">&amp;</item>",
        "<?php function & f() { $ž = new \\App\\Type(); } ?>", "'a' '\\u0041' 'x' \"\"\"raw\"\"\" @\"a\"\"b\"",
        " - services: { factory: Thing(), class: \\App\\Type, enabled: true } # note",
        " - \"quoted key\": value", "#!/usr/bin/env bash", "#!/usr/bin/python3", "export NAME=value # comment",
        "### heading", "``` csharp", "---", "[text](https://example.com) **bold** *emphasis* _em_ __bold__",
        "{if $value}{include file.latte}{$item|escape}{/if}", "-2.5 0x123 1e-2 null TRUE @value 12%",
        "Žluťoučký_123 漢字 \u0301 ٣ İ ı K \u200C \uD800 \uDC00 😀", "[][-]\\\r\n",
    ];

    [Theory]
    [InlineData(".", true)]
    [InlineData("[a-z]", false)]
    [InlineData("[^a-z]", false)]
    [InlineData("[a-z-[aeiou]]", true)]
    [InlineData("[\\p{L}\\p{N}_]", true)]
    [InlineData("[\\x80-\uFFFF]", true)]
    [InlineData("\\w", true)]
    [InlineData("\\W", true)]
    [InlineData("\\d", true)]
    [InlineData("\\s", true)]
    [InlineData("[][-]", true)]
    [InlineData("i", false)]
    public void Character_predicates_match_dotnet_for_every_utf16_code_unit(string pattern, bool caseSensitive)
    {
        var node = Assert.IsType<StreamChar>(StreamPattern.Compile(pattern, caseSensitive));
        var expected = CompileRegex("\\A(?:" + pattern + ")\\z", caseSensitive);
        Span<char> value = stackalloc char[1];
        for (int i = char.MinValue; i <= char.MaxValue; i++)
        {
            value[0] = (char)i;
            Assert.Equal(expected.IsMatch(value), node.Characters.Matches(value[0]));
        }
    }

    [Theory]
    [InlineData(@"(?:a|ab)+?b", true)]
    [InlineData(@"(?:|a)b?", true)]
    [InlineData(@"a{0}b{2,4}?c{1,}d*?", true)]
    [InlineData(@"(?i:ab(?-i:C)d)e", true)]
    [InlineData(@"(?-i:ab(?i:C)d)e", false)]
    [InlineData(@"(?<=^[ \t]*)#[ \t]*[A-Za-z]+", true)]
    [InlineData(@"(?<!\w)\b(?:a|ab)+(?!b)\B", true)]
    [InlineData(@"^a.*?$", true)]
    [InlineData(@"(?:\x0041|\u0041)", true)]
    [InlineData(@"[a-z-[d-f-[e]]]+", true)]
    [InlineData(@"a{word}[][-]", true)]
    [InlineData(@"\\[\]\r\n]+", true)]
    public void Parsed_structure_preserves_dotnet_match_positions_and_lengths(string pattern, bool caseSensitive)
    {
        AssertEquivalent(pattern, caseSensitive, Samples.Concat(["abbbccd", "abbbcd", "aaaaabc", "abcde", "a{word}]"]));
    }

    [Fact]
    public void Every_regex_in_all_builtin_grammars_preserves_its_meaning()
    {
        var assembly = typeof(Grammar).Assembly;
        string[] resources = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("Grammars/", StringComparison.Ordinal) && !n.EndsWith("schema.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(BuiltinGrammars.Registry.All.Count(), resources.Length);
        foreach (string name in resources)
        {
            using var input = assembly.GetManifestResourceStream(name)!;
            using var json = JsonDocument.Parse(input);
            bool caseSensitive = !json.RootElement.TryGetProperty("caseSensitive", out var setting) || setting.GetBoolean();
            var patterns = Patterns(json.RootElement).ToList();
            patterns.Add(Grammar.DefaultWordPattern);
            Assert.NotEmpty(patterns);
            foreach (string pattern in patterns)
                AssertEquivalent(pattern, caseSensitive, Samples, name);
        }
    }

    [Theory]
    [InlineData("(a)")]
    [InlineData(@"(a)\1")]
    [InlineData("(?<name>a)")]
    [InlineData("(?i)a")]
    [InlineData("(?s:.)")]
    [InlineData("(?>a+)")]
    [InlineData("(?(?=a)b|c)")]
    [InlineData(@"\A")]
    [InlineData(@"\z")]
    [InlineData(@"\0")]
    public void Unsupported_constructs_are_rejected_instead_of_changing_their_meaning(string pattern) =>
        Assert.Throws<NotSupportedException>(() => StreamPattern.Compile(pattern, true));

    [Theory]
    [InlineData("[a-")]
    [InlineData("[z-a]")]
    [InlineData("a{3,2}")]
    [InlineData("a**")]
    [InlineData("(")]
    public void Invalid_patterns_keep_dotnet_validation(string pattern) =>
        Assert.Throws<RegexParseException>(() => StreamPattern.Compile(pattern, true));

    [Fact]
    public void Local_ignore_case_does_not_depend_on_the_callers_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            AssertEquivalent("(?i:i)(?-i:I)i", true, ["iIi", "IIi", "İIi", "ıIi", "iII"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static IEnumerable<string> Patterns(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
                if (property.Name is "match" or "begin" or "end" or "wordPattern" or "firstLine")
                    yield return property.Value.GetString()!;
                else
                    foreach (var pattern in Patterns(property.Value)) yield return pattern;
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray())
                foreach (var pattern in Patterns(item)) yield return pattern;
    }

    private static void AssertEquivalent(string pattern, bool caseSensitive, IEnumerable<string> inputs, string? grammar = null)
    {
        var original = CompileRegex(pattern, caseSensitive);
        var parsed = CompileRegex(Render(StreamPattern.Compile(pattern, caseSensitive)), true);
        foreach (string input in inputs)
        {
            var expected = original.Matches(input).Select(m => (m.Index, m.Length)).ToArray();
            var actual = parsed.Matches(input).Select(m => (m.Index, m.Length)).ToArray();
            Assert.True(expected.SequenceEqual(actual), $"{grammar}: {pattern} on {input}");
        }
    }

    private static Regex CompileRegex(string pattern, bool caseSensitive) =>
        new(pattern, RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase), TimeSpan.FromSeconds(1));

    // Rendering the AST lets the independent .NET engine check parser precedence, scopes and match endpoints.
    private static string Render(StreamNode node) => node switch
    {
        StreamChar character => character.Characters.ToString(),
        StreamSequence sequence => string.Concat(sequence.Items.Select(Render)),
        StreamChoice choice => "(?:" + string.Join('|', choice.Items.Select(Render)) + ")",
        StreamRepeat repeat => "(?:" + Render(repeat.Child) + "){" + repeat.Minimum.ToString(CultureInfo.InvariantCulture)
            + (repeat.Maximum == repeat.Minimum ? "" : "," + repeat.Maximum?.ToString(CultureInfo.InvariantCulture)) + "}" + (repeat.Lazy ? "?" : ""),
        StreamLook look => "(?" + (look.Behind ? "<" : "") + (look.Positive ? "=" : "!") + Render(look.Child) + ")",
        StreamAnchor anchor => anchor.Kind switch
        {
            StreamAnchorKind.Start => "^", StreamAnchorKind.End => "$",
            StreamAnchorKind.WordBoundary => @"\b", StreamAnchorKind.NotWordBoundary => @"\B",
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(node)),
    };
}
