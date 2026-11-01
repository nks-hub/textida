using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

public class InterpreterSessionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "textida-interp-" + Guid.NewGuid().ToString("N")[..8]);

    public InterpreterSessionTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private static async Task<bool> Until(Func<bool> done, int ms = 15000)
    {
        for (var clock = System.Diagnostics.Stopwatch.StartNew(); clock.ElapsedMilliseconds < ms; await Task.Delay(20))
            if (done()) return true;
        return false;
    }

    [Fact]
    public void The_interpreter_of_a_language_is_chosen_by_its_mode()
    {
        Assert.Same(InterpreterKind.Python, InterpreterKind.ForLanguage("python"));
        Assert.Same(InterpreterKind.Node, InterpreterKind.ForLanguage("javascript"));
        Assert.Same(InterpreterKind.Php, InterpreterKind.ForLanguage("php"));
        Assert.Same(InterpreterKind.Psql, InterpreterKind.ForLanguage("sql"));
        Assert.Null(InterpreterKind.ForLanguage("csharp"));
    }

    [Fact]
    public void A_python_in_a_venv_of_the_folder_is_the_one_that_is_used()
    {
        string bin = Path.Combine(_folder, ".venv", OperatingSystem.IsWindows() ? "Scripts" : "bin");
        Directory.CreateDirectory(bin);
        string python = Path.Combine(bin, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.WriteAllText(python, "");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(python, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string sub = Path.Combine(_folder, "src", "pkg");
        Directory.CreateDirectory(sub);

        Assert.Equal(python, InterpreterKind.Python.Find(_folder), ignoreCase: true);
        Assert.Equal(python, InterpreterKind.Python.Find(sub), ignoreCase: true); // a folder below the one that has the .venv finds it too
    }

    [Fact]
    public async Task Text_sent_to_python_runs_in_one_session_that_keeps_its_state_and_ends_with_it()
    {
        if (InterpreterKind.Python.Find(null) is null) return; // no python on this machine: nothing to run
        var lines = new List<string>();
        using var session = InterpreterSession.Start(InterpreterKind.Python, _folder, l => { lock (lines) lines.Add(l); })!;
        Assert.NotNull(session);

        session.Send("x = 40");
        session.Send("print(x + 2)");
        Assert.True(await Until(() => { lock (lines) return lines.Contains("42"); }), string.Join("|", lines));

        session.Send("def f(n):\n    return n * 2");
        session.Send("print(f(x))");
        Assert.True(await Until(() => { lock (lines) return lines.Contains("80"); }), string.Join("|", lines)); // a block that ends indented is closed for the interpreter

        session.Send("print(1/0)");
        Assert.True(await Until(() => { lock (lines) return lines.Any(l => l.Contains("ZeroDivisionError")); }), string.Join("|", lines)); // errors come in the same output

        int? exit = null;
        session.Exited += code => exit = code;
        session.Send("raise SystemExit(3)");
        Assert.True(await Until(() => exit is not null));
        Assert.Equal(3, exit);
        Assert.False(session.Running);
        Assert.Throws<InvalidOperationException>(() => session.Send("print(1)"));
        lock (lines) Assert.DoesNotContain(lines, l => l.Contains(">>>")); // prompts are not output
    }

    [Fact]
    public void A_program_that_is_not_there_gives_no_session()
    {
        var nothing = new InterpreterKind("none", "None", ["definitely-not-an-interpreter-xyz"], []);
        Assert.Null(InterpreterSession.Start(nothing, _folder, _ => { }));
    }
}
