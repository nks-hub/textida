using System.Text.RegularExpressions;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Acp;

/// <summary>What the editor does not hold back from the agent's own tools. The agent is a plain process with the rights of the user: no sandbox, no terminal capability, nothing that stops its own tools.</summary>
[Flags]
public enum AcpReach
{
    None = 0,
    Files = 1,
    Programs = 2,
    Network = 4,
    All = Files | Programs | Network,
}

/// <summary>
/// A supported agent and how to start it (P6.6.4). <paramref name="ListedVersion"/> is the version the public ACP registry lists for the agent
/// (cdn.agentclientprotocol.com/registry/v1/latest/registry.json); it says what to install, not what was tested.
/// <paramref name="VerifiedVersion"/> is the version a real run of the scenarios passed with, and stays null until there is such a run: the agent is then "not verified", never "supported".
/// The table holds no keys, tokens or environment: the agent logs in by itself, or reads what the user's own environment gives it.
/// </summary>
/// <param name="Command">The program, looked up in PATH.</param>
/// <param name="Repository">Where the agent is documented and installed from (the repository the registry names); shown when the program is not found.</param>
/// <param name="EditorSetsMode">Whether the editor tells the agent which permission mode to use. It never does: no <c>session/set_mode</c> and no configuration is sent, so the agent keeps its own default.</param>
/// <param name="OwnToolsReach">What the editor does not stop the agent's own tools from doing; shown to the user before the start and in the panel.</param>
/// <param name="VersionArgs">Arguments that make the program print its version and leave; <c>--version</c> when null.</param>
public sealed record AcpAdapter(string Id, string Name, string Command, IReadOnlyList<string> Args, string ListedVersion, string Repository, string? VerifiedVersion = null, bool EditorSetsMode = false,
    AcpReach OwnToolsReach = AcpReach.All, IReadOnlyList<string>? VersionArgs = null)
{
    public bool IsVerified => VerifiedVersion is not null;

    /// <summary>The line the user reads and may copy: the program and its arguments.</summary>
    public string CommandLine => string.Join(' ', new[] { Command }.Concat(Args));

    public IReadOnlyList<string> VersionCheckArgs => VersionArgs ?? ["--version"];
}

/// <summary>
/// The table of supported agents. The agents' own tools can write files without the editor: only <c>fs/write_text_file</c> goes through it.
/// Real runs (Windows, <c>AcpRealAgentsTests</c> in the category <c>RealAgents</c>; the editor sets no mode):
/// <c>claude-agent-acp</c> 0.98.0 and 0.99.0 start, answer, ask before their Read and Write tools (the answer of the user decides; "No" and a closed dialog write nothing), end a running command on cancel,
/// are reported when killed and leave no process behind. <c>codex-acp</c> 2.3.0 does all of that except asking: in its workspace sandbox it asked nothing for its reads and writes.
/// Neither adapter calls <c>fs/read_text_file</c> or <c>fs/write_text_file</c>: they read and write the disk with their own tools, so an unsaved tab is not what they read and their write is not a proposal
/// the user decides in the change review. What the editor does with it: the <c>session/request_permission</c> of an Edit or Write carries the change as a <c>diff</c> in the content of the tool call
/// (<see cref="AcpDiff"/>), which is shown in the review while the dialog waits, and the file the agent writes after an "allow" is taken over when it holds exactly that change.
/// That is why <see cref="AcpAdapter.VerifiedVersion"/> stays empty for both.
/// </summary>
public static class AcpAdapters
{
    public static IReadOnlyList<AcpAdapter> All { get; } =
    [
        new("gemini", "Gemini CLI", "gemini", ["--acp"], "0.66.0", "https://github.com/google-gemini/gemini-cli"),
        new("codex", "Codex", "codex-acp", [], "2.3.0", "https://github.com/agentclientprotocol/codex-acp"),
        new("claude", "Claude", "claude-agent-acp", [], "0.98.0", "https://github.com/agentclientprotocol/claude-agent-acp"),
        new("junie", "Junie", "junie", ["--acp=true"], "3893.5.0", "https://github.com/JetBrains/junie-acp-release"),
        new("copilot", "GitHub Copilot", "copilot", ["--acp"], "1.0.104", "https://github.com/github/copilot-cli"),
        new("opencode", "OpenCode", "opencode", ["acp"], "2.2.0", "https://github.com/anomalyco/opencode"),
    ];

    public static AcpAdapter? Find(string id) => All.FirstOrDefault(a => a.Id == id);
}

/// <summary>Which tool calls an agent reports that the editor can neither check nor undo.</summary>
public static class AcpToolKinds
{
    /// <summary>
    /// <c>execute</c> (the editor offers no terminal), <c>delete</c>, <c>move</c> and <c>fetch</c> have no method the editor serves, so the agent does them by itself.
    /// <c>read</c>, <c>edit</c> and <c>search</c> may go through the editor or not; the editor counts what it served itself.
    /// </summary>
    public static bool RunsOutsideEditor(string? kind) => kind is "execute" or "delete" or "move" or "fetch";
}

/// <summary>The version a program reports, or why it could not be read.</summary>
public sealed record AcpVersionResult(string? Version, string? Problem);

/// <summary>Asks an agent program for its version (P6.6.4). A program that prints none, fails or does not answer in time is a note for the user, never a reason to refuse the start.</summary>
public static partial class AcpVersionProbe
{
    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)*(?:[-+][0-9A-Za-z.\-]+)?")]
    private static partial Regex VersionPattern();

    /// <summary>Runs <paramref name="executable"/> with <paramref name="args"/> (its input closed, ended after <paramref name="timeout"/>) and takes the first version number in its output.</summary>
    public static async Task<AcpVersionResult> ReadAsync(string executable, IReadOnlyList<string> args, string workingDirectory, TimeSpan timeout, CancellationToken ct = default)
    {
        var lines = new List<string>();
        ToolResult result;
        try { result = await ToolRunner.RunAsync(new ToolInvocation("acp.version", executable, args, workingDirectory, timeout, ""), lines.Add, ct).ConfigureAwait(false); }
        catch (ToolException e) { return new AcpVersionResult(null, e.Message); }
        if (result.End == ToolEnd.TimedOut) return new AcpVersionResult(null, $"no answer within {timeout.TotalSeconds:0.#} s");
        foreach (var line in lines)
            if (VersionPattern().Match(line) is { Success: true } m) return new AcpVersionResult(m.Value, null);
        return new AcpVersionResult(null, result.ExitCode != 0 ? $"exit code {result.ExitCode}" : "no version in the output");
    }
}
