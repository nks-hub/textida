using System.Runtime.Versioning;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Install;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, Explorer Menu / Remove Explorer Menu / File Associations: the integration of one user with Windows Explorer, written by the program itself (<see cref="SystemIntegration"/>).
/// The same switches are in the settings, under System integration.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The last Explorer integration command; tests wait for it.</summary>
    internal Task ShellTask { get; private set; } = Task.CompletedTask;

    internal CommandOutcome AddShellMenu() => OperatingSystem.IsWindows() ? RunShell(AddMenu) : Unavailable();

    internal CommandOutcome RemoveShellIntegration() => OperatingSystem.IsWindows() ? RunShell(RemoveExplorerEntries) : Unavailable();

    internal CommandOutcome ShowShellAssociations()
    {
        if (!OperatingSystem.IsWindows()) return Unavailable();
        ShellTask = ChooseShellAssociationsAsync();
        return CommandOutcome.Handled;
    }

    [SupportedOSPlatform("windows")]
    private static string AddMenu(SystemIntegration shell)
    {
        shell.SetMenu(true, Loc.Get(K.ShellMenuLabel));
        return K.ShellMenuAdded;
    }

    [SupportedOSPlatform("windows")]
    private static string RemoveExplorerEntries(SystemIntegration shell)
    {
        shell.RemoveExplorerIntegration();
        return K.ShellRemoved;
    }

    private CommandOutcome Unavailable()
    {
        ShellTask = Task.CompletedTask;
        SetStatusMessage(Loc.Get(K.ShellUnavailable));
        return CommandOutcome.Handled;
    }

    [SupportedOSPlatform("windows")]
    private CommandOutcome RunShell(Func<SystemIntegration, string> run)
    {
        ShellTask = Task.CompletedTask;
        if (OpenShell() is not { } shell) return CommandOutcome.Handled;
        try
        {
            SetStatusMessage(Loc.Get(run(shell), ("program", shell.ExePath)));
            shell.Notify();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            SetStatusMessage(Loc.Get(K.ShellFailed, ("reason", e.Message)));
        }
        return CommandOutcome.Handled;
    }

    [SupportedOSPlatform("windows")]
    private SystemIntegration? OpenShell()
    {
        var shell = ShellHost.Open();
        if (shell is null) SetStatusMessage(Loc.Get(K.ShellUnavailable));
        return shell;
    }

    /// <summary>Lists the extensions with their state; choosing one switches it, and the list stays until it is dismissed.</summary>
    [SupportedOSPlatform("windows")]
    private async Task ChooseShellAssociationsAsync()
    {
        if (OpenShell() is not { } shell) return;
        try
        {
            while (true)
            {
                var items = ShellIntegration.Extensions.Select(e => new PickItem(e, e, Loc.Get(shell.IsAssociated(e) ? K.ShellAssocOn : K.ShellAssocOff))).ToList();
                var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.ShellAssocTitle), Loc.Get(K.ShellAssocWatermark), items), CancellationToken.None);
                if (chosen is null) break;
                shell.SetAssociated(chosen, !shell.IsAssociated(chosen));
                shell.Notify();
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            SetStatusMessage(Loc.Get(K.ShellFailed, ("reason", e.Message)));
        }
    }
}
