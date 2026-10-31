using NksHub.Textida.Workbench;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.3.2: the local history keeps a bounded amount by default (the newest and the pinned revisions are kept whatever the limits say).</summary>
public class HistoryDefaultsTests
{
    [Theory]
    [InlineData("history.maxRevisionsPerFile", 100)]
    [InlineData("history.maxAgeDays", 90)]
    [InlineData("history.maxMiBPerFile", 50)]
    [InlineData("history.maxTotalMiB", 2048)]
    public void The_retention_limits_start_at_a_sensible_value_not_at_no_limit(string key, int expected)
    {
        var definition = WorkbenchHost.CreateBuiltin().Settings.Definitions.Single(d => d.Key == key);
        Assert.Equal(expected, definition.Default is System.Text.Json.Nodes.JsonValue value ? value.GetValue<int>() : Convert.ToInt32(definition.Default));
    }
}
