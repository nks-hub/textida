using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The tool launcher (P4.3.3), the LOG pattern that makes the output clickable (P4.3.1) and the log mode commands (P4.3.2). A tool runs through
/// <see cref="ToolRunner"/>: arguments are a list, never a command line, and the first run of a definition waits for a yes to its command and directory.
/// Its output goes to the Tasks channel of the output window, line by line.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>Where a line of output points: the pattern and the places a relative file name is looked for.</summary>
    private sealed record OutputLog(LogPattern Pattern, string WorkDirectory, string? DocumentPath);

    /// <summary>The output lines as the window shows them, one per text line, so a click can find the line it hit.</summary>
    private List<OutputLine> _outputShown = [];
    private readonly Dictionary<string, OutputLog> _outputLogs = [];
    private readonly Button _outputStop = new() { IsVisible = false };
    private CancellationTokenSource? _toolJob;
    private string _toolRunning = "";
    private CancellationTokenSource? _logJob;
    private LogPatternWindow? _logPatternWindow;

    private const string OutputLogFallback = "";

    public bool ToolRunningNow => _toolJob is not null;
    internal Button OutputStopButton => _outputStop;
    internal TextBox OutputTextBox => _outputText;
    internal bool LogSearchRunning => _logJob is not null;

    // ---- choosing and running a tool ----

    /// <summary>Lists the defined tools; the chosen one is started. A definition file with errors still offers the tools that are valid.</summary>
    internal CommandOutcome ShowToolMenu()
    {
        var catalog = _app.Tools.Load(ActiveDirectory());
        var rows = catalog.Tools.Select(tool => new QuickRow(tool.Title, null, tool.Foreign ? tool.Source : tool.Spec.Executable, null, true, null, tool.Title, () => _ = RunToolAsync(tool))).ToList();
        foreach (var error in catalog.Errors)
            rows.Add(new QuickRow(error, null, null, null, false, Loc.Get(K.ToolsInvalid), error, () => { }));
        string definitions = Loc.Get(K.ToolsOpenDefinitions);
        rows.Add(new QuickRow(definitions, null, _app.Tools.FilePath, null, true, null, definitions, OpenToolDefinitions));
        ShowChoice(Loc.Get(K.ToolsWatermark), Loc.Get(K.A11yTools), rows);
        return CommandOutcome.Handled;
    }

    /// <summary>Opens the file of tool definitions; a first call writes an empty one to start from.</summary>
    internal void OpenToolDefinitions()
    {
        try
        {
            string path = _app.Tools.FilePath;
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "{\n  \"externalTools\": [],\n  \"logPatterns\": []\n}\n");
            }
            OpenFile(path, null);
        }
        catch (IOException e) { SetStatusMessage(e.Message); }
        catch (UnauthorizedAccessException e) { SetStatusMessage(e.Message); }
    }

    /// <summary>
    /// Resolves the variables for the document in front, asks once for a command and a directory that were not run before, and runs the tool.
    /// Everything it has to say goes to the Tasks channel, whatever happens.
    /// </summary>
    internal async Task<bool> RunToolAsync(ToolEntry entry, DocTab? document = null, bool interactive = true)
    {
        if (_toolJob is not null)
        {
            SetStatusMessage(Loc.Get(K.ToolBusy, ("title", _toolRunning)));
            return false;
        }
        var tab = document ?? (Active is { Custom: null } active ? active : null);
        string source = entry.Spec.Id;
        ToolInvocation tool;
        try { tool = ResolveTool(entry.Spec, tab); }
        catch (ToolException e)
        {
            ReportToolFailure(entry, e.Message);
            return false;
        }

        if (!_app.ToolApprovals.IsApproved(tool))
        {
            if (!interactive)
            {
                ReportToolFailure(entry, Loc.Get(K.SaveActionsToolNotApproved)); // nobody is there to answer after an autosave
                return false;
            }
            var details = new List<string> { Loc.Get(K.ToolConfirmCommand, ("command", tool.CommandLine)), Loc.Get(K.ToolConfirmDirectory, ("directory", tool.WorkingDirectory)) };
            if (entry.Source is { } origin) details.Add(Loc.Get(K.ToolConfirmSource, ("source", origin)));
            var request = new ChoiceRequest(Loc.Get(K.ToolConfirmTitle), Loc.Get(entry.Foreign ? K.ToolConfirmForeign : K.ToolConfirmMessage, ("title", entry.Title)),
                [new DialogButton(Loc.Get(K.ToolConfirmRun), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], details);
            if (await _app.Dialogs.ChooseAsync(this, request) != 0) return false;
            _app.ToolApprovals.Approve(tool);
        }
        if (_toolJob is not null)
        {
            SetStatusMessage(Loc.Get(K.ToolBusy, ("title", _toolRunning)));
            return false;
        }

        var cts = _toolJob = new CancellationTokenSource();
        _toolRunning = entry.Title;
        _outputStop.IsVisible = true;
        if (entry.Place is { } place) _outputLogs[source] = new OutputLog(place, tool.WorkingDirectory, tab?.Path);
        else _outputLogs.Remove(source);

        _app.Output.Append(OutputChannel.Tasks, source, "> " + tool.CommandLine);
        _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.ToolOutDirectory, ("directory", tool.WorkingDirectory)));
        ShowOutput(OutputChannel.Tasks);
        bool succeeded = false;
        long started = Stopwatch.GetTimestamp();
        string seconds() => Stopwatch.GetElapsedTime(started).TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture);
        try
        {
            var result = await ToolRunner.RunAsync(tool, text => _app.Output.Append(OutputChannel.Tasks, source, text), cts.Token);
            string summary = result.End switch
            {
                ToolEnd.TimedOut => Loc.Get(K.ToolOutTimedOut, ("seconds", tool.Timeout?.TotalSeconds.ToString("0.##", CultureInfo.CurrentCulture) ?? "")),
                ToolEnd.Cancelled => Loc.Get(K.ToolOutCancelled, ("seconds", seconds())),
                _ => Loc.Get(K.ToolOutFinished, ("code", result.ExitCode), ("seconds", seconds())),
            };
            _app.Output.Append(OutputChannel.Tasks, source, summary);
            SetStatusMessage(summary);
            succeeded = result is { End: ToolEnd.Exited, ExitCode: 0 };
        }
        catch (ToolException e)
        {
            ReportToolFailure(entry, e.Message);
        }
        finally
        {
            _toolJob = null;
            cts.Dispose();
            _outputStop.IsVisible = false;
        }
        return succeeded;
    }

    /// <summary>The folder of the open file, or null; the project tools of tools.json files in a folder are looked for from here.</summary>
    private string? ActiveDirectory() =>
        Active is { Custom: null } tab && tab.Path is { } path ? Path.GetDirectoryName(Path.GetFullPath(path)) : null;

    /// <summary>
    /// The one place that turns a tool definition into the program, the arguments and the directory: the variables of the open document and those of
    /// <c>App.VariableContextFor</c> (the user's own, file.path, date and the rest, as clips and recipes see them) come in here. The launcher and the
    /// definition editor both call it, so the editor shows what the menu starts. A user variables file that cannot be read stops the tool, as it stops a clip.
    /// </summary>
    internal ToolInvocation ResolveTool(ToolSpec spec, DocTab? document = null)
    {
        var tab = document ?? (Active is { Custom: null } active ? active : null);
        var (line, column) = tab?.CaretPosition() ?? (1, 1);
        var context = _app.VariableContextFor(tab?.Path, SelectionTexts(tab?.Surface).FirstOrDefault() ?? "", out var broken);
        if (broken is not null) throw new ToolException(Loc.Get(K.ClipVariablesBroken, ("reason", broken)));
        var scope = new VariableScope(context);
        return ToolRunner.Resolve(spec, ToolVariables.ForDocument(tab?.Path, line, column, name => scope.TryGet(name, out var value) ? value : null),
            (tab?.Path is { } own ? Path.GetDirectoryName(Path.GetFullPath(own)) : ActiveDirectory()) ?? Environment.CurrentDirectory);
    }

    // ---- editor of the definitions ----

    private ToolEditorWindow? _toolEditor;

    /// <summary>Opens the editor of the tool definitions of the profile.</summary>
    internal void ShowToolEditor()
    {
        if (_toolEditor is { } open)
        {
            open.Activate();
            return;
        }
        var window = _toolEditor = new ToolEditorWindow(_app.Tools, PreviewTool);
        window.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F1) { _app.OpenHelp("tools.edit"); e.Handled = true; } };
        window.Closed += (_, _) => _toolEditor = null;
        window.Show(this);
    }

    /// <summary>The line the editor shows for a definition: the command as it would start now, or why it cannot.</summary>
    internal string PreviewTool(ExternalToolContribution definition)
    {
        var variables = ToolCatalog.Declarations(definition);
        if (variables is null) return "";
        try { return Loc.Get(K.ToolEditPreview, ("command", ResolveTool(ToolCatalog.ToSpec(definition, variables)).CommandLine)); }
        catch (ToolException e) { return Loc.Get(K.ToolEditPreviewFailed, ("reason", e.Message)); }
    }

    // ---- Makefile targets ----

    private static readonly Lazy<LogPattern> MakeErrors = new(() => LogPattern.Compile(MakefileTargets.ErrorPattern, LogPatternSyntax.Regex));

    /// <summary>Lists the targets of the open Makefile; the chosen one is run by make through the tool runner.</summary>
    internal CommandOutcome ShowMakeTargets()
    {
        if (Active is not { Custom: null, Hex: null } tab || tab.Path is not { } path || !MakefileTargets.IsMakefileName(Path.GetFileName(path)))
        {
            SetStatusMessage(Loc.Get(K.MakeNotMakefile));
            return CommandOutcome.Handled;
        }
        string text;
        try { text = tab.Surface is { } surface ? surface.Document.GetText(0, surface.Document.TextLength) : File.ReadAllText(path); }
        catch (IOException e)
        {
            SetStatusMessage(e.Message);
            return CommandOutcome.Handled;
        }
        var targets = MakefileTargets.Parse(text);
        if (targets.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.MakeNoTargets, ("file", Path.GetFileName(path))));
            return CommandOutcome.Handled;
        }
        var rows = targets.Select(t => new QuickRow(t.Name, null, Loc.Get(K.MakeDetail, ("line", t.Line)), null, true, null, t.Name, () => _ = RunMakeTargetAsync(path, t.Name))).ToList();
        ShowChoice(Loc.Get(K.MakeWatermark), Loc.Get(K.A11yMake), rows);
        return CommandOutcome.Handled;
    }

    /// <summary>
    /// Runs <c>make -f file target</c> in the folder of the Makefile as a tool: the command is confirmed the first time, the output goes to the Tasks channel and a line
    /// that names a file and a line opens it. <paramref name="program"/> and <paramref name="programArgs"/> (put before the arguments of make) exist so a test can stand in for make.
    /// </summary>
    internal Task RunMakeTargetAsync(string makefile, string target, string program = MakefileTargets.DefaultProgram, IReadOnlyList<string>? programArgs = null)
    {
        string full = Path.GetFullPath(makefile);
        static string Literal(string text) => text.Replace("$", "$$", StringComparison.Ordinal);
        var definition = new ExternalToolContribution
        {
            Id = "make:" + target,
            Title = Loc.Get(K.MakeTitle, ("target", target)),
            Executable = Literal(program),
            Args = [.. (programArgs ?? []).Select(Literal), "-f", Literal(Path.GetFileName(full)), Literal(target)],
            Cwd = Literal(Path.GetDirectoryName(full)!),
        };
        return RunToolAsync(new ToolEntry(definition, ToolCatalog.ToSpec(definition, [], full), null, MakeErrors.Value));
    }

    private void ReportToolFailure(ToolEntry entry, string reason)
    {
        string text = Loc.Get(K.ToolCannotRun, ("title", entry.Title), ("reason", reason));
        _app.Output.Append(OutputChannel.Tasks, entry.Spec.Id, text);
        SetStatusMessage(text);
        ShowOutput(OutputChannel.Tasks);
    }

    /// <summary>Ends the running tool and everything it started. The output it wrote so far stays.</summary>
    internal CommandOutcome StopTool()
    {
        if (_toolJob is not { } job) return CommandOutcome.NotHandled;
        job.Cancel();
        return CommandOutcome.Handled;
    }

    // ---- clicking in the output ----

    private void WireOutputClicks()
    {
        _outputStop.Classes.Add("outputaction");
        _outputStop.Click += (_, _) => StopTool();
        _outputText.Tapped += (_, _) =>
        {
            if (_outputText.SelectionStart == _outputText.SelectionEnd) OpenOutputLineAt(_outputText.CaretIndex);
        };
    }

    /// <summary>Opens the place the output line at the text position points to; false when the line points nowhere.</summary>
    internal bool OpenOutputLineAt(int textIndex)
    {
        string text = _outputText.Text ?? "";
        int index = text.AsSpan(0, Math.Clamp(textIndex, 0, text.Length)).Count('\n');
        return OpenOutputLine(index);
    }

    /// <summary>Opens the place the line with this zero-based number of the shown output points to.</summary>
    internal bool OpenOutputLine(int index)
    {
        if (_outputChannel != OutputChannel.Tasks || index < 0 || index >= _outputShown.Count) return false;
        var line = _outputShown[index];
        if (!_outputLogs.TryGetValue(line.Source, out var log) && !_outputLogs.TryGetValue(OutputLogFallback, out log)) return false;
        if (log.Pattern.Match(line.Text) is not { } place)
        {
            SetStatusMessage(Loc.Get(K.LogNoPlace));
            return false;
        }
        if (ResolveLogFile(place, log) is not { } file)
        {
            SetStatusMessage(Loc.Get(K.LogFileMissing, ("file", place.File ?? "")));
            return false;
        }
        try
        {
            var tab = OpenFile(file, null);
            if (tab.Surface is { } surface)
            {
                int number = Math.Min(place.Line, surface.Document.LineCount);
                surface.GoToLine(number);
                var target = surface.Document.GetLineByNumber(number);
                surface.SetSelections([Sel.At(target.Offset + Math.Min((place.Column ?? 1) - 1, target.Length))]);
            }
            else tab.GoToLine(place.Line);
            FocusView(tab);
            return true;
        }
        catch (IOException e)
        {
            SetStatusMessage(e.Message);
            return false;
        }
    }

    /// <summary>
    /// The file a place names: a full path as it is; a relative one against the directory the tool ran in, then the directory of the file that was open when
    /// it started, then an open document of that name. A place without a file means the file that was open.
    /// </summary>
    private string? ResolveLogFile(LogLocation place, OutputLog log)
    {
        if (place.File is not { } name) return log.DocumentPath is { } own && File.Exists(own) ? own : null;
        var candidates = new List<string>();
        try
        {
            if (Path.IsPathRooted(name)) candidates.Add(name);
            else
            {
                candidates.Add(Path.Combine(log.WorkDirectory, name));
                if (log.DocumentPath is { } document && Path.GetDirectoryName(Path.GetFullPath(document)) is { } directory) candidates.Add(Path.Combine(directory, name));
            }
            if (candidates.FirstOrDefault(File.Exists) is { } found) return Path.GetFullPath(found);
            string fileName = Path.GetFileName(name);
            return Tabs.Select(t => t.Path).FirstOrDefault(p => p is not null && string.Equals(Path.GetFileName(p), fileName, PathComparison));
        }
        catch (ArgumentException)
        {
            return null; // a name with characters no path can hold
        }
    }

    // ---- LOG pattern with a preview ----

    /// <summary>Opens the tester for a LOG pattern over the latest output of the tools; a pattern it accepts is used for the clicks in the output window.</summary>
    internal void ShowLogPatternTester()
    {
        if (_logPatternWindow is { } open)
        {
            open.Activate();
            return;
        }
        string sample = string.Join('\n', _app.Output.Read(OutputChannel.Tasks, 200).Select(l => l.Text));
        var window = _logPatternWindow = new LogPatternWindow("", LogPatternSyntax.Mask, sample);
        window.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.F1) { _app.OpenHelp("tools.logPattern"); e.Handled = true; } };
        window.Used += UseLogPattern;
        window.Closed += (_, _) => _logPatternWindow = null;
        window.Show(this);
    }

    /// <summary>Makes <paramref name="pattern"/> the one the output window uses for every line, over the patterns of single tools.</summary>
    internal void UseLogPattern(LogPattern pattern)
    {
        string? document = Active is { Custom: null } tab ? tab.Path : null;
        string directory = document is not null ? Path.GetDirectoryName(Path.GetFullPath(document)) ?? Environment.CurrentDirectory : Environment.CurrentDirectory;
        _outputLogs.Clear();
        _outputLogs[OutputLogFallback] = new OutputLog(pattern, directory, document);
        SetStatusMessage(Loc.Get(K.LogPatternUsed));
    }

    // ---- log mode ----

    /// <summary>Jumps to the next or previous line of level error in a document in log mode; the search reads the file in blocks and Esc stops it.</summary>
    internal CommandOutcome LogStep(bool forward)
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        if (!tab.Doc.IsLog)
        {
            SetStatusMessage(Loc.Get(K.LogNotLogMode));
            return CommandOutcome.Handled;
        }
        if (_logJob is not null)
        {
            SetStatusMessage(Loc.Get(K.LogSearchBusy));
            return CommandOutcome.Handled;
        }
        _ = LogStepAsync(tab, forward);
        return CommandOutcome.Handled;
    }

    private async Task LogStepAsync(DocTab tab, bool forward)
    {
        var doc = tab.Doc.Doc;
        var snapshot = doc.GetSnapshot();
        long caret = CaptureOffsets(tab).Caret;
        var rules = tab.Doc.LogLevels;
        long first = doc.BomLength;
        var cts = _logJob = new CancellationTokenSource();
        var shown = Stopwatch.StartNew();
        void Progress(long reached)
        {
            if (shown.ElapsedMilliseconds < 300) return;
            shown.Restart();
            long total = Math.Max(1, snapshot.ByteLength);
            long done = forward ? reached : total - reached;
            Dispatcher.UIThread.Post(() => { if (_logJob == cts) SetStatusMessage(Loc.Get(K.LogSearching, ("percent", Math.Clamp(done * 100 / total, 0, 100)))); });
        }
        SetStatusMessage(Loc.Get(K.LogSearching, ("percent", 0)));
        long? hit;
        try
        {
            hit = await Task.Run(() =>
            {
                long from = forward ? LogScan.NextLineStart(snapshot, snapshot.Codec, caret) : LogScan.LineStart(snapshot, snapshot.Codec, first, caret);
                return LogScan.Find(snapshot, snapshot.Codec, first, rules, LogLevel.Error, from, forward, Progress, cts.Token);
            }, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(Loc.Get(K.LogCancelled));
            return;
        }
        catch (RegexMatchTimeoutException)
        {
            SetStatusMessage(Loc.Get(K.LogTimedOut));
            return;
        }
        finally
        {
            _logJob = null;
            cts.Dispose();
        }
        if (doc.Revision != snapshot.Revision) SetStatusMessage(Loc.Get(K.LogChanged));
        else if (hit is not { } offset) SetStatusMessage(Loc.Get(forward ? K.LogNoNext : K.LogNoPrevious));
        else
        {
            RecordJump();
            SelectTab(tab);
            ApplyOffsets(tab, offset, offset);
            FocusView(tab);
            SetStatusMessage(Loc.Get(K.LogFound));
        }
    }

    /// <summary>Esc ends a running search for an error.</summary>
    private bool CancelLogSearch()
    {
        if (_logJob is not { } job) return false;
        job.Cancel();
        return true;
    }

    /// <summary>Lets the user pick the rules that give the lines of this document their level, and turns log mode on for it.</summary>
    internal CommandOutcome ChooseLogLevels()
    {
        if (Active is not { Custom: null, Hex: null } tab) return CommandOutcome.NotHandled;
        var catalog = _app.Tools.Load();
        void Apply(LogLevelRules rules, string name)
        {
            tab.Doc.ChooseLanguage(LanguageModes.ById(LanguageModes.LogId));
            tab.Doc.SetLogLevels(rules, name);
            SetStatusMessage(Loc.Get(K.LogLevelsApplied, ("name", name)));
        }
        string builtin = Loc.Get(K.LogLevelsDefault);
        var rows = new List<QuickRow> { new(builtin, null, null, null, true, null, builtin, () => Apply(LogLevelRules.Default, builtin)) };
        foreach (var pattern in catalog.Patterns.Where(p => p.Levels is not null))
            rows.Add(new QuickRow(pattern.Title, null, pattern.Definition.Id, null, true, null, pattern.Title, () => Apply(pattern.Levels!, pattern.Title)));
        ShowChoice(Loc.Get(K.LogLevelsWatermark), Loc.Get(K.A11yLogLevels), rows);
        return CommandOutcome.Handled;
    }
}
