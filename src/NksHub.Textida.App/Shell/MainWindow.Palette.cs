using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    private readonly Border _palette = new() { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 36, 0, 0), Width = 580 };
    private readonly TextBox _paletteInput = new();
    private readonly ListBox _paletteList = new() { MaxHeight = 360, Background = Brushes.Transparent };
    private readonly TextBlock _paletteEmpty = new() { Margin = new Thickness(14, 8), IsVisible = false };
    private List<PaletteItem> _paletteItems = [];
    private DocTab? _paletteReturnTo;

    internal bool IsPaletteOpen => _palette.IsVisible;
    internal TextBox PaletteInput => _paletteInput;
    internal IReadOnlyList<PaletteItem> PaletteResults => _paletteItems;

    private Control BuildPalette()
    {
        _palette.Classes.Add("palette");
        var stack = new StackPanel { Children = { _paletteInput, _paletteList, _paletteEmpty } };
        _palette.Child = stack;
        _paletteInput.Classes.Add("paletteinput");
        _paletteInput.TextChanged += (_, _) => RefreshPalette();
        _paletteInput.AddHandler(KeyDownEvent, OnPaletteKey, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _paletteList.DoubleTapped += (_, _) => RunPaletteSelection();
        return _palette;
    }

    private void ApplyPaletteTexts()
    {
        _paletteInput.PlaceholderText = Loc.Get(K.PaletteWatermark);
        _paletteEmpty.Text = Loc.Get(K.PaletteNoResults);
        AutomationProperties.SetName(_paletteInput, Loc.Get(K.A11yPalette));
        AutomationProperties.SetName(_paletteList, Loc.Get(K.A11yPaletteResults));
    }

    public void ShowPalette()
    {
        _paletteReturnTo = Active;
        _palette.IsVisible = true;
        _paletteInput.Text = "";
        RefreshPalette();
        Dispatcher.UIThread.Post(() => _paletteInput.Focus(), DispatcherPriority.Input);
    }

    public void ClosePalette()
    {
        if (!_palette.IsVisible) return;
        _palette.IsVisible = false;
        if (_paletteReturnTo is { } tab && tab.Group is not null) FocusView(tab);
    }

    internal void SetPaletteQuery(string text)
    {
        _paletteInput.Text = text;
        RefreshPalette();
    }

    private void RefreshPalette()
    {
        var wb = _app.Workbench;
        var context = new ShellContext(this, forMenu: true);
        _paletteItems = [.. CommandPalette.Query(wb.Commands, wb.Keymap, wb.Strings, context, _paletteInput.Text ?? "", 400).Where(i => wb.Commands.HasHandler(i.CommandId)).Take(60)];
        _paletteList.Items.Clear();
        foreach (var item in _paletteItems)
        {
            var title = new TextBlock { Text = item.Title, VerticalAlignment = VerticalAlignment.Center };
            var category = new TextBlock { Text = item.Category ?? "", Opacity = 0.6, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            var shortcut = new TextBlock { Text = item.Shortcut ?? "", Opacity = 0.6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(6, 2), Opacity = item.Enabled ? 1 : 0.5 };
            Grid.SetColumn(title, 1);
            Grid.SetColumn(shortcut, 2);
            row.Children.Add(category);
            row.Children.Add(title);
            row.Children.Add(shortcut);
            _paletteList.Items.Add(new ListBoxItem { Content = row, IsEnabled = true, Tag = item.CommandId });
            AutomationProperties.SetName(row, (item.Category is null ? "" : item.Category + ": ") + item.Title);
        }
        if (_paletteItems.Count > 0) _paletteList.SelectedIndex = 0;
        _paletteList.IsVisible = _paletteItems.Count > 0;
        _paletteEmpty.IsVisible = _paletteItems.Count == 0;
    }

    private void OnPaletteKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down: MovePalette(1); e.Handled = true; break;
            case Key.Up: MovePalette(-1); e.Handled = true; break;
            case Key.Enter: RunPaletteSelection(); e.Handled = true; break;
            case Key.Escape: ClosePalette(); e.Handled = true; break;
        }
    }

    private void MovePalette(int step)
    {
        if (_paletteItems.Count == 0) return;
        _paletteList.SelectedIndex = Math.Clamp(_paletteList.SelectedIndex + step, 0, _paletteItems.Count - 1);
        _paletteList.ScrollIntoView(_paletteList.SelectedIndex);
    }

    internal void RunPaletteSelection()
    {
        if (_paletteList.SelectedIndex is not (>= 0 and var index) || index >= _paletteItems.Count) return;
        var item = _paletteItems[index];
        ClosePalette();
        _ = _app.Workbench.Commands.ExecuteAsync(item.CommandId, new ShellContext(this, forMenu: true));
    }
}
