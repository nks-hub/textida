using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Services;

/// <summary>What the bookmark dialog edits; <paramref name="Type"/> and <paramref name="Region"/> are shown, not edited.</summary>
internal sealed record HexBookmarkForm(string Name, string? Color, string? Comment, string? Type, string Region);

/// <summary>The dialog of a hex bookmark.</summary>
internal sealed partial class AvaloniaDialogs
{
    /// <summary>The colours the dialog offers by name: the accent, danger and good tones of the theme and three more of the same weight.</summary>
    internal static readonly (string Hex, string Key)[] BookmarkPalette =
    [
        ("#0F7B6C", K.HexBmColorTeal), ("#B3261E", K.HexBmColorRed), ("#C77700", K.HexBmColorAmber),
        ("#2D6CDF", K.HexBmColorBlue), ("#7B4FBF", K.HexBmColorViolet), ("#1B6E3A", K.HexBmColorGreen),
    ];

    public async Task<HexBookmarkForm?> HexBookmarkAsync(Window owner, HexBookmarkForm initial)
    {
        var name = new TextBox { Text = initial.Name, MinWidth = 220 };
        var preset = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        preset.Items.Add(Loc.Get(K.HexBmColorNone));
        foreach (var (_, key) in BookmarkPalette) preset.Items.Add(Loc.Get(key));
        var code = new TextBox { MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Left };
        var error = new TextBlock { Foreground = ThemeColors.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var comment = new TextBox { Text = initial.Comment ?? "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxHeight = 120 };
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetName(preset, Loc.Get(K.HexBmColor));
        AutomationProperties.SetName(code, Loc.Get(K.HexBmColorCode));
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);

        bool syncing = false;
        void ShowColor(string? color)
        {
            syncing = true;
            int index = color is null ? 0 : Array.FindIndex(BookmarkPalette, p => p.Hex == color) + 1;
            preset.SelectedIndex = index;
            code.Text = color ?? "";
            syncing = false;
        }
        string? Parsed(out bool valid)
        {
            valid = true;
            try { return HexBookmarks.NormalizeColor(code.Text); }
            catch (ArgumentException) { valid = false; return null; }
        }
        void Check()
        {
            Parsed(out bool valid);
            error.Text = valid ? "" : Loc.Get(K.HexBmColorInvalid);
            ok.IsEnabled = valid;
        }
        preset.SelectionChanged += (_, _) =>
        {
            if (syncing || preset.SelectedIndex < 0) return;
            ShowColor(preset.SelectedIndex == 0 ? null : BookmarkPalette[preset.SelectedIndex - 1].Hex);
            Check();
        };
        code.TextChanged += (_, _) =>
        {
            if (syncing) return;
            string? color = Parsed(out bool valid);
            if (valid)
            {
                syncing = true;
                preset.SelectedIndex = color is null ? 0 : Array.FindIndex(BookmarkPalette, p => p.Hex == color) + 1;
                syncing = false;
            }
            Check();
        };
        ShowColor(initial.Color);

        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                Row(Loc.Get(K.HexBmName), name),
                Row(Loc.Get(K.HexBmColor), preset),
                Row(Loc.Get(K.HexBmColorCode), new StackPanel { Spacing = 2, Children = { code, error } }),
                Row(Loc.Get(K.HexBmComment), comment),
                Row(Loc.Get(K.HexBmRegion), new TextBlock { Text = initial.Region, VerticalAlignment = VerticalAlignment.Center, FontFamily = Mono }),
                Row(Loc.Get(K.HexBmType), new TextBlock { Text = initial.Type ?? Loc.Get(K.HexBmTypeNone), VerticalAlignment = VerticalAlignment.Center }),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        var dialog = Frame(Loc.Get(K.HexBmDialogTitle), panel, 480, "hex.bookmarks");
        ok.Click += (_, _) => dialog.Close(new HexBookmarkForm(name.Text ?? "", Parsed(out _), string.IsNullOrWhiteSpace(comment.Text) ? null : comment.Text, initial.Type, initial.Region));
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { name.Focus(); name.SelectAll(); };
        return await ShowAsync<HexBookmarkForm?>(dialog, owner);
    }
}
