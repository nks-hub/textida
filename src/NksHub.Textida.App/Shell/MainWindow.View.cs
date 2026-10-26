using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Search;

namespace NksHub.Textida.App.Shell;

/// <summary>The view switches of the View menu: each one writes a setting, so it has a source, survives a restart and can be set in the settings file.</summary>
internal sealed partial class MainWindow
{
    private const int DefaultRightMargin = 80;

    /// <summary>Spaces, tabs and line endings are both drawn.</summary>
    internal bool HiddenCharactersShown => Options is { ShowWhitespace: true, ShowLineEndings: true };

    internal void ToggleHiddenCharacters()
    {
        bool on = !HiddenCharactersShown;
        _app.Workbench.Set("editor.renderWhitespace", on ? "all" : "none");
        _app.Workbench.Set("editor.renderLineEndings", JsonValue.Create(on));
    }

    internal void ToggleLineNumbers() => ToggleSetting("editor.lineNumbers");

    internal void ToggleCurrentLine() => ToggleSetting("editor.highlightCurrentLine");

    internal void ToggleLinks() => ToggleSetting("editor.links");

    internal void ToggleMinimap() => ToggleSetting("editor.minimap");

    internal void ToggleRightMargin() => _app.Workbench.Set("editor.rightMargin", JsonValue.Create(Options.RightMargin > 0 ? 0 : DefaultRightMargin));

    private void ToggleSetting(string key) => _app.Workbench.Set(key, JsonValue.Create(!_app.Workbench.Settings.GetBool(key)));

    private DocTab? _marksTab;

    /// <summary>Marks the lines of the find matches in the strip of the minimap of <paramref name="tab"/>; null clears the marks.</summary>
    private void ShowFindMarks(DocTab? tab, IReadOnlyList<SearchHit>? hits)
    {
        if (_marksTab is not null && _marksTab != tab) _marksTab.Minimap?.SetMarks([]);
        _marksTab = tab;
        tab?.Minimap?.SetMarks(hits is null ? [] : hits.Select(h => new MinimapMark(h.Line - 1, MinimapMarkKind.Match)).ToList());
    }

    /// <summary>Ctrl+wheel over a text view zooms like the zoom commands, so the size is the setting and not a leftover of the control.</summary>
    private void OnWheelZoom(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers != Shortcuts.Primary || e.Delta.Y == 0 || Active is not { Custom: null } tab) return;
        if (e.Source is not Visual source || !tab.Root.IsVisualAncestorOf(source)) return;
        Zoom(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }
}
