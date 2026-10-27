using NksHub.Textida.Core.Conversions;

namespace NksHub.Textida.Core.Tests;

/// <summary>
/// P4.2.2: the list of special conversions is closed. The ids are the owner's decision; none is written down yet, so both the closed list and the table of
/// cases are empty and the gate passes. The day an id is added to <see cref="SpecialConversions.All"/>, the gate fails until the id is in
/// <see cref="ClosedList"/> and has a row in <see cref="Cases"/> with an input and the expected output.
/// </summary>
public class SpecialConversionsTests
{
    private static readonly string[] ClosedList = [];

    private static readonly (string Id, string Input, string Expected)[] Cases = [];

    internal static List<string> Gate(IReadOnlyList<SpecialConversion> all, IReadOnlyCollection<string> closed, IReadOnlyList<(string Id, string Input, string Expected)> cases)
    {
        var problems = new List<string>();
        foreach (var id in all.Select(c => c.Id).GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key)) problems.Add($"{id}: id used twice");
        foreach (var id in all.Select(c => c.Id).Except(closed)) problems.Add($"{id}: not in the closed list");
        foreach (var id in closed.Except(all.Select(c => c.Id))) problems.Add($"{id}: in the closed list but not offered");
        foreach (var c in all)
        {
            if (string.IsNullOrWhiteSpace(c.TitleKey)) problems.Add($"{c.Id}: no title key");
            var mine = cases.Where(x => x.Id == c.Id).ToList();
            if (mine.Count == 0) problems.Add($"{c.Id}: no test case");
            foreach (var (_, input, expected) in mine)
                if (c.Convert(input) != expected) problems.Add($"{c.Id}: '{input}' gives '{c.Convert(input)}', expected '{expected}'");
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
        var up = new SpecialConversion("up", "k", s => s.ToUpperInvariant());
        Assert.Empty(Gate([up], ["up"], [("up", "a", "A")]));
        Assert.Contains("no test case", Assert.Single(Gate([up], ["up"], [])));
        Assert.Contains(Gate([up], [], [("up", "a", "A")]), p => p.Contains("not in the closed list"));
        Assert.Contains(Gate([up], ["up"], [("up", "a", "a")]), p => p.Contains("gives 'A'"));
        Assert.Contains(Gate([up, up], ["up"], [("up", "a", "A")]), p => p.Contains("twice"));
        Assert.Contains(Gate([], ["up"], []), p => p.Contains("not offered"));
        Assert.Contains(Gate([], [], [("up", "a", "A")]), p => p.Contains("does not exist"));
    }
}
