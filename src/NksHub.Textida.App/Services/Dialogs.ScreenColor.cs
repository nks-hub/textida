using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.App.Services;

internal sealed partial class AvaloniaDialogs
{
    /// <summary>
    /// The colour picker of the screen: a small window shows the colour of the pixel under the pointer, wherever the pointer is, and updates it 20 times a second. Enter or Pick takes it, Esc cancels. The pointer is moved by the user to any
    /// program; this window keeps the keyboard, so the press of Enter reaches it.
    /// </summary>
    public async Task<Rgba?> PickScreenColorAsync(Window owner, Func<(Rgba Color, int X, int Y)?> read)
    {
        var swatch = new Border { Width = 72, Height = 72, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        var value = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 18 };
        var where = new TextBlock { Opacity = 0.7 };
        var hint = new TextBlock { Text = Loc.Get(K.ScreenColorHint), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(swatch, Loc.Get(K.ScreenColorSwatch));
        AutomationProperties.SetLiveSetting(value, AutomationLiveSetting.Off);
        Rgba? current = null;
        void Update()
        {
            if (read() is not { } sample)
            {
                value.Text = Loc.Get(K.ScreenColorUnreadable);
                return;
            }
            current = sample.Color;
            swatch.Background = new SolidColorBrush(Color.FromRgb(sample.Color.R, sample.Color.G, sample.Color.B));
            value.Text = $"#{sample.Color.R:x2}{sample.Color.G:x2}{sample.Color.B:x2}";
            where.Text = Loc.Get(K.ScreenColorAt, ("x", sample.X), ("y", sample.Y));
            AutomationProperties.SetName(value, value.Text);
        }
        Update();
        var texts = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { value, where } };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { swatch, texts } };
        var row = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12, Children = { top, hint, row } };
        var dialog = Frame(Loc.Get(K.ScreenColorTitle), panel, 380);
        dialog.Topmost = true;
        row.Children.Add(MakeButton(new DialogButton(Loc.Get(K.ScreenColorPick), IsDefault: true), dialog, 0));
        row.Children.Add(MakeButton(new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true), dialog, 1));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => Update();
        timer.Start();
        dialog.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Space) { dialog.Close(0); e.Handled = true; }
        };
        try
        {
            int answer = await ShowAsync<int?>(dialog, owner) ?? 1;
            return answer == 0 ? current : null;
        }
        finally { timer.Stop(); }
    }
}
