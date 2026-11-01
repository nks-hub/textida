using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Tools;

/// <summary>The interpreters text can be sent to: the program that is looked for, its arguments for an interactive session and the name shown for it.</summary>
public sealed record InterpreterKind(string Id, string Title, string[] Programs, string[] Arguments)
{
    public static readonly InterpreterKind Python = new("python", "Python", ["python", "python3", "py"], ["-u", "-i", "-q", "-c", "import sys; sys.ps1 = ''; sys.ps2 = ''"]);
    public static readonly InterpreterKind Php = new("php", "PHP", ["php"], ["-a"]);
    public static readonly InterpreterKind Node = new("node", "Node.js", ["node"], ["-i"]);
    public static readonly InterpreterKind Psql = new("psql", "psql", ["psql"], ["-q"]);

    public static IReadOnlyList<InterpreterKind> All { get; } = [Python, Php, Node, Psql];

    /// <summary>The interpreter for a language mode of the editor (<c>python</c>, <c>php</c>, <c>javascript</c>, <c>sql</c>); null when there is none.</summary>
    public static InterpreterKind? ForLanguage(string languageId) => languageId switch
    {
        "python" => Python,
        "php" => Php,
        "javascript" or "js" => Node,
        "sql" => Psql,
        _ => null,
    };

    /// <summary>
    /// The full path of the program: the one in a <c>.venv</c> or <c>node_modules/.bin</c> of <paramref name="folder"/> or a folder above it, else the one on the PATH; null when there is none.
    /// </summary>
    public string? Find(string? folder)
    {
        foreach (var program in Programs)
            if (ToolLocator.Find(program, folder) is { } path && !IsStoreStub(path)) return path;
        return null;
    }

    /// <summary>The <c>python.exe</c> in the WindowsApps folder opens the Microsoft Store instead of Python when none is installed.</summary>
    private static bool IsStoreStub(string path) => OperatingSystem.IsWindows() && path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// An interpreter that runs for as long as the user keeps sending text to it (PZ.6.2): what is written to its input goes to a process started in an interactive mode, what the process prints (output and errors, line by line)
/// is handed to <c>onOutput</c>. A prompt that has no line break (<c>&gt;&gt;&gt; </c>, <c>&gt; </c>, <c>php &gt; </c>) is not output and is dropped; any other text without a line break is handed over after a short quiet.
/// </summary>
public sealed partial class InterpreterSession : IDisposable
{
    [GeneratedRegex(@"^(?:>>> |\.\.\. |> |\.\.\. |php > |\w+[=\-(]+[#>] |\.{3}\s*)$")]
    private static partial Regex PromptPattern();

    private readonly Process _process;
    private readonly Action<string> _onOutput;
    private int _stopped;

    private InterpreterSession(Process process, Action<string> onOutput)
    {
        _process = process;
        _onOutput = onOutput;
    }

    /// <summary>Whether the process still runs.</summary>
    public bool Running => Volatile.Read(ref _stopped) == 0 && !_process.HasExited;

    /// <summary>Raised once when the process has ended, with its exit code.</summary>
    public event Action<int>? Exited;

    /// <summary>
    /// Starts the interpreter in <paramref name="workingDirectory"/>; null when the program is not found. Throws <see cref="InvalidOperationException"/> when it cannot be started.
    /// A program found in a <c>.venv</c> is the one the user means: <c>VIRTUAL_ENV</c> is set and its folder goes first in <c>PATH</c>.
    /// </summary>
    public static InterpreterSession? Start(InterpreterKind kind, string workingDirectory, Action<string> onOutput)
    {
        if (kind.Find(workingDirectory) is not { } program) return null;
        var info = ProcessLaunch.CreateStartInfo(program, kind.Arguments);
        (info.RedirectStandardInput, info.RedirectStandardOutput, info.RedirectStandardError, info.UseShellExecute, info.CreateNoWindow, info.WorkingDirectory) = (true, true, true, false, true, workingDirectory);
        info.StandardOutputEncoding = info.StandardErrorEncoding = Encoding.UTF8;
        info.StandardInputEncoding = new UTF8Encoding(false);
        info.Environment["PYTHONIOENCODING"] = "utf-8";
        info.Environment["PYTHONUNBUFFERED"] = "1";
        string? binFolder = Path.GetDirectoryName(program);
        if (binFolder is not null && (binFolder.Contains(Path.DirectorySeparatorChar + ".venv" + Path.DirectorySeparatorChar) || binFolder.EndsWith(Path.DirectorySeparatorChar + ".venv", StringComparison.Ordinal)
            || binFolder.Contains(Path.DirectorySeparatorChar + ".venv" + Path.AltDirectorySeparatorChar)))
        {
            info.Environment["VIRTUAL_ENV"] = Path.GetDirectoryName(binFolder)!;
            info.Environment["PATH"] = binFolder + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }
        Process process;
        try { process = Process.Start(info) ?? throw new InvalidOperationException("The process did not start."); }
        catch (Exception e) when (e is Win32Exception or IOException) { throw new InvalidOperationException(e.Message, e); }
        ProcessLaunch.Track(process);
        var session = new InterpreterSession(process, onOutput);
        _ = session.PumpAsync(process.StandardOutput);
        _ = session.PumpAsync(process.StandardError);
        _ = Task.Run(async () =>
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false); // what the pumps still have is handed over first
            session.Exited?.Invoke(process.ExitCode);
        });
        return session;
    }

    /// <summary>Writes <paramref name="text"/> and a line break to the input of the interpreter; a block that ends indented gets a blank line, which is what ends a block in an interactive session.</summary>
    public void Send(string text)
    {
        if (!Running) throw new InvalidOperationException("The interpreter has ended.");
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n');
        var input = _process.StandardInput;
        input.Write(normalized);
        input.Write('\n');
        string lastLine = normalized[(normalized.LastIndexOf('\n') + 1)..];
        if (normalized.Contains('\n') && lastLine.Length > 0 && char.IsWhiteSpace(lastLine[0])) input.Write('\n'); // the block ends indented: a blank line ends it
        input.Flush();
    }

    private async Task PumpAsync(StreamReader reader)
    {
        var line = new StringBuilder();
        var buffer = new char[4096];
        var pending = reader.ReadAsync(buffer, 0, buffer.Length);
        while (true)
        {
            if (line.Length > 0 && !pending.IsCompleted && await Task.WhenAny(pending, Task.Delay(150)).ConfigureAwait(false) != pending)
            {
                Flush(line, final: true); // text without a line break that nothing follows
                continue;
            }
            int read;
            try { read = await pending.ConfigureAwait(false); }
            catch (Exception e) when (e is IOException or ObjectDisposedException) { break; }
            if (read == 0) break;
            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                if (c == '\n') Flush(line, final: false);
                else if (c != '\r') line.Append(c);
            }
            pending = reader.ReadAsync(buffer, 0, buffer.Length);
        }
        Flush(line, final: true);
    }

    private void Flush(StringBuilder line, bool final)
    {
        string text = line.ToString();
        line.Clear();
        if (final && (text.Length == 0 || PromptPattern().IsMatch(text))) return; // a prompt is not output
        _onOutput(text);
    }

    /// <summary>Ends the process and what it started.</summary>
    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        try { _process.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception) { /* it has ended */ }
    }

    public void Dispose()
    {
        Stop();
        _process.Dispose();
    }
}
