using Avalonia.Controls;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Shell;

/// <summary>EditorConfig in the open documents (P2.6): where a value comes from is shown in the status bar and the settings, and a save trims only the lines that were edited.</summary>
internal sealed partial class MainWindow
{
    /// <summary>The <c>.editorconfig</c> that decides the indentation of the active document, or null when the indentation comes from the settings or from the content.</summary>
    private string? IndentSourceFile()
    {
        foreach (var key in new[] { "editor.indentSize", "editor.insertSpaces", "editor.tabSize" })
            if (_app.Workbench.Settings.Resolve(key) is { Source: SettingSource.EditorConfig } r) return r.LayerName;
        return null;
    }

    private void OpenEditorConfigFile()
    {
        if (Active?.Doc.EditorConfig.Files is not { Count: > 0 } files) return;
        try { OpenFile(files[0], null); }
        catch (IOException e) { SetStatusMessage(e.Message); }
    }

    /// <summary>Status bar of the search results tab: the counts on the left, the name of the view on the right.</summary>
    private void ShowResultsStatus()
    {
        _statusLeft.Text = ResultsView?.SummaryText ?? Loc.Get(K.StatusReady);
        _statusRight.Children.Add(new TextBlock { Text = Loc.Get(K.StatusSearchResults), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
    }

    /// <summary>
    /// What a save does to the text, from the settings and the <c>.editorconfig</c> of the file: trailing blanks go only from lines edited since the last save,
    /// and the final line break is added (or, when <c>insert_final_newline = false</c> says so, removed). Line endings and the encoding are never touched.
    /// One undo step, made only when a save really goes ahead.
    /// </summary>
    private void PrepareForSave(OpenDocument od)
    {
        if (od.IsLarge) return;
        od.RefreshEditorConfig();
        var options = OptionsFor(od);
        bool? finalNewline = options.InsertFinalNewline ? true : od.EditorConfig.Get("insert_final_newline") == "false" ? false : null;
        if (!options.TrimTrailingWhitespace && finalNewline is null) return;
        var (plan, _) = SaveTransforms.Apply(od.Doc, new SaveTransformOptions(options.TrimTrailingWhitespace, finalNewline, options.ConfiguredEol));
        if (plan.Note is not null) SetStatusMessage(Loc.Get(K.StatusTrimSkipped));
    }
}
