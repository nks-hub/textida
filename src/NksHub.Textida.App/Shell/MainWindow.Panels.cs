using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The output window below the editor and the full screen mode. Hidden until asked for (View menu or key); it shows one channel at a time,
/// read from the files of <see cref="OutputChannels"/>, so it is the same after a crash of the tool that wrote them.
/// </summary>
internal sealed partial class MainWindow
{
    private static readonly FontFamily OutputFont = new("Cascadia Mono, Consolas, Menlo, monospace");

    private readonly Border _outputPanel = new() { IsVisible = false, Height = 200 };
    private readonly TextBox _outputText = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = OutputFont, FontSize = 12 };
    private readonly Dictionary<OutputChannel, ToggleButton> _outputTabs = [];
    private readonly Button _outputClear = new();
    private readonly Button _outputClose = new();
    private OutputChannel _outputChannel = OutputChannel.Tasks;
    private bool _outputDirty;
    private WindowState _beforeFullScreen = WindowState.Normal;

    public bool OutputPanelOpen => _outputPanel.IsVisible;
    public bool IsFullScreen => WindowState == WindowState.FullScreen;
    internal OutputChannel ShownOutputChannel => _outputChannel;
    internal string OutputPanelText => _outputText.Text ?? "";
    internal Control OutputPanelControl => _outputPanel;

    private Control BuildOutputPanel()
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var channel in Enum.GetValues<OutputChannel>())
        {
            var tab = new ToggleButton { IsChecked = channel == _outputChannel };
            tab.Classes.Add("outputtab");
            var shown = channel;
            tab.Click += (_, _) => ShowOutput(shown);
            _outputTabs[channel] = tab;
            tabs.Children.Add(tab);
        }
        _outputClear.Classes.Add("outputaction");
        _outputClear.Click += (_, _) => _app.Output.Clear(_outputChannel);
        _outputClose.Classes.Add("outputaction");
        _outputClose.Click += (_, _) => ToggleOutput(false);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 0, 8, 0), Children = { _outputStop, _outputClear, _outputClose } };
        var header = new DockPanel { Height = 30, LastChildFill = false };
        header.Children.Add(Dock(actions, Avalonia.Controls.Dock.Right));
        header.Children.Add(Dock(tabs, Avalonia.Controls.Dock.Left));

        _outputText.Classes.Add("outputtext");
        _outputPanel.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter && e.Source == _outputText)
            {
                e.Handled = OpenOutputLineAt(_outputText.CaretIndex);
                return;
            }
            if (e.Key != Key.Escape) return;
            ToggleOutput(false);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        WireOutputClicks();

        _outputPanel.Classes.Add("outputpanel");
        _outputPanel.Child = new DockPanel { Children = { Dock(header, Avalonia.Controls.Dock.Top), _outputText } };
        _app.Output.Appended += OnOutputChanged;
        _app.Output.Cleared += OnOutputCleared;
        return _outputPanel;
    }

    private void ReleaseOutput()
    {
        _app.Output.Appended -= OnOutputChanged;
        _app.Output.Cleared -= OnOutputCleared;
    }

    private void ApplyOutputTexts()
    {
        _outputTabs[OutputChannel.Tasks].Content = Loc.Get(K.PanelOutputTasks);
        _outputTabs[OutputChannel.Search].Content = Loc.Get(K.PanelOutputSearch);
        _outputTabs[OutputChannel.Remote].Content = Loc.Get(K.PanelOutputRemote);
        _outputStop.Content = Loc.Get(K.PanelOutputStop);
        AutomationProperties.SetName(_outputStop, Loc.Get(K.PanelOutputStop));
        _outputClear.Content = Loc.Get(K.PanelOutputClear);
        _outputClose.Content = Loc.Get(K.PanelOutputClose);
        _outputText.PlaceholderText = Loc.Get(K.PanelOutputEmpty);
        AutomationProperties.SetName(_outputPanel, Loc.Get(K.A11yOutputPanel));
        AutomationProperties.SetName(_outputText, Loc.Get(K.A11yOutputText));
        foreach (var (channel, tab) in _outputTabs) AutomationProperties.SetName(tab, Loc.Get(channel switch { OutputChannel.Tasks => K.A11yOutputTasks, OutputChannel.Search => K.A11yOutputSearch, _ => K.A11yOutputRemote }));
        AutomationProperties.SetName(_outputClear, Loc.Get(K.PanelOutputClear));
        AutomationProperties.SetName(_outputClose, Loc.Get(K.PanelOutputClose));
        if (_outputPanel.IsVisible) RefreshOutput();
    }

    public void ToggleOutput(bool? open = null)
    {
        bool show = open ?? !OutputPanelOpen;
        if (show == OutputPanelOpen) return;
        _outputPanel.IsVisible = show;
        _menuModel = BuildMenuModel();
        if (show)
        {
            RefreshOutput();
            Dispatcher.UIThread.Post(() => _outputText.Focus(), DispatcherPriority.Input);
        }
        else if (Active is { } tab) FocusView(tab);
    }

    /// <summary>Opens the output window on a channel.</summary>
    public void ShowOutput(OutputChannel channel)
    {
        _outputChannel = channel;
        foreach (var (c, tab) in _outputTabs) tab.IsChecked = c == channel;
        if (!OutputPanelOpen) ToggleOutput(true);
        else RefreshOutput();
    }

    private void OnOutputChanged(OutputChannel channel, OutputLine line)
    {
        if (channel != _outputChannel || _outputDirty) return;
        _outputDirty = true;
        Dispatcher.UIThread.Post(RefreshOutput, DispatcherPriority.Background);
    }

    private void OnOutputCleared(OutputChannel channel) => Dispatcher.UIThread.Post(() => { if (channel == _outputChannel) RefreshOutput(); });

    private void RefreshOutput()
    {
        _outputDirty = false;
        var sb = new StringBuilder();
        _outputShown = [.. _app.Output.Read(_outputChannel)];
        foreach (var line in _outputShown)
        {
            if (line.Time != default) sb.Append(line.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("  ");
            if (line.Source.Length > 0) sb.Append(line.Source).Append("  ");
            sb.Append(line.Text).Append('\n');
        }
        _outputText.Text = sb.ToString();
        _outputText.CaretIndex = _outputText.Text.Length;
    }

    // ---- full screen ----

    public void ToggleFullScreen(bool? on = null)
    {
        bool enter = on ?? !IsFullScreen;
        if (enter == IsFullScreen) return;
        if (enter)
        {
            _beforeFullScreen = WindowState;
            WindowState = WindowState.FullScreen;
        }
        else WindowState = _beforeFullScreen;
        _menuModel = BuildMenuModel();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // the system moves the keyboard to the menu while the window changes size; the editor gets it back once that is over
        if (change.Property == WindowStateProperty && (IsFullScreen || change.OldValue is WindowState.FullScreen))
            DispatcherTimer.RunOnce(() => { if (Active is { } tab && !IsPaletteOpen && !OutputPanelOpen) FocusView(tab); }, TimeSpan.FromMilliseconds(200));
    }
}
