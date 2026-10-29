using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Text;
using NksHub.Textida.Install;

namespace NksHub.Textida.App.Shell;

/// <summary>The window of the macOS integration: the section with its switches (<see cref="MacIntegrationSection"/>) and a Close button. Tools, System Integration, macOS Integration opens it.</summary>
internal sealed class MacIntegrationWindow : Window
{
    private readonly Button _close = new() { IsCancel = true, MinWidth = 84, HorizontalAlignment = HorizontalAlignment.Right };

    public MacIntegrationWindow(MacIntegration integration)
    {
        Section = new MacIntegrationSection(integration);
        Classes.Add("shell");
        HelpContext.OnF1(this, "tools.macIntegration");
        Width = 640;
        MinWidth = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _close.Click += (_, _) => Close();
        Content = new ReadingDockPanel { Children = { new StackPanel { Margin = new Thickness(18, 14), Spacing = 12, Children = { Section, _close } } } };
        // subscribed only while open, so a window that is never shown or is closed does not stay reachable from Loc
        Opened += (_, _) => { Loc.Changed += OnLanguageChanged; ApplyTexts(); Section.Refresh(); };
        Closed += (_, _) => Loc.Changed -= OnLanguageChanged;
        ApplyTexts();
    }

    /// <summary>The switches; the settings can take this control over.</summary>
    internal MacIntegrationSection Section { get; }

    private void OnLanguageChanged()
    {
        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(OnLanguageChanged);
            return;
        }
        ApplyTexts();
        Section.Refresh();
    }

    /// <summary>Sets every text and name from the catalogue; again when the language changes.</summary>
    internal void ApplyTexts()
    {
        Title = Loc.Get(K.MacTitle);
        AutomationProperties.SetName(this, Loc.Get(K.A11yMacWindow));
        _close.Content = Loc.Get(K.DialogClose);
        AutomationProperties.SetName(_close, Loc.Get(K.DialogClose));
        Section.ApplyTexts();
    }
}
