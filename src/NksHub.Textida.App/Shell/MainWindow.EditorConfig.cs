using Avalonia.Controls;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;
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

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<EditorConfigResult, IReadOnlyList<ConfigHit>> ConfigHitCache = new();

    /// <summary>
    /// The configuration files that apply to the document (P8.7.4), found by walking the parent folders only. The walk runs once per <see cref="OpenDocument.EditorConfig"/> result,
    /// which is replaced when the file is opened, saved or renamed, so the status bar does not touch the disk on every caret move. <c>.editorconfig</c> comes from that result.
    /// </summary>
    internal static IReadOnlyList<ConfigHit> ConfigHitsOf(OpenDocument od) =>
        od.Path is not { } path ? [] : ConfigHitCache.GetValue(od.EditorConfig, editorConfig =>
        [.. ConfigFiles.For(path).Select(h => ReferenceEquals(h.Config, LanguageCatalog.EditorConfig) ? h with { Path = editorConfig.Properties.Count > 0 ? editorConfig.Files[0] : null } : h)
            .Where(h => h.Path is not null || !ReferenceEquals(h.Config, LanguageCatalog.EditorConfig) || editorConfig.Files.Count == 0)]);

    /// <summary>The text of the status bar item: the names of the configuration files found, two at most.</summary>
    internal static string ConfigLabel(IReadOnlyList<ConfigHit> found)
    {
        var names = found.Select(h => System.IO.Path.GetFileName(h.Path!)).ToList();
        return string.Join(", ", names.Take(2)) + (names.Count > 2 ? " +" + (names.Count - 2) : "");
    }

    /// <summary>Lists the configuration files of the active file: a found one opens on a click, a missing one with a template is created in the project folder.</summary>
    internal CommandOutcome ShowConfigFiles()
    {
        var rows = new List<QuickRow>();
        if (Active is { Custom: null, Hex: null } tab && tab.Path is { } path)
        {
            var hits = ConfigHitsOf(tab.Doc);
            foreach (var hit in hits.Where(h => h.Path is not null))
            {
                string found = hit.Path!, name = System.IO.Path.GetFileName(found);
                rows.Add(new QuickRow(name, null, System.IO.Path.GetDirectoryName(found), null, true, null, name, () => OpenConfigFile(found)));
            }
            foreach (var hit in hits.Where(h => h.Path is null && h.Config.Template is not null))
            {
                var config = hit.Config;
                string name = Loc.Get(K.ConfigFilesCreate, ("name", config.Names[0])), detail = Loc.Get(K.ConfigFilesCreateDetail, ("folder", ConfigFiles.FolderForNew(path)));
                rows.Add(new QuickRow(name, null, detail, null, true, null, name + ", " + detail, () => CreateConfigFile(tab, config)));
            }
        }
        ShowChoice(Loc.Get(K.ConfigFilesWatermark), Loc.Get(K.A11yConfigFiles), rows, emptyText: Loc.Get(K.ConfigFilesEmpty));
        return CommandOutcome.Handled;
    }

    private void OpenConfigFile(string path)
    {
        try { OpenFile(path, null); }
        catch (IOException e) { SetStatusMessage(e.Message); }
    }

    private void CreateConfigFile(DocTab tab, CatalogConfig config)
    {
        string created;
        try { created = ConfigFiles.Create(config, tab.Path!); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatusMessage(Loc.Get(K.ConfigFilesFailed, ("name", config.Names[0]), ("reason", e.Message)));
            return;
        }
        tab.Doc.RefreshEditorConfig();
        OpenConfigFile(created);
        SetStatusMessage(Loc.Get(K.ConfigFilesCreated, ("file", created)));
    }

    /// <summary>Status bar of the search results tab: the counts on the left, the name of the view on the right.</summary>
    private void ShowResultsStatus()
    {
        if (Active?.Custom is Editing.DiffView compare)
        {
            _statusLeft.Text = _message.Length > 0 ? _message : compare.SummaryText;
            _statusRight.Children.Add(new TextBlock { Text = Loc.Get(K.A11yCompare), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            return;
        }
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
        if (od.IsLarge || od.IsHex) return;
        od.RefreshEditorConfig();
        var options = OptionsFor(od);
        bool? finalNewline = options.InsertFinalNewline ? true : od.EditorConfig.Get("insert_final_newline") == "false" ? false : null;
        if (!options.TrimTrailingWhitespace && finalNewline is null) return;
        var (plan, _) = SaveTransforms.Apply(od.Doc, new SaveTransformOptions(options.TrimTrailingWhitespace, finalNewline, options.ConfiguredEol));
        if (plan.Note is not null) SetStatusMessage(Loc.Get(K.StatusTrimSkipped));
    }
}
