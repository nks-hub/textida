using NksHub.Textida.Core.Conversions;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P4.2.2: the list of special conversions is closed. A conversion added to <see cref="SpecialConversions.All"/> fails the gate until its id is in
/// <see cref="ClosedList"/> and it has rows in <see cref="Cases"/> with an input and the expected output (null when the conversion must refuse the input).
/// </summary>
public class SpecialConversionsTests
{
    private static readonly string[] ClosedList =
    [
        "decToHex", "hexToDec", "urlEncode", "urlDecode", "quotedPrintableEncode", "quotedPrintableDecode", "base64Encode", "base64Decode", "removeAccents", "tabsToSpaces",
    ];

    private static readonly (string Id, string Input, string? Expected)[] Cases =
    [
        ("decToHex", "255", "FF"),
        ("decToHex", "99", "63"),
        ("decToHex", " 2766\n", " ACE\n"),
        ("decToHex", "18446744073709551615", "FFFFFFFFFFFFFFFF"),
        ("decToHex", "18446744073709551616", null),
        ("decToHex", "cat", null),
        ("decToHex", "-1", null),
        ("decToHex", "112 113", null),
        ("decToHex", " ", null),
        ("hexToDec", "ACE", "2766"),
        ("hexToDec", "0xff", "255"),
        ("hexToDec", "FFFFFFFFFFFFFFFF", "18446744073709551615"),
        ("hexToDec", "10000000000000000", null),
        ("hexToDec", "AXE", null),
        ("hexToDec", "0x", null),
        ("urlEncode", "a b/č\r\n", "a%20b%2F%C4%8D%0D%0A"),
        ("urlEncode", "safe-_.~", "safe-_.~"),
        ("urlDecode", "a%20b%2F%C4%8D%0D%0A", "a b/č\r\n"),
        ("urlDecode", "100% %zz", "100% %zz"),
        ("quotedPrintableEncode", "č = 1\r\n", "=C4=8D =3D 1=0D=0A"),
        ("quotedPrintableEncode", "tab\there", "tab=09here"),
        ("quotedPrintableDecode", "=C4=8D =3D 1=0D=0A", "č = 1\r\n"),
        ("quotedPrintableDecode", "soft=\r\nbreak=\nhere", "softbreakhere"),
        ("quotedPrintableDecode", "=c4=8d", "č"),
        ("quotedPrintableDecode", "=G1", null),
        ("quotedPrintableDecode", "=C4", null),
        ("quotedPrintableDecode", "end=", null),
        ("base64Encode", "Ahoj", "QWhvag=="),
        ("base64Encode", "č", "xI0="),
        ("base64Decode", "QWhvag==", "Ahoj"),
        ("base64Decode", " xI0=\n", " č\n"),
        ("base64Decode", "QWhv\r\nag==", "Ahoj"),
        ("base64Decode", "QWhvag=", null),
        ("base64Decode", "/w==", null),
        ("removeAccents", "Příliš žluťoučký kůň úpěl ďábelské ódy", "Prilis zlutoucky kun upel dabelske ody"),
        ("removeAccents", "Ærø ß ł", "Ærø ß ł"),
        ("removeAccents", "नमस्ते", "नमस्ते"),
        ("tabsToSpaces", "a\tb", "a   b"),
        ("tabsToSpaces", "abcd\te", "abcd    e"),
        ("tabsToSpaces", "\tx\r\n\ty", "    x\r\n    y"),
    ];

    private static readonly ConversionContext AtLineStart = new(4, 0);

    internal static List<string> Gate(IReadOnlyList<SpecialConversion> all, IReadOnlyCollection<string> closed, IReadOnlyList<(string Id, string Input, string? Expected)> cases)
    {
        var problems = new List<string>();
        foreach (var id in all.Select(c => c.Id).GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key)) problems.Add($"{id}: id used twice");
        foreach (var id in all.Select(c => c.Id).Except(closed)) problems.Add($"{id}: not in the closed list");
        foreach (var id in closed.Except(all.Select(c => c.Id))) problems.Add($"{id}: in the closed list but not offered");
        foreach (var c in all)
        {
            if (string.IsNullOrWhiteSpace(c.TitleKey)) problems.Add($"{c.Id}: no title key");
            // the sample is what the list previews and what the window test converts and undoes, so it must convert to another text
            if (c.Sample is null || c.Convert(c.Sample, AtLineStart) is not { } converted || converted == c.Sample) problems.Add($"{c.Id}: no sample that converts");
            var mine = cases.Where(x => x.Id == c.Id).ToList();
            if (mine.Count == 0) problems.Add($"{c.Id}: no test case");
            foreach (var (_, input, expected) in mine)
                if (c.Convert(input, AtLineStart) is var actual && actual != expected) problems.Add($"{c.Id}: '{input}' gives '{actual ?? "refused"}', expected '{expected ?? "refused"}'");
        }
        foreach (var id in cases.Select(x => x.Id).Except(all.Select(c => c.Id))) problems.Add($"{id}: a case for a conversion that does not exist");
        return problems;
    }

    [Fact]
    public void The_shipped_list_equals_the_closed_list_and_every_conversion_has_a_passing_case()
    {
        var problems = Gate(SpecialConversions.All, ClosedList, Cases);
        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void The_gate_stops_a_new_conversion_without_a_case_outside_the_list_or_with_a_wrong_result()
    {
        var up = new SpecialConversion("up", "k", (s, _) => s.ToUpperInvariant(), "a");
        Assert.Empty(Gate([up], ["up"], [("up", "a", "A")]));
        Assert.Contains("no test case", Assert.Single(Gate([up], ["up"], [])));
        Assert.Contains(Gate([up], [], [("up", "a", "A")]), p => p.Contains("not in the closed list"));
        Assert.Contains(Gate([up], ["up"], [("up", "a", "a")]), p => p.Contains("gives 'A'"));
        Assert.Contains(Gate([up], ["up"], [("up", "a", null)]), p => p.Contains("expected 'refused'"));
        Assert.Contains(Gate([up, up], ["up"], [("up", "a", "A")]), p => p.Contains("twice"));
        Assert.Contains(Gate([], ["up"], []), p => p.Contains("not offered"));
        Assert.Contains(Gate([], [], [("up", "a", "A")]), p => p.Contains("does not exist"));
        Assert.Contains("no sample", Assert.Single(Gate([up with { Sample = null }], ["up"], [("up", "a", "A")])));
        Assert.Contains("no sample", Assert.Single(Gate([up with { Sample = "A" }], ["up"], [("up", "a", "A")])));
    }

    [Fact]
    public void Every_conversion_has_its_own_command()
    {
        Assert.All(SpecialConversions.All, c => Assert.Equal("nks.edit.convert." + c.Id, c.CommandId));
        Assert.Equal(SpecialConversions.All.Count, SpecialConversions.All.Select(c => c.CommandId).Distinct().Count());
    }

    [Theory]
    [InlineData(4, 0, "\tb", "    b")]
    [InlineData(4, 2, "\tb", "  b")]
    [InlineData(4, 4, "\tb", "    b")]
    [InlineData(8, 3, "x\ty\n\tz", "x    y\n        z")]
    [InlineData(0, 0, "\t", " ")]
    public void Tabs_to_spaces_counts_tab_stops_from_the_column_the_text_starts_at(int tabSize, int column, string input, string expected)
    {
        var tabs = SpecialConversions.All.Single(c => c.Id == "tabsToSpaces");
        Assert.Equal(expected, tabs.Convert(input, new ConversionContext(tabSize, column)));
    }
}
