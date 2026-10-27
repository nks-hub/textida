using Microsoft.Win32;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, Explorer Menu / Remove Explorer Menu / File Associations: the integration of one user with Windows Explorer, written by the program itself (<see cref="ShellIntegration"/>).
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The last Explorer integration command; tests wait for it.</summary>
    internal Task ShellTask { get; private set; } = Task.CompletedTask;

    internal CommandOutcome AddShellMenu() => RunShell(s => { s.AddMenu(Loc.Get(K.ShellMenuLabel)); return K.ShellMenuAdded; });

    internal CommandOutcome RemoveShellIntegration() => RunShell(s => { s.RemoveAll(); return K.ShellRemoved; });

    internal CommandOutcome ShowShellAssociations()
    {
        ShellTask = ChooseShellAssociationsAsync();
        return CommandOutcome.Handled;
    }

    private CommandOutcome RunShell(Func<ShellIntegration, string> run)
    {
        ShellTask = Task.CompletedTask;
        if (OpenShell() is not { } shell) return CommandOutcome.Handled;
        try
        {
            using (shell.Key) SetStatusMessage(Loc.Get(run(shell.Integration), ("program", shell.Exe)));
            NotifyExplorer();
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            SetStatusMessage(Loc.Get(K.ShellFailed, ("reason", e.Message)));
        }
        return CommandOutcome.Handled;
    }

    private static void NotifyExplorer()
    {
        if (ShellIntegration.ClassesOverride is null) ShellIntegration.NotifyExplorer();
    }

    private (ShellIntegration Integration, RegistryKey Key, string Exe)? OpenShell()
    {
        var exe = ShellIntegration.ExeOverride ?? Environment.ProcessPath;
        if (!OperatingSystem.IsWindows() || exe is null || !Path.GetFileName(exe).Equals("Textida.exe", StringComparison.OrdinalIgnoreCase))
        {
            SetStatusMessage(Loc.Get(K.ShellUnavailable));
            return null;
        }
        var key = (ShellIntegration.ClassesOverride ?? ShellIntegration.OpenUserClasses)();
        return (new ShellIntegration(key, exe), key, exe);
    }

    /// <summary>Lists the extensions with their state; choosing one switches it, and the list stays until it is dismissed.</summary>
    private async Task ChooseShellAssociationsAsync()
    {
        if (OpenShell() is not { } shell) return;
        using var key = shell.Key;
        try
        {
            while (true)
            {
                var items = ShellIntegration.Extensions.Select(e => new PickItem(e, e, Loc.Get(shell.Integration.IsAssociated(e) ? K.ShellAssocOn : K.ShellAssocOff))).ToList();
                var chosen = await PluginPickAsync(new QuickPickRequest(Loc.Get(K.ShellAssocTitle), Loc.Get(K.ShellAssocWatermark), items), CancellationToken.None);
                if (chosen is null) break;
                if (shell.Integration.IsAssociated(chosen)) shell.Integration.Dissociate(chosen);
                else shell.Integration.Associate(chosen);
                NotifyExplorer();
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            SetStatusMessage(Loc.Get(K.ShellFailed, ("reason", e.Message)));
        }
    }
}
