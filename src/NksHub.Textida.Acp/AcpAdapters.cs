namespace NksHub.Textida.Acp;

/// <summary>
/// A supported agent and how to start it (P6.6.4). <paramref name="ListedVersion"/> is the version the public ACP registry lists for the agent
/// (cdn.agentclientprotocol.com/registry/v1/latest/registry.json); it says what to install, not what was tested.
/// <paramref name="VerifiedVersion"/> is the version a real run of the scenarios passed with, and stays null until there is such a run: the agent is then "not verified", never "supported".
/// </summary>
/// <param name="Command">The program, looked up in PATH.</param>
/// <param name="EditorSetsMode">Whether the editor tells the agent which permission mode to use. It never does: no <c>session/set_mode</c> and no configuration is sent, so the agent keeps its own default.</param>
public sealed record AcpAdapter(string Id, string Name, string Command, IReadOnlyList<string> Args, string ListedVersion, string? VerifiedVersion = null, bool EditorSetsMode = false)
{
    public bool IsVerified => VerifiedVersion is not null;

    /// <summary>The line the user reads and may copy: the program and its arguments.</summary>
    public string CommandLine => string.Join(' ', new[] { Command }.Concat(Args));
}

/// <summary>The table of supported agents. The agents' own tools can write files without the editor: only <c>fs/write_text_file</c> goes through it.</summary>
public static class AcpAdapters
{
    public static IReadOnlyList<AcpAdapter> All { get; } =
    [
        new("gemini", "Gemini CLI", "gemini", ["--acp"], "0.65.0"),
        new("codex", "Codex", "codex-acp", [], "2.3.0"),
        new("claude", "Claude", "claude-agent-acp", [], "0.97.0"),
        new("junie", "Junie", "junie", ["--acp=true"], "3813.9.0"),
        new("copilot", "GitHub Copilot", "copilot", ["--acp"], "1.0.104"),
        new("opencode", "OpenCode", "opencode", ["acp"], "1.19.4"),
    ];

    public static AcpAdapter? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}
