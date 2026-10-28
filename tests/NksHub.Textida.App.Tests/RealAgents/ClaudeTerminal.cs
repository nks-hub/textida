using System.Text.RegularExpressions;

namespace NksHub.Textida.App.Tests.RealAgents;

/// <summary>
/// The interactive Claude Code on a pseudo console, as in a terminal window next to the editor (the <c>/ide</c> connection is made by the interactive program only). The user's settings,
/// hooks and servers are left out (<c>--setting-sources project</c>, <c>--strict-mcp-config</c>); the login is the one of the <c>CLAUDE_CONFIG_DIR</c> this process has.
/// </summary>
internal sealed class ClaudeTerminal : IDisposable
{
    private readonly PtyProcess _pty;
    private bool _trustAnswered;

    private ClaudeTerminal(PtyProcess pty, string debugLog)
    {
        _pty = pty;
        DebugLog = debugLog;
    }

    /// <summary>The debug log of the program: what it did with the MCP server <c>ide</c> is in it.</summary>
    public string DebugLog { get; }

    public string Screen => _pty.Text;

    public bool HasExited => _pty.HasExited;

    /// <summary>
    /// Starts the program in <paramref name="folder"/> with the permission mode of the question for every edit and a cheap model. <c>--ide</c> makes it connect to the one editor whose
    /// lock file lists the folder.
    /// </summary>
    public static ClaudeTerminal Start(string claude, string folder, string debugLog)
    {
        var pty = PtyProcess.Start($"\"{claude}\" --ide --setting-sources project --strict-mcp-config --permission-mode default --model haiku --debug-file \"{debugLog}\"", folder, RealTools.AgentEnvironment());
        return new ClaudeTerminal(pty, debugLog);
    }

    /// <summary>Answers the question about trusting the folder with "Yes" once (the answer is remembered by the program for the folder). Call it while waiting; true when it answered.</summary>
    public bool AnswerTrustQuestion()
    {
        if (_trustAnswered || !Screen.Contains("Yes, I trust this folder", StringComparison.Ordinal)) return false;
        _trustAnswered = true;
        Thread.Sleep(1500); // the dialog takes keys only after it has drawn itself
        _pty.Type("\u001b[B"); // the dialog starts on "No, exit"
        Thread.Sleep(500);
        _pty.Enter();
        return true;
    }

    /// <summary>True when the input line is there and the program is not working: the text it draws at the bottom.</summary>
    public bool IsWaitingForInput => Screen.Contains("? for shortcuts", StringComparison.Ordinal) && !Screen.TrimEnd().EndsWith("esc to interrupt", StringComparison.Ordinal);

    public void Say(string text)
    {
        _pty.Type(text);
        Thread.Sleep(500); // typed text and Enter in one go would be taken for a paste
        _pty.Enter();
    }

    /// <summary>The debug log the program writes while it runs; empty before the program has made it.</summary>
    public string ReadDebugLog()
    {
        if (!File.Exists(DebugLog)) return "";
        using var stream = new FileStream(DebugLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>The names of the tools of the MCP server <c>ide</c> that the program called, in the order of the debug log.</summary>
    public IReadOnlyList<string> IdeToolCalls() => [.. Regex.Matches(ReadDebugLog(), "MCP server \"ide\": Calling MCP tool: (\\S+)").Select(m => m.Groups[1].Value)];

    /// <summary>The lines of the debug log that tell how the connection to the editor went.</summary>
    public IReadOnlyList<string> IdeConnectionLines() =>
        [.. ReadDebugLog().Split('\n').Where(l => l.Contains("MCP server \"ide\"", StringComparison.Ordinal) && !l.Contains("Calling MCP tool", StringComparison.Ordinal)).Select(l => l.Trim())];

    public void Dispose() => _pty.Dispose();
}
