using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>The map of the file and the byte statistics beside the hex grid (P5.6.4).</summary>
internal sealed partial class MainWindow
{
    internal CommandOutcome ToggleHexMinimap()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        hex.SetMinimapVisible(!hex.Minimap.IsVisible);
        return CommandOutcome.Handled;
    }

    /// <summary>Gives the keyboard to the map (shown first when it is hidden); Up, Down, Page Up, Page Down, Home and End move the caret through the file, Esc goes back to the grid.</summary>
    internal CommandOutcome FocusHexMinimap()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        if (!hex.Minimap.IsVisible) hex.SetMinimapVisible(true);
        hex.Minimap.Focus(Avalonia.Input.NavigationMethod.Tab);
        return CommandOutcome.Handled;
    }

    /// <summary>Switches the cells of the map between the entropy and the kind of byte, and says which it is now.</summary>
    internal CommandOutcome CycleHexMapMode()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        hex.Minimap.Mode = hex.Minimap.Mode == ByteMapMode.Entropy ? ByteMapMode.Class : ByteMapMode.Entropy;
        SetStatusMessage(Loc.Get(hex.Minimap.Mode == ByteMapMode.Entropy ? K.HexMapEntropy : K.HexMapClass));
        return CommandOutcome.Handled;
    }

    /// <summary>Opens the structure panel, or closes it when it already has the focus (P8.10.1).</summary>
    internal CommandOutcome ToggleHexTemplate()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        var panel = hex.Structure;
        if (panel.IsVisible && panel.IsKeyboardFocusWithin) { panel.IsVisible = false; hex.Focus(); }
        else panel.FocusTree();
        return CommandOutcome.Handled;
    }

    /// <summary>Hex: Open Structure Template File: asks for a <c>.ksy</c> file and reads the file through it (P8.10.1).</summary>
    internal CommandOutcome OpenHexTemplateFile()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        _ = hex.Structure.OpenFileAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Hex: Export Structure Tree to JSON: the tree of the chosen template opens as a JSON document in a new tab.</summary>
    internal CommandOutcome ExportHexTemplateJson()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        _ = ExportHexTemplateJsonAsync(hex);
        return CommandOutcome.Handled;
    }

    internal async Task ExportHexTemplateJsonAsync(HexView hex)
    {
        (string Json, bool Truncated)? result;
        try { result = await hex.Structure.ExportJsonAsync(); }
        catch (IOException e)
        {
            SetStatusMessage(Loc.Get(K.KsyExportFailed, ("reason", e.Message)));
            return;
        }
        if (result is not { } tree)
        {
            SetStatusMessage(Loc.Get(K.KsyExportNone));
            return;
        }
        OpenGeneratedText(tree.Json.ReplaceLineEndings(Environment.NewLine), "json");
        if (tree.Truncated) SetStatusMessage(Loc.Get(K.KsyExportTruncated, ("nodes", Core.Hex.Ksy.KsyJson.MaxNodes)));
    }

    internal CommandOutcome ToggleHexStatistics()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        var panel = hex.Statistics;
        panel.IsVisible = !panel.IsVisible;
        if (panel.IsVisible) panel.Refresh();
        return CommandOutcome.Handled;
    }
}
