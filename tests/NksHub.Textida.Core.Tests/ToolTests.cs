using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.Core.Tests;

/// <summary>P4.3.3: the tool launcher builds an argument list, never a command line, and ends what it started.</summary>
public sealed class ToolTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-tool-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private static string EchoDll => Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");

    private ToolSpec Echo(IEnumerable<string> args, TimeSpan? timeout = null) =>
        new("echo", Dotnet, [EchoDll, .. args], null, timeout);

    private ToolInvocation Resolve(ToolSpec spec, Func<string, string?>? variables = null) =>
        ToolRunner.Resolve(spec, variables ?? (_ => null), _dir);

    private static async Task<(ToolResult Result, List<string> Lines)> Run(ToolInvocation tool, CancellationToken ct = default)
    {
        var lines = new List<string>();
        var result = await ToolRunner.RunAsync(tool, l => lines.Add(l), ct);
        return (result, lines);
    }

    [Fact]
    public async Task A_variable_with_a_space_in_the_path_is_one_argument()
    {
        string file = Path.Combine(_dir, "my file with spaces.txt");
        File.WriteAllText(file, "x");
        var tool = Resolve(Echo(["${File}", "--mark=${FileName}"]), ToolVariables.ForDocument(file));
        var (result, lines) = await Run(tool);
        Assert.Equal(ToolEnd.Exited, result.End);
        Assert.Equal([$"[{file}]", "[--mark=my file with spaces.txt]"], lines);
    }

    [Theory]
    [InlineData("say \"hi\"")]
    [InlineData("it's")]
    [InlineData("a & b | c > d < e")]
    [InlineData("100% ^caret^ %PATH% $HOME `id` $(id)")]
    [InlineData("trailing backslash\\")]
    [InlineData("quote at end \"")]
    [InlineData("two  spaces\ttab")]
    [InlineData("")]
    public async Task Text_that_a_shell_would_act_on_reaches_the_program_unchanged(string text)
    {
        var tool = Resolve(Echo([text, "after"]));
        var (_, lines) = await Run(tool);
        Assert.Equal(["[" + text + "]", "[after]"], lines);
    }

    [Fact]
    public async Task A_variable_value_with_quotes_and_operators_stays_inside_its_argument()
    {
        string evil = "x\" & echo injected & \"";
        var tool = Resolve(Echo(["pre-${V}-post"]), n => n == "V" ? evil : null);
        var (_, lines) = await Run(tool);
        Assert.Equal(["[pre-" + evil + "-post]"], lines);
    }

    [Fact]
    public async Task A_folder_with_a_space_is_the_working_directory_and_a_dollar_can_be_written_twice()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_dir, "work dir")).FullName;
        var tool = Resolve(new ToolSpec("t", Dotnet, [EchoDll, "$$HOME", "${FileDir}"], "${FileDir}"), ToolVariables.ForDocument(Path.Combine(folder, "a.txt")));
        Assert.Equal(folder, tool.WorkingDirectory);
        var (_, lines) = await Run(tool);
        Assert.Equal(["[$HOME]", $"[{folder}]"], lines);
    }

    [Fact]
    public void An_unknown_variable_or_one_that_needs_a_file_stops_before_anything_runs()
    {
        var unknown = Assert.Throws<ToolException>(() => Resolve(Echo(["${Nope}"])));
        Assert.Contains("Nope", unknown.Message);
        Assert.Throws<ToolException>(() => Resolve(Echo(["${File}"]), ToolVariables.ForDocument(null)));
        Assert.Throws<ToolException>(() => Resolve(Echo(["${File"])));
    }

    [Fact]
    public void A_missing_program_or_directory_is_named()
    {
        Assert.Contains("not found", Assert.Throws<ToolException>(() => Resolve(new ToolSpec("t", "no-such-program-xyz", []))).Message);
        Assert.Contains("does not exist", Assert.Throws<ToolException>(() => Resolve(new ToolSpec("t", Dotnet, [], Path.Combine(_dir, "missing")))).Message);
    }

    /// <summary>A batch file that hands its arguments, unchanged, to the ToolEcho program (the way vendor/bin/phpstan.bat hands them to php).</summary>
    private string EchoBatch(string name = "echo-tool.bat", string prefix = "", string before = "")
    {
        string bat = Path.Combine(_dir, name);
        File.WriteAllText(bat, $"@echo off\r\n{before}\"{Dotnet}\" \"{EchoDll}\" {prefix}%*\r\n");
        return bat;
    }

    private async Task<List<string>> RunBatch(string bat, params string[] args)
    {
        var (result, lines) = await Run(Resolve(new ToolSpec("t", bat, args)));
        Assert.Equal(ToolEnd.Exited, result.End);
        return lines;
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("with space and\ttab")]
    [InlineData("amp & pipe | lt < gt > caret ^ parens ( ) end")]
    [InlineData("100% %PATH% %%x%% %cd% %~dp0 %1 %*")]
    [InlineData("say \"hi\" and \"\"double\"\"")]
    [InlineData("bang ! !PATH! ^! ^^")]
    [InlineData("trailing backslash\\")]
    [InlineData("two trailing\\\\")]
    [InlineData("back\\\"quote")]
    [InlineData("back\\\\\"quote\\")]
    [InlineData("\"")]
    [InlineData("\\")]
    [InlineData("%")]
    [InlineData("")]
    [InlineData("unicode ěščř ü")]
    [InlineData("a&echo pwned")]
    [InlineData("\"&calc")]
    [InlineData("\" & echo pwned & \"")]
    [InlineData("%PATH%")]
    [InlineData("^&echo pwned")]
    [InlineData("|echo pwned")]
    [InlineData(">pwned.txt")]
    public async Task A_batch_file_gets_every_argument_back_exactly_and_nothing_is_run_or_expanded(string text)
    {
        if (!OperatingSystem.IsWindows()) return;
        var lines = await RunBatch(EchoBatch(), text, "after one", "");
        Assert.Equal(["[" + text + "]", "[after one]", "[]"], lines);
        Assert.False(File.Exists(Path.Combine(_dir, "pwned.txt")));
        Assert.False(File.Exists(Path.Combine(Environment.CurrentDirectory, "pwned.txt")));
    }

    [Fact]
    public async Task A_batch_file_in_a_folder_with_special_characters_is_started_and_the_name_may_leave_out_the_extension()
    {
        if (!OperatingSystem.IsWindows()) return;
        string folder = Directory.CreateDirectory(Path.Combine(_dir, "my tools & (more) 100%")).FullName;
        string bat = Path.Combine(folder, "tool.cmd");
        File.WriteAllText(bat, $"@echo off\r\n\"{Dotnet}\" \"{EchoDll}\" %*\r\n");
        Assert.Equal(["[x y]"], await RunBatch(bat, "x y"));
        Assert.Equal(bat, Resolve(new ToolSpec("t", Path.Combine(folder, "tool"), [])).Executable);
    }

    [Fact]
    public async Task The_exit_code_of_a_batch_file_is_kept_and_a_batch_file_can_be_stopped_by_the_time_limit()
    {
        if (!OperatingSystem.IsWindows()) return;
        var (failed, _) = await Run(Resolve(new ToolSpec("t", EchoBatch(), ["a", "--exit", "7"])));
        Assert.Equal(new ToolResult(ToolEnd.Exited, 7), failed);
        var (slow, lines) = await Run(Resolve(new ToolSpec("t", EchoBatch(), ["--sleep"], null, TimeSpan.FromMilliseconds(3000))));
        Assert.Equal(ToolEnd.TimedOut, slow.End);
        Assert.Contains("started", lines);
    }

    [Theory]
    [InlineData("one\ntwo")]
    [InlineData("one\r\ntwo")]
    [InlineData("tail\r")]
    [InlineData("esc\u001b[0m")]
    public async Task An_argument_cmd_exe_cannot_pass_safely_is_refused_before_anything_starts(string text)
    {
        if (!OperatingSystem.IsWindows()) return;
        string marker = Path.Combine(_dir, "ran.txt");
        string bat = EchoBatch(before: $"echo ran> \"{marker}\"\r\n");
        var e = Assert.Throws<ToolException>(() => Resolve(new ToolSpec("t", bat, ["fine", text])));
        Assert.Contains("control character", e.Message);
        var invocation = new ToolInvocation("t", bat, ["fine", text], _dir, null, "");
        await Assert.ThrowsAsync<ToolException>(() => ToolRunner.RunAsync(invocation, _ => { }));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task A_bang_is_refused_only_when_the_batch_file_turns_delayed_expansion_on()
    {
        if (!OperatingSystem.IsWindows()) return;
        string marker = Path.Combine(_dir, "ran.txt");
        string delayed = EchoBatch("delayed.bat", before: $"setlocal EnableDelayedExpansion\r\necho ran> \"{marker}\"\r\n");
        Assert.Contains("delayed expansion", Assert.Throws<ToolException>(() => Resolve(new ToolSpec("t", delayed, ["a!b"]))).Message);
        Assert.Contains("delayed expansion", Assert.Throws<ToolException>(() => Resolve(new ToolSpec("t", delayed, ["!PATH!"]))).Message);
        Assert.False(File.Exists(marker));
        Assert.Equal(["[a b]"], await RunBatch(delayed, "a b"));
        Assert.Equal(["[a!b]"], await RunBatch(EchoBatch("plain.bat"), "a!b"));
    }

    [Fact]
    public void The_command_line_for_cmd_exe_quotes_every_part_and_neutralises_the_percent_sign()
    {
        string bat = Path.Combine(_dir, "none.bat");
        File.WriteAllText(bat, "@echo off\r\n");
        string expected = "/d /v:off /s /c \"\"" + bat + "\" \"x y\" \"a&b\" \"50%%cd:~,%\" \"q\\\\\"\"z\" \"end\\\\\" \"\"\"";
        Assert.Equal(expected, BatchCommandLine.Build(bat, ["x y", "a&b", "50%", "q\\\"z", "end\\", ""]));
    }

    [Fact]
    public void The_start_info_of_a_program_has_an_argument_list_and_that_of_a_batch_file_goes_through_cmd_exe()
    {
        var program = ProcessLaunch.CreateStartInfo(Dotnet, ["a b", "c&d"]);
        Assert.Equal(["a b", "c&d"], program.ArgumentList);
        Assert.Equal("", program.Arguments);
        if (!OperatingSystem.IsWindows()) return;
        string bat = EchoBatch();
        var batch = ProcessLaunch.CreateStartInfo(bat, ["a b", "c&d"]);
        Assert.Equal(BatchCommandLine.Interpreter, batch.FileName);
        Assert.Empty(batch.ArgumentList);
        Assert.Equal(BatchCommandLine.Build(bat, ["a b", "c&d"]), batch.Arguments);
        Assert.Throws<ToolException>(() => ProcessLaunch.CreateStartInfo(bat, ["one\ntwo"]));
        ProcessLaunch.CreateStartInfo(Dotnet, ["one\ntwo"]); // a program takes the line break as it is
    }

    [Fact]
    public async Task A_formatter_can_be_a_batch_file()
    {
        if (!OperatingSystem.IsWindows()) return;
        string file = Path.Combine(_dir, "my file.txt");
        File.WriteAllText(file, "alpha\n");
        var tool = Resolve(new ToolSpec("fmt", EchoBatch(prefix: "--format "), ["${File}"]), ToolVariables.ForDocument(file));
        var (result, _) = await Run(tool);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("ALPHA\n", File.ReadAllText(file));
    }

    [Fact]
    public void A_bare_name_is_looked_up_in_the_path_only_never_in_the_working_directory()
    {
        if (!OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(_dir, "planted-tool.exe"), "not a program");
        Assert.Throws<ToolException>(() => ToolRunner.Resolve(new ToolSpec("t", "planted-tool", []), _ => null, _dir));
    }

    [Fact]
    public async Task Output_of_both_streams_arrives_line_by_line_and_the_exit_code_is_kept()
    {
        var (result, lines) = await Run(Resolve(Echo(["--both"])));
        Assert.Equal(["to-err", "to-out"], lines.Order());
        Assert.Equal(0, result.ExitCode);
        var (failed, _) = await Run(Resolve(Echo(["a", "--exit", "7"])));
        Assert.Equal(new ToolResult(ToolEnd.Exited, 7), failed);
    }

    [Fact]
    public async Task Running_over_the_time_limit_ends_the_process_and_says_so()
    {
        var tool = Resolve(Echo(["--sleep"], TimeSpan.FromMilliseconds(700)));
        var started = DateTime.UtcNow;
        var (result, lines) = await Run(tool);
        Assert.Equal(ToolEnd.TimedOut, result.End);
        Assert.Contains("started", lines);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30), "the process was not ended");
    }

    [Fact]
    public async Task Cancelling_ends_the_process()
    {
        var tool = Resolve(Echo(["--sleep"]));
        using var cts = new CancellationTokenSource();
        var lines = new List<string>();
        var running = ToolRunner.RunAsync(tool, l => { lines.Add(l); cts.Cancel(); }, cts.Token);
        var result = await running.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(ToolEnd.Cancelled, result.End);
    }

    [Fact]
    public async Task A_program_that_cannot_start_throws_with_the_reason()
    {
        string notExecutable = Path.Combine(_dir, "data.exe");
        File.WriteAllText(notExecutable, "this is not a program");
        var tool = ToolRunner.Resolve(new ToolSpec("t", notExecutable, []), _ => null, _dir);
        await Assert.ThrowsAsync<ToolException>(() => ToolRunner.RunAsync(tool, _ => { }));
    }

    [Fact]
    public void The_command_is_shown_with_quotes_where_the_shell_would_need_them_but_the_list_stays_plain()
    {
        var tool = Resolve(Echo(["a b", "plain", "q\"uote"]));
        Assert.Contains("\"a b\" plain \"q\\\"uote\"", tool.CommandLine);
        Assert.Equal(["a b", "plain", "q\"uote"], tool.Args.Skip(1));
    }

    [Fact]
    public void An_approval_is_tied_to_the_definition_and_survives_a_restart()
    {
        string store = Path.Combine(_dir, "profile", "approvals.txt");
        var spec = Echo(["one"]);
        var tool = Resolve(spec);
        var approvals = new ToolApprovals(store);
        Assert.False(approvals.IsApproved(tool));
        approvals.Approve(tool);
        Assert.True(new ToolApprovals(store).IsApproved(tool));
        Assert.False(approvals.IsApproved(Resolve(Echo(["two"]))));
        Assert.False(approvals.IsApproved(Resolve(spec with { Cwd = Directory.CreateDirectory(Path.Combine(_dir, "other")).FullName })));
        Assert.False(approvals.IsApproved(Resolve(spec with { Id = "renamed" })));
    }

    [Theory]
    [InlineData(ToolVariableType.Path, "a\nb")]
    [InlineData(ToolVariableType.Path, "out*.txt")]
    [InlineData(ToolVariableType.Path, "   ")]
    [InlineData(ToolVariableType.Text, "one\ntwo")]
    public void A_variable_of_the_wrong_type_is_refused_before_anything_starts(ToolVariableType type, string value)
    {
        string marker = Path.Combine(_dir, "ran.txt");
        var spec = Echo(["${Target}", "--exit", "0"]) with { Variables = [new ToolVariableDecl("Target", type, value)] };
        var e = Assert.Throws<ToolException>(() => Resolve(spec));
        Assert.Contains("${Target}", e.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task A_typed_variable_takes_a_value_from_the_lookup_or_from_its_declaration_and_runs_as_one_argument()
    {
        var declared = new[] { new ToolVariableDecl("Out", ToolVariableType.Path, "${FileDir}/my out"), new ToolVariableDecl("Note", ToolVariableType.Text) };
        string file = Path.Combine(_dir, "a.txt");
        var lookup = ToolVariables.ForDocument(file, user: new Dictionary<string, string> { ["Note"] = "hello \"world\"", ["FileBase"] = "own" });
        var tool = Resolve(Echo(["${Out}", "${Note}", "${FileBase}"]) with { Variables = declared }, lookup);
        var (_, lines) = await Run(tool);
        Assert.Equal([$"[{_dir}/my out]", "[hello \"world\"]", "[own]"], lines);

        // declared without a value and without one anywhere: unavailable, like any unknown variable
        Assert.Throws<ToolException>(() => Resolve(Echo(["${Missing}"]) with { Variables = [new ToolVariableDecl("Missing", ToolVariableType.Text)] }));
        Assert.Throws<ToolException>(() => Resolve(Echo([]) with { Variables = [new ToolVariableDecl("X", ToolVariableType.Text, "a"), new ToolVariableDecl("X", ToolVariableType.Text, "b")] }));
    }

    [Fact]
    public void Declared_variables_and_the_origin_belong_to_what_an_approval_is_tied_to()
    {
        var approvals = new ToolApprovals(Path.Combine(_dir, "approvals2.txt"));
        var spec = Echo(["${V}"]) with { Variables = [new ToolVariableDecl("V", ToolVariableType.Text, "one")] };
        approvals.Approve(Resolve(spec));
        Assert.True(approvals.IsApproved(Resolve(spec)));
        Assert.False(approvals.IsApproved(Resolve(spec with { Variables = [new ToolVariableDecl("V", ToolVariableType.Text, "two")] })));
        Assert.False(approvals.IsApproved(Resolve(spec with { Variables = [new ToolVariableDecl("V", ToolVariableType.Path, "one")] })));
        var foreign = spec with { Origin = Path.Combine(_dir, "project", ".textida", "tools.json") };
        Assert.False(approvals.IsApproved(Resolve(foreign)));
        approvals.Approve(Resolve(foreign));
        Assert.True(approvals.IsApproved(Resolve(foreign)));
        var elsewhere = Directory.CreateDirectory(Path.Combine(_dir, "elsewhere")).FullName;
        Assert.False(approvals.IsApproved(ToolRunner.Resolve(foreign, _ => null, elsewhere)));
    }

    [Fact]
    public void Makefile_targets_are_read_without_make()
    {
        const string text = """
            CC := gcc
            OBJS = a.o b.o
            .PHONY: all clean
            all: app docs
            app: $(OBJS)
            	$(CC) -o $@ $^
            %.o: %.c
            	$(CC) -c $<
            $(OBJS): config.h
            clean docs::
            	rm -f app
            define RULE
            fake: nothing
            endef
            export FLAG: yes
            lines = one \
                two: three
            target-specific: CFLAGS = -O2
            ifeq ($(X),1)
            test: app
            endif
            all: again
            """;
        var targets = MakefileTargets.Parse(text);
        Assert.Equal(["all", "app", "clean", "docs", "target-specific", "test"], targets.Select(t => t.Name));
        Assert.Equal(4, targets[0].Line);
        Assert.True(MakefileTargets.IsMakefileName("GNUmakefile"));
        Assert.True(MakefileTargets.IsMakefileName("rules.MK"));
        Assert.False(MakefileTargets.IsMakefileName("notes.txt"));
        var errors = LogPattern.Compile(MakefileTargets.ErrorPattern, LogPatternSyntax.Regex);
        Assert.Equal(new LogLocation("main.c", 12, 5), errors.Match("main.c:12:5: error: x"));
        Assert.Equal(new LogLocation("Makefile", 7, null), errors.Match("make: *** [Makefile:7: all] Error 1"));
        Assert.Equal(new LogLocation(@"C:\src\a.c", 3, 1), errors.Match(@"C:\src\a.c:3:1: warning"));
        Assert.Null(errors.Match("nothing here"));
    }
}
