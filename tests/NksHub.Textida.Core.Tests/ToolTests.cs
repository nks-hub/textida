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

    [Fact]
    public void A_batch_file_is_refused_on_windows_because_it_would_split_the_arguments_again()
    {
        if (!OperatingSystem.IsWindows()) return;
        string bat = Path.Combine(_dir, "run.cmd");
        File.WriteAllText(bat, "@echo off\r\n");
        Assert.Contains("batch file", Assert.Throws<ToolException>(() => Resolve(new ToolSpec("t", bat, []))).Message);
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
