using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Sends the selection, or the line at the caret, to an interpreter that stays open (PZ.6.2): Python, PHP, Node.js or psql, by the language of the document. What it prints goes to the output panel under the source
/// <c>interpreter:name</c>. The program is the one of a <c>.venv</c> (or <c>node_modules/.bin</c>) in the folder of the file or above it when there is one, else the one on the PATH. One interpreter runs per kind and window;
/// it ends with the command that stops them, with the window, or by itself (its end is said in the output).
/// </summary>
internal sealed partial class MainWindow
{
    private readonly Dictionary<string, InterpreterSession> _interpreters = [];
    private bool _interpretersHooked;

    internal CommandOutcome SendToInterpreter()
    {
        if (ActiveEditor is not { } surface || Active is not { } tab) return CommandOutcome.NotHandled;
        if (InterpreterKind.ForLanguage(tab.Doc.Language.Id) is not { } kind)
        {
            SetStatusMessage(Loc.Get(K.InterpreterNoLanguage, ("language", tab.Doc.Language.Name)));
            return CommandOutcome.Handled;
        }
        var selected = surface.GetSelections().Where(s => !s.IsEmpty).Select(s => surface.Document.GetText(s.Start, s.Length)).ToList();
        string text = selected.Count > 0 ? string.Join(Environment.NewLine, selected) : surface.Document.GetLineByOffset(Math.Min(surface.CaretOffset, surface.Document.TextLength)) is { } line ? surface.Document.GetText(line.Offset, line.Length) : "";
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatusMessage(Loc.Get(K.InterpreterNothing));
            return CommandOutcome.Handled;
        }
        string folder = tab.Path is { } path ? System.IO.Path.GetDirectoryName(path)! : Environment.CurrentDirectory;
        string source = "interpreter:" + kind.Id;
        if (!_interpreters.TryGetValue(kind.Id, out var session) || !session.Running)
        {
            try
            {
                session = InterpreterSession.Start(kind, folder, output => Dispatcher.UIThread.Post(() => _app.Output.Append(OutputChannel.Tasks, source, output)))!;
            }
            catch (InvalidOperationException e)
            {
                SetStatusMessage(Loc.Get(K.InterpreterFailed, ("title", kind.Title), ("reason", e.Message)));
                return CommandOutcome.Handled;
            }
            if (session is null)
            {
                SetStatusMessage(Loc.Get(K.InterpreterNotFound, ("title", kind.Title), ("programs", string.Join(", ", kind.Programs))));
                return CommandOutcome.Handled;
            }
            _interpreters[kind.Id] = session;
            var started = session;
            session.Exited += code => Dispatcher.UIThread.Post(() =>
            {
                _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.InterpreterEnded, ("title", kind.Title), ("code", code)));
                if (_interpreters.TryGetValue(kind.Id, out var current) && ReferenceEquals(current, started)) _interpreters.Remove(kind.Id);
            });
            if (!_interpretersHooked)
            {
                _interpretersHooked = true;
                Closed += (_, _) => StopInterpreters();
            }
            _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.InterpreterStarted, ("title", kind.Title), ("directory", folder)));
        }
        foreach (var echoed in text.Replace("\r\n", "\n").Split('\n')) _app.Output.Append(OutputChannel.Tasks, source, "> " + echoed);
        try { session.Send(text); }
        catch (Exception e) when (e is InvalidOperationException or IOException)
        {
            SetStatusMessage(Loc.Get(K.InterpreterFailed, ("title", kind.Title), ("reason", e.Message)));
            return CommandOutcome.Handled;
        }
        ShowOutput(OutputChannel.Tasks);
        return CommandOutcome.Handled;
    }

    /// <summary>Ends every interpreter of the window.</summary>
    internal CommandOutcome StopInterpreters()
    {
        int count = _interpreters.Count;
        foreach (var session in _interpreters.Values.ToList()) session.Dispose();
        _interpreters.Clear();
        if (count > 0) SetStatusMessage(Loc.Get(K.InterpreterStopped, ("count", count)));
        return CommandOutcome.Handled;
    }
}
