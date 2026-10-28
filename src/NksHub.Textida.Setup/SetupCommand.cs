using NksHub.Textida.Install;

namespace NksHub.Textida.Setup;

internal enum SetupMode { Install, Uninstall, ApplyUpdate, Rollback, Check, Help }

/// <summary>
/// What the command line asks for. A choice the line does not name stays <c>null</c>: a first install then takes the default, an update keeps what the installed program has now.
/// </summary>
internal sealed class SetupCommand
{
    public const string Usage = """
        usage: TextidaSetup [options]
          (no options)               the setup window
          --silent                   no window; the result is the exit code (0 done, 1 failed, 2 wrong usage)
          --dir PATH                 install folder (default: the installed one, else %LOCALAPPDATA%\Programs\Textida)
          --menu | --no-menu         Explorer context menu entry
          --start-menu | --no-start-menu   Start menu shortcut
          --desktop | --no-desktop   desktop shortcut
          --assoc LIST | all | none  file types offered under Open with, like .txt,.md
          --launch | --no-launch     start Textida when done (silent: off by default)
          --lang cs|en|sk|de         language of the messages (default: the language of Windows)
          --uninstall                remove Textida (window, or --silent); --delete-settings removes the settings too
          --apply-update DIR         switch the install folder to the unpacked update in DIR (--dir, --wait-pid N, --restart)
          --rollback                 swap the install folder with the previous version (--dir, --wait-pid N, --restart)
          --temp-copy                set on the copy that runs from a temporary folder; it removes itself when done
          --payload FILE             install the package in this ZIP instead of the one inside the setup
          --check                    print what the setup carries and exit
        """;

    public SetupMode Mode { get; private set; } = SetupMode.Install;
    public bool Silent { get; private set; }
    public string? Dir { get; private set; }
    public bool? StartMenu { get; private set; }
    public bool? Desktop { get; private set; }
    public bool? ContextMenu { get; private set; }
    public bool? Launch { get; private set; }
    public IReadOnlyList<string>? Extensions { get; private set; }
    public string? Language { get; private set; }
    public bool DeleteSettings { get; private set; }
    public string? Staged { get; private set; }
    public int WaitPid { get; private set; }
    public bool Restart { get; private set; }
    public string? Payload { get; private set; }

    /// <summary>Set on the copy of the setup that runs from a temporary folder: it removes itself when it is done.</summary>
    public bool TempCopy { get; private set; }

    /// <summary>Path below <c>HKCU</c> of the Apps list entry; the updater of the program passes the key of its tests with it.</summary>
    public string? UninstallKey { get; private set; }

    public string? Error { get; private set; }

    /// <summary>True for the modes that never show a window.</summary>
    public bool NeedsNoWindow => Silent || Mode is SetupMode.ApplyUpdate or SetupMode.Rollback or SetupMode.Check or SetupMode.Help;

    public static SetupCommand Parse(IReadOnlyList<string> args)
    {
        var command = new SetupCommand();
        for (int i = 0; i < args.Count && command.Error is null; i++)
        {
            var arg = args[i];
            string? inline = null;
            int equals = arg.StartsWith("--", StringComparison.Ordinal) ? arg.IndexOf('=') : -1;
            if (equals > 0) { inline = arg[(equals + 1)..]; arg = arg[..equals]; }
            string Value() => inline ?? (i + 1 < args.Count ? args[++i] : command.Fail($"{arg} needs a value"));
            switch (arg.ToLowerInvariant())
            {
                case "--silent": command.Silent = true; break;
                case "--dir": command.Dir = Value(); break;
                case "--menu": command.ContextMenu = true; break;
                case "--no-menu": command.ContextMenu = false; break;
                case "--start-menu": command.StartMenu = true; break;
                case "--no-start-menu": command.StartMenu = false; break;
                case "--desktop": command.Desktop = true; break;
                case "--no-desktop": command.Desktop = false; break;
                case "--launch": command.Launch = true; break;
                case "--no-launch": command.Launch = false; break;
                case "--no-assoc": command.Extensions = []; break;
                case "--assoc": command.Extensions = command.ParseExtensions(Value()); break;
                case "--lang": command.Language = Value(); break;
                case "--uninstall": command.Mode = SetupMode.Uninstall; break;
                case "--delete-settings": command.DeleteSettings = true; break;
                case "--apply-update": command.Mode = SetupMode.ApplyUpdate; command.Staged = Value(); break;
                case "--rollback": command.Mode = SetupMode.Rollback; break;
                case "--wait-pid":
                    if (int.TryParse(Value(), out var pid) && pid >= 0) command.WaitPid = pid;
                    else command.Fail("--wait-pid needs a process number");
                    break;
                case "--restart": command.Restart = true; break;
                case "--temp-copy": command.TempCopy = true; break;
                case "--payload": command.Payload = Value(); break;
                case "--uninstall-key": command.UninstallKey = Value().Replace("HKCU:\\", "", StringComparison.OrdinalIgnoreCase); break;
                case "--check": command.Mode = SetupMode.Check; break;
                case "--help" or "-h" or "-?" or "/?": command.Mode = SetupMode.Help; break;
                default: command.Fail($"unknown option {args[i]}"); break;
            }
        }
        if (command.Error is null && command.Language is not null && !SetupText.Languages.Contains(command.Language.ToLowerInvariant()))
            command.Fail($"unknown language {command.Language} (cs, en, sk, de)");
        return command;
    }

    private string Fail(string message)
    {
        Error ??= message;
        return "";
    }

    private IReadOnlyList<string>? ParseExtensions(string list)
    {
        if (list.Equals("all", StringComparison.OrdinalIgnoreCase)) return ShellIntegration.Extensions;
        if (list.Equals("none", StringComparison.OrdinalIgnoreCase)) return [];
        var chosen = new List<string>();
        foreach (var raw in list.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            var extension = "." + raw.Trim().TrimStart('.').ToLowerInvariant();
            var known = ShellIntegration.Extensions.FirstOrDefault(e => e == extension);
            if (known is null) { Fail($"{raw} is not one of the file types the setup can offer ({string.Join(" ", ShellIntegration.Extensions)})"); return null; }
            if (!chosen.Contains(known)) chosen.Add(known);
        }
        return chosen;
    }
}
