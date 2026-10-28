using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// The content of the colour picker: a preview, sliders for hue, saturation, lightness and opacity, and the value as it will be written (in the notation of the
/// colour that was clicked). Typing a colour in any notation in the value box sets the sliders. <see cref="Color"/> is what the document gets when the picker closes.
/// </summary>
internal sealed class ColorPickerPanel : StackPanel
{
    private readonly Border _preview = new() { Height = 28, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(110, 128, 128, 128)) };
    private readonly Slider _hue, _saturation, _lightness, _opacity;
    private readonly TextBox _value = new();
    private bool _updating;

    public ColorPickerPanel(ColorMatch match)
    {
        HelpContext.Set(this, "tools.colors");
        Match = match;
        Width = 270;
        Spacing = 8;
        Margin = new Thickness(6);
        AutomationProperties.SetName(this, Loc.Get(K.A11yColorPicker));
        _hue = Row(K.ColorPickerHue, 360);
        _saturation = Row(K.ColorPickerSaturation, 100);
        _lightness = Row(K.ColorPickerLightness, 100);
        _opacity = Row(K.ColorPickerOpacity, 100);
        Children.Insert(0, _preview);
        var label = new TextBlock { Text = Loc.Get(K.ColorPickerValue), MinWidth = 82, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(_value, label.Text);
        DockPanel.SetDock(label, Avalonia.Controls.Dock.Left);
        Children.Add(new DockPanel { Children = { label, _value } });
        Children.Add(new TextBlock { Text = Loc.Get(K.ColorPickerHint), Opacity = 0.65, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        _value.TextChanged += (_, _) => FromText();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            CancelRequested?.Invoke();
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Show(match.Color, fromText: false);
    }

    /// <summary>The colour that was clicked, with the notation the result is written in.</summary>
    public ColorMatch Match { get; }

    /// <summary>The colour as the picker has it now.</summary>
    public Rgba Color { get; private set; }

    /// <summary>Escape: close without changing the document.</summary>
    public event Action? CancelRequested;

    /// <summary>The value box, the sliders and the preview all show <see cref="Color"/> after this; for tests.</summary>
    internal TextBox ValueBox => _value;

    internal (double Hue, double Saturation, double Lightness, double Opacity) Sliders => (_hue.Value, _saturation.Value, _lightness.Value, _opacity.Value);

    /// <summary>Moves the sliders as a user would; the colour and the value follow.</summary>
    internal void SetSliders(double hue, double saturation, double lightness, double opacity)
    {
        _hue.Value = hue;
        _saturation.Value = saturation;
        _lightness.Value = lightness;
        _opacity.Value = opacity;
    }

    private Slider Row(string labelKey, double maximum)
    {
        var slider = new Slider { Minimum = 0, Maximum = maximum, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock { Text = Loc.Get(labelKey), MinWidth = 82, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, label.Text);
        DockPanel.SetDock(label, Avalonia.Controls.Dock.Left);
        Children.Add(new DockPanel { Children = { label, slider } });
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) FromSliders();
        };
        return slider;
    }

    private void FromSliders()
    {
        if (_updating) return;
        Show(ColorLiterals.FromHsl(_hue.Value, _saturation.Value / 100, _lightness.Value / 100, _opacity.Value / 100), fromText: false, keepSliders: true);
    }

    private void FromText()
    {
        if (_updating || !ColorLiterals.TryParse(_value.Text?.Trim() ?? "", out var typed)) return;
        Show(typed.Color, fromText: true);
    }

    private void Show(Rgba color, bool fromText, bool keepSliders = false)
    {
        _updating = true;
        try
        {
            Color = color;
            _preview.Background = new SolidColorBrush(Avalonia.Media.Color.FromArgb((byte)Math.Round(color.A * 255), color.R, color.G, color.B));
            if (!keepSliders)
            {
                var (h, s, l) = ColorLiterals.ToHsl(color);
                _hue.Value = h;
                _saturation.Value = s * 100;
                _lightness.Value = l * 100;
                _opacity.Value = color.A * 100;
            }
            if (!fromText) _value.Text = ColorLiterals.FormatLike(Match, color);
        }
        finally { _updating = false; }
    }
}
