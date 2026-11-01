using System.Text.Json.Nodes;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>View, Icon Skin (PZ.4.3): an optional folder of bitmaps replaces the vector icons of the commands it has a picture for; the others keep their drawing.</summary>
internal sealed partial class MainWindow
{
    internal const string IconSkinSetting = "appearance.iconSkin";

    internal CommandOutcome ChooseIconSkin()
    {
        _ = ChooseIconSkinAsync();
        return CommandOutcome.Handled;
    }

    internal async Task ChooseIconSkinAsync()
    {
        string? folder = await _app.Dialogs.PickFolderAsync(this);
        if (folder is null) return;
        var skin = IconSkin.Load(folder);
        if (skin.Images.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.IconSkinEmpty, ("folder", folder), ("reason", skin.Problems.FirstOrDefault() ?? Loc.Get(K.IconSkinNoPictures))));
            return;
        }
        ApplyIconSkin(folder);
        SetStatusMessage(Loc.Get(K.IconSkinUsed, ("icons", skin.Images.Count), ("problems", skin.Problems.Count)));
    }

    internal CommandOutcome ResetIconSkin()
    {
        ApplyIconSkin("");
        SetStatusMessage(Loc.Get(K.IconSkinOff));
        return CommandOutcome.Handled;
    }

    private void ApplyIconSkin(string folder)
    {
        IconSkin.Use(folder);
        _app.Workbench.Set(IconSkinSetting, JsonValue.Create(folder));
        foreach (var window in _app.Windows) window.RefreshToolbar();
    }
}
