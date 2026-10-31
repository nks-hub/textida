using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Tests;

/// <summary>P6.3.1: an MCP client is known by its name, a new one has no permission until the user decides, and a decision holds after a restart and can be taken back for one client.</summary>
public class McpClientPermissionTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private string File_ => Path.Combine(_temp.Path, "mcp-clients.json");

    [Fact]
    public void A_new_client_is_refused_and_the_user_is_asked_once_until_the_user_decides()
    {
        var clients = new McpClients(File_);
        var asked = new List<string>();
        clients.Asked += asked.Add;

        Assert.False(clients.IsAllowed("agent"));
        Assert.False(clients.IsAllowed("agent")); // the second request of the same client does not ask again
        Assert.False(clients.IsAllowed("other"));
        Assert.Equal(["agent", "other"], asked);

        clients.Decide("agent", true);
        Assert.True(clients.IsAllowed("agent"));
        Assert.False(clients.IsAllowed("other"));
    }

    [Fact]
    public void A_decision_survives_a_restart_and_one_client_can_be_revoked_or_forgotten_without_touching_the_others()
    {
        var first = new McpClients(File_);
        first.Decide("a", true);
        first.Decide("b", true);

        var second = new McpClients(File_);
        Assert.True(second.IsAllowed("a"));
        Assert.True(second.IsAllowed("b"));

        second.Decide("a", false); // revoked
        Assert.False(second.IsAllowed("a"));
        Assert.True(second.IsAllowed("b"));

        var asked = new List<string>();
        var third = new McpClients(File_);
        third.Asked += asked.Add;
        Assert.False(third.IsAllowed("a")); // still denied after a restart: a denial is a decision too
        Assert.Empty(asked);
        third.Forget("a");
        Assert.False(third.IsAllowed("a"));
        Assert.Equal(["a"], asked); // forgotten: the user is asked again
        Assert.True(third.IsAllowed("b"));
    }

    [Fact]
    public void A_damaged_file_allows_nobody_and_asks_again()
    {
        File.WriteAllText(File_, "{ not json");
        var clients = new McpClients(File_);
        var asked = new List<string>();
        clients.Asked += asked.Add;
        Assert.False(clients.IsAllowed("agent"));
        Assert.Equal(["agent"], asked);
    }
}
