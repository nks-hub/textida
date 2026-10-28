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

    internal CommandOutcome ToggleHexStatistics()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        var panel = hex.Statistics;
        panel.IsVisible = !panel.IsVisible;
        if (panel.IsVisible) panel.Refresh();
        return CommandOutcome.Handled;
    }
}
