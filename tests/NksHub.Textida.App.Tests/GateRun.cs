using Avalonia.Controls;
using Avalonia;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// One walk of the gate: the same screens in the four languages. It collects what is wrong with the controls found and, once every language has been
/// walked, which names did not change from one language to the next, because a name that is the same in all four was never translated.
/// </summary>
internal sealed class GateRun
{
    private readonly string[] _data;
    private readonly IReadOnlySet<string> _keys = A11yAudit.Keys();
    private readonly IReadOnlySet<string> _neutral = A11yAudit.NeutralTexts();
    private readonly Dictionary<(string Scenario, string Language), HashSet<string>> _names = [];
    private readonly Dictionary<string, int> _controls = [];

    /// <param name="data">Texts that come from the test and not from the catalogues: file names, paths.</param>
    public GateRun(params string[] data) => _data = data;

    public List<string> Problems { get; } = [];

    public void Audit(string scenario, string language, Visual root) => Audit(scenario, language, A11yAudit.Collect(root));

    public void Audit(string scenario, string language, IReadOnlyList<A11yAudit.Item> items)
    {
        _controls[scenario] = _controls.GetValueOrDefault(scenario) + items.Count;
        if (!_names.TryGetValue((scenario, language), out var names)) _names[(scenario, language)] = names = [];
        foreach (var item in items)
        {
            foreach (var problem in A11yAudit.Problems(item, _keys, _data)) Problems.Add($"[{scenario}/{language}] {item.Control}: {problem}");
            if (!string.IsNullOrWhiteSpace(item.Name)) names.Add(item.Name);
        }
    }

    public void AuditOrder(string scenario, TopLevel top)
    {
        foreach (var problem in A11yAudit.ReadingOrderProblems(top, A11yAudit.TabSequence(top))) Problems.Add($"[{scenario}] {problem}");
    }

    /// <summary>Every screen was walked in every language, found controls, and no name stayed the same in all four.</summary>
    public void Finish()
    {
        foreach (var scenario in _names.Keys.Select(k => k.Scenario).Distinct())
        {
            if (_controls.GetValueOrDefault(scenario) == 0) Problems.Add($"[{scenario}] no control was found: the walk covers nothing");
            var sets = A11yAudit.Languages.Where(l => _names.ContainsKey((scenario, l))).Select(l => _names[(scenario, l)]).ToList();
            if (sets.Count < A11yAudit.Languages.Length)
            {
                Problems.Add($"[{scenario}] was not walked in all four languages");
                continue;
            }
            foreach (var name in sets[0].Where(n => sets.All(s => s.Contains(n)) && !A11yAudit.IsLanguageNeutral(n, _data, _neutral)))
                Problems.Add($"[{scenario}] the name '{name}' is the same in all four languages, so it was never translated");
        }
    }

    public void AssertClean()
    {
        Finish();
        Assert.True(Problems.Count == 0, $"{Problems.Count} accessibility problems:\n" + string.Join("\n", Problems.Distinct().Take(80)));
    }
}
