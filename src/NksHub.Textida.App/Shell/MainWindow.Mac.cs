using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, System Integration, macOS Integration / Remove Textida from macOS: the command in the terminal, the quick action for Finder and the file types of one user, written by the program itself
/// (<see cref="Install.MacIntegration"/>). Nothing is added until the user switches it on.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The integration window while it is open.</summary>
    internal MacIntegrationWindow? MacWindow { get; private set; }

    internal CommandOutcome ShowMacIntegration()
    {
        if (MacHost.Open() is not { } integration)
        {
            SetStatusMessage(Loc.Get(K.MacUnavailable));
            return CommandOutcome.Handled;
        }
        if (MacWindow is { } open)
        {
            open.Activate();
            return CommandOutcome.Handled;
        }
        var window = new MacIntegrationWindow(integration);
        window.Closed += (_, _) => MacWindow = null;
        MacWindow = window;
        window.Show(this);
        return CommandOutcome.Handled;
    }

    internal CommandOutcome RemoveMacIntegration()
    {
        if (MacHost.Open() is not { } integration)
        {
            SetStatusMessage(Loc.Get(K.MacUnavailable));
            return CommandOutcome.Handled;
        }
        try
        {
            integration.RemoveAll();
            SetStatusMessage(Loc.Get(K.MacRemoved));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatusMessage(Loc.Get(K.MacFailed, ("reason", e.Message)));
        }
        MacWindow?.Section.Refresh();
        return CommandOutcome.Handled;
    }
}
