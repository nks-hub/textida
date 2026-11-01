using Avalonia.Headless.XUnit;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.6.6: the list of what an agent loads and the plans source of Go to Anything.</summary>
[Collection("LocState")]
public class AgentFilesAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public AgentFilesAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public void The_list_shows_the_chain_of_the_active_file_with_a_sum_and_opens_a_file_on_enter()
    {
        _temp.File("AGENTS.md", new string('x', 800));
        _temp.File("CLAUDE.md", "rules");
        var w = _app.CreateWindow(null);
        w.AgentHomeFolder = Path.Combine(_temp.Path, "home");
        w.OpenFile(_temp.File("code.cs", "class A;"), null);

        w.ShowAgentFiles();

        Assert.True(w.IsPaletteOpen);
        var rows = w.QuickResults;
        Assert.Equal(3, rows.Count); // the sum and two files
        Assert.False(rows[0].Enabled);
        Assert.Contains("2", rows[0].Title);
        Assert.Equal(["AGENTS.md", "CLAUDE.md"], rows.Skip(1).Select(r => r.Title));
        Assert.Contains("200", rows[1].Detail!); // 800 characters, about 200 tokens
        rows[2].Run();
        Assert.EndsWith("CLAUDE.md", w.Active!.Path);
    }

    [AvaloniaFact]
    public void A_plus_in_go_to_anything_lists_the_plans_of_agents()
    {
        Directory.CreateDirectory(Path.Combine(_temp.Path, ".claude", "plans"));
        Directory.CreateDirectory(Path.Combine(_temp.Path, "docs", "plans"));
        _temp.File(".claude/plans/refactor.md", "# plan");
        _temp.File("docs/plans/release.md", "# plan");
        var w = _app.CreateWindow(null);
        w.AgentHomeFolder = Path.Combine(_temp.Path, "home");
        w.OpenFile(_temp.File("a.txt", "x"), null);

        w.ShowQuickOpen("+");

        Assert.Equal(["refactor.md", "release.md"], w.QuickResults.Select(r => r.Title).Order());
        w.SetPaletteQuery("+release");
        Assert.Equal(["release.md"], w.QuickResults.Select(r => r.Title));
    }
}
