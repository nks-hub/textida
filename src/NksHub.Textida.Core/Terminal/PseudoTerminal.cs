using System.Text;

namespace NksHub.Textida.Core.Terminal;

/// <summary>
/// A program on a pseudo terminal: the Windows pseudo console (ConPTY) or a Unix pty. What the program writes arrives in <see cref="OutputReceived"/> (on a thread of its own), what the user types goes to <see cref="Write"/>,
/// <see cref="Resize"/> tells the program the new size, and <see cref="Dispose"/> ends the program and everything it started.
/// </summary>
public interface IPseudoTerminal : IDisposable
{
    int ProcessId { get; }
    bool HasExited { get; }
    int? ExitCode { get; }

    /// <summary>Raised on the reader thread with the bytes of the output; the array is only valid during the call.</summary>
    event Action<ReadOnlyMemory<byte>>? OutputReceived;

    /// <summary>Raised once, on a thread of the pool, with the exit code when the program has ended.</summary>
    event Action<int>? Exited;

    void Write(ReadOnlySpan<byte> data);
    void Resize(int columns, int rows);
}

public static class PseudoTerminal
{
    /// <summary>
    /// Starts <paramref name="program"/> (a full path) with <paramref name="arguments"/> in <paramref name="workingDirectory"/>. The environment is that of this process plus <paramref name="environment"/> and the terminal type
    /// (<c>TERM=xterm-256color</c>, <c>COLORTERM=truecolor</c>). Throws <see cref="IOException"/> when the pseudo terminal or the program cannot be started.
    /// </summary>
    public static IPseudoTerminal Start(string program, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment, int columns, int rows)
    {
        var merged = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) merged[(string)entry.Key] = (string?)entry.Value ?? "";
        merged["TERM"] = "xterm-256color";
        merged["COLORTERM"] = "truecolor";
        if (environment is not null) foreach (var (name, value) in environment) merged[name] = value;
        if (!OperatingSystem.IsWindows() && !merged.ContainsKey("LANG") && !merged.ContainsKey("LC_ALL")) merged["LANG"] = "en_US.UTF-8";
        return OperatingSystem.IsWindows()
            ? ConPtyProcess.Start(program, arguments, workingDirectory, merged, columns, rows)
            : UnixPtyProcess.Start(program, arguments, workingDirectory, merged, columns, rows);
    }

    /// <summary>The program and arguments of the shell the setting names, or the default of the system: PowerShell on Windows, <c>$SHELL</c> (else zsh on macOS, bash elsewhere) on the others. Null when the program is not found.</summary>
    public static (string Program, IReadOnlyList<string> Arguments)? ResolveShell(string? setting, string? startFolder)
    {
        string name = setting?.Trim() ?? "";
        if (name.Length == 0)
        {
            if (OperatingSystem.IsWindows()) name = "powershell.exe";
            else name = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell ? shell : OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
        }
        var parts = SplitCommand(name);
        if (parts.Count == 0) return null;
        string? program = Tools.ToolLocator.Find(parts[0], startFolder);
        return program is null ? null : (program, parts.Skip(1).ToList());
    }

    /// <summary>Splits a command typed in the setting at spaces, keeping a part in double quotes together: <c>"C:\Program Files\Git\bin\bash.exe" --login</c>.</summary>
    public static List<string> SplitCommand(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        foreach (char c in text)
        {
            if (c == '"') { quoted = !quoted; any = true; }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any || current.Length > 0) parts.Add(current.ToString());
                current.Clear();
                any = false;
            }
            else current.Append(c);
        }
        if (any || current.Length > 0) parts.Add(current.ToString());
        return parts;
    }
}
