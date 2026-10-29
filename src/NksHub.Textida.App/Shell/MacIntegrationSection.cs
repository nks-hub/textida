using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Install;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What Textida adds to macOS for the user, as switches: the <c>textida</c> command in the terminal, the quick action "Open in Textida" for Finder, the file types that open in Textida, and the button that
/// takes all of it back. Nothing is kept in the settings: every switch shows what the disk and Launch Services say, and a change is written at once. A switch that the system refuses goes back, with the
/// reason shown. It is a control of its own, so a window shows it now and the settings can hold it.
/// </summary>
internal sealed class MacIntegrationSection : StackPanel
{
    private readonly MacIntegration _integration;
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _notInBundle = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _command = new(), _finder = new();
    private readonly TextBlock _commandLink = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _commandHint = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _finderNote = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly TextBlock _typesTitle = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _typesNote = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly WrapPanel _types = new() { Orientation = Orientation.Horizontal };
    private readonly List<(string Extension, CheckBox Box)> _extensions = [];
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false, Foreground = ThemeColors.Danger };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _removeAll = new() { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Left };
    private bool _syncing;

    public MacIntegrationSection(MacIntegration integration)
    {
        _integration = integration;
        Spacing = 6;
        foreach (var extension in IntegrationExtensions.All)
        {
            var box = new CheckBox { Content = extension, MinWidth = 84 };
            box.IsCheckedChanged += (_, _) => Apply(() => _integration.SetAssociated(extension, box.IsChecked == true));
            _extensions.Add((extension, box));
            _types.Children.Add(box);
        }
        _command.IsCheckedChanged += (_, _) => Apply(() =>
        {
            if (_command.IsChecked == true) _integration.AddCommand();
            else _integration.RemoveCommand();
        });
        _finder.IsCheckedChanged += (_, _) => Apply(() =>
        {
            if (_finder.IsChecked == true) _integration.AddFinderAction(Loc.Get(K.MacActionLabel));
            else _integration.RemoveFinderAction();
        });
        _removeAll.Click += (_, _) =>
        {
            if (!Apply(_integration.RemoveAll)) return;
            _result.Text = Loc.Get(K.MacRemoved);
            _result.IsVisible = true;
        };
        foreach (var child in new Control[] { _description, _notInBundle, _command, _commandLink, _commandHint, _finder, _finderNote, _typesTitle, _types, _typesNote, _removeAll, _problem, _result })
            Children.Add(child);
        ApplyTexts();
        Refresh();
    }

    internal CheckBox CommandBox => _command;
    internal CheckBox FinderBox => _finder;
    internal Button RemoveAllButton => _removeAll;
    internal IReadOnlyList<(string Extension, CheckBox Box)> Extensions => _extensions;
    internal string CommandLinkText => _commandLink.IsVisible ? _commandLink.Text ?? "" : "";
    internal string CommandHintText => _commandHint.IsVisible ? _commandHint.Text ?? "" : "";
    internal string NotInBundleText => _notInBundle.IsVisible ? _notInBundle.Text ?? "" : "";
    internal string ProblemText => _problem.IsVisible ? _problem.Text ?? "" : "";
    internal string ResultText => _result.IsVisible ? _result.Text ?? "" : "";

    /// <summary>Sets every text and name from the catalogue; again when the language changes.</summary>
    internal void ApplyTexts()
    {
        _description.Text = Loc.Get(K.MacDescription);
        _notInBundle.Text = Loc.Get(K.MacNotInBundle);
        _command.Content = Loc.Get(K.MacCli);
        _finder.Content = Loc.Get(K.MacFinder);
        _finderNote.Text = Loc.Get(K.MacFinderNote);
        _typesTitle.Text = Loc.Get(K.MacAssoc);
        _typesNote.Text = Loc.Get(K.MacAssocNote);
        _removeAll.Content = Loc.Get(K.MacRemoveAll);
        AutomationProperties.SetName(this, Loc.Get(K.A11yMacWindow));
        AutomationProperties.SetName(_command, Loc.Get(K.MacCli));
        AutomationProperties.SetName(_finder, Loc.Get(K.MacFinder));
        AutomationProperties.SetName(_removeAll, Loc.Get(K.MacRemoveAll));
        AutomationProperties.SetName(_types, Loc.Get(K.MacAssoc));
        foreach (var (extension, box) in _extensions) AutomationProperties.SetName(box, Loc.Get(K.MacAssocItem, ("extension", extension)));
        AutomationProperties.SetLiveSetting(_problem, AutomationLiveSetting.Assertive);
        AutomationProperties.SetLiveSetting(_result, AutomationLiveSetting.Polite);
    }

    /// <summary>Runs a change and shows the state again; when macOS refuses, the switch goes back and the reason is shown. False when it was refused.</summary>
    private bool Apply(Action change)
    {
        if (_syncing) return true;
        bool done = true;
        try
        {
            change();
            _problem.IsVisible = false;
            _result.IsVisible = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _problem.Text = Loc.Get(K.MacFailed, ("reason", e.Message));
            _problem.IsVisible = true;
            done = false;
        }
        Refresh();
        return done;
    }

    /// <summary>Reads the state again from the disk and Launch Services and shows it.</summary>
    internal void Refresh()
    {
        _syncing = true;
        try
        {
            bool bundle = _integration.BundlePath is not null;
            _notInBundle.IsVisible = !bundle;
            var link = _integration.CommandLink();
            _command.IsChecked = link is not null;
            _commandLink.IsVisible = link is not null;
            _commandLink.Text = link is null ? "" : Loc.Get(K.MacCliLink, ("path", link));
            bool needsPath = link is not null && link.StartsWith(_integration.UserBinDir, StringComparison.Ordinal);
            _commandHint.IsVisible = needsPath;
            _commandHint.Text = needsPath ? Loc.Get(K.MacCliHint, ("folder", _integration.UserBinDir), ("line", "export PATH=\"$HOME/bin:$PATH\"")) : "";
            _finder.IsEnabled = bundle;
            _finder.IsChecked = bundle && _integration.HasFinderAction();
            _finderNote.IsVisible = bundle;
            var associated = bundle ? _integration.AssociatedExtensions().ToHashSet(StringComparer.Ordinal) : [];
            foreach (var (extension, box) in _extensions)
            {
                box.IsEnabled = bundle;
                box.IsChecked = associated.Contains(extension);
            }
        }
        finally { _syncing = false; }
    }
}
