using System.IO.Compression;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Import of TextMate grammars (PZ.5.3): a file (<c>.tmLanguage</c>, <c>.tmLanguage.json</c>, a zipped <c>.tmbundle</c>) or a bundle folder is converted to the grammar format of the editor (<see cref="TextMateImport"/>), kept as a data plugin
/// of its own and offered as a language. Every construct that was not carried over is written to the Tasks tab, so the user sees what the colours will lack.
/// </summary>
internal sealed partial class MainWindow
{
    internal CommandOutcome ImportTextMate()
    {
        _ = ImportTextMateAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>The import in progress, for tests.</summary>
    internal Task TextMateWork { get; private set; } = Task.CompletedTask;

    private async Task ImportTextMateAsync()
    {
        string title = Loc.Get(K.TextMateTitle);
        int kind = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(title, Loc.Get(K.TextMateChoose),
            [new DialogButton(Loc.Get(K.TextMateFile), IsDefault: true), new DialogButton(Loc.Get(K.TextMateFolder)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
        string? path = kind switch
        {
            0 => await _app.Dialogs.PickFileAsync(this, title, Loc.Get(K.TextMateFilter), ["*.tmLanguage", "*.tmLanguage.json", "*.plist", "*.json", "*.tmbundle", "*.zip"]),
            1 => await _app.Dialogs.PickFolderAsync(this),
            _ => null,
        };
        if (path is null) return;
        TextMateWork = ImportTextMateAsync(path);
        await TextMateWork;
    }

    /// <summary>Converts the grammars of <paramref name="path"/> off the interface thread and keeps them; says in the status bar what was imported and lists the warnings in the Tasks tab.</summary>
    internal async Task ImportTextMateAsync(string path)
    {
        IReadOnlyList<TextMateImportResult> results;
        try { results = await Task.Run(() => TextMateImport.FromPath(path, BuiltinGrammars.Registry)); }
        catch (Exception e) when (e is TextMateImportException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatusMessage(Loc.Get(K.TextMateFailed, ("reason", e.Message)));
            return;
        }
        int imported = 0, warnings = 0;
        const string source = "textmate";
        foreach (var result in results)
        {
            if (!_app.Clips.ImportGrammar(result.Id, result.Name, result.Json, out var detail))
            {
                _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.TextMateNotKept, ("name", result.Name), ("reason", detail)));
                continue;
            }
            imported++;
            warnings += result.Warnings.Count;
            _app.Output.Append(OutputChannel.Tasks, source, Loc.Get(K.TextMateImported, ("name", result.Name), ("id", result.Id), ("warnings", result.Warnings.Count)));
            foreach (var warning in result.Warnings) _app.Output.Append(OutputChannel.Tasks, source, "  " + warning);
        }
        LanguageModes.UseUserGrammars(_app.Clips.Grammars);
        SetStatusMessage(Loc.Get(K.TextMateDone, ("imported", imported), ("warnings", warnings)));
        ShowOutput(OutputChannel.Tasks);
    }
}
