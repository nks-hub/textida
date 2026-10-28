using System.Runtime.Versioning;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup;

/// <summary>
/// The modes that run without a window: <c>--silent</c> install and uninstall, the switch of an update and the return to the previous version, and <c>--check</c>.
/// The result is the exit code, 0 done and 1 failed; the sentence goes to <paramref name="output"/> or <paramref name="error"/> in the language of the system or of <c>--lang</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SetupRunner
{
    public static int Run(SetupCommand command, SetupEnvironment env, TextWriter output, TextWriter error)
    {
        if (command.Mode == SetupMode.Help)
        {
            output.WriteLine(SetupCommand.Usage);
            return 0;
        }
        var model = new SetupModel(env, new SetupText(command.Language ?? env.UiLanguage));
        model.Apply(command);
        return command.Mode switch
        {
            SetupMode.Check => Check(model, output, error),
            SetupMode.Uninstall => Uninstall(model, output, error),
            SetupMode.ApplyUpdate or SetupMode.Rollback => Switch(command, model, output, error),
            _ => Install(model, output, error),
        };
    }

    private static int Check(SetupModel model, TextWriter output, TextWriter error)
    {
        if (!model.HasPackage)
        {
            error.WriteLine(model.Text.Get(S.ErrorNoPackage));
            return 1;
        }
        output.WriteLine($"TextidaSetup: package {model.PackageVersion}");
        return 0;
    }

    private static int Install(SetupModel model, TextWriter output, TextWriter error)
    {
        if (!model.HasPackage)
        {
            error.WriteLine(model.Text.Get(S.ErrorNoPackage));
            return 1;
        }
        try
        {
            var result = model.Install();
            output.WriteLine(model.DoneMessage(result));
            foreach (var warning in result.Warnings) output.WriteLine(model.Text.Get(S.DoneWarning, ("reason", warning)));
            if (model.Launch) model.Env.Launch(result.ExePath, result.Dir);
            return 0;
        }
        catch (SetupException e)
        {
            error.WriteLine(model.Text.Describe(e));
            return 1;
        }
    }

    private static int Uninstall(SetupModel model, TextWriter output, TextWriter error)
    {
        try
        {
            var data = model.Uninstall();
            output.WriteLine(model.Text.Get(model.DeleteSettings ? S.UninstallDoneAll : S.UninstallDone, ("data", data)));
            return 0;
        }
        catch (SetupException e)
        {
            error.WriteLine(model.Text.Describe(e));
            return 1;
        }
    }

    /// <summary>Waits for the editor to end, switches the folders, and starts the editor again when asked, also after a failure, so the user is not left without it.</summary>
    private static int Switch(SetupCommand command, SetupModel model, TextWriter output, TextWriter error)
    {
        var dir = command.Dir ?? model.Installed?.Dir ?? model.Dir;
        var code = 0;
        if (command.WaitPid > 0) model.Env.WaitForProcess(command.WaitPid);
        try
        {
            var installer = model.NewInstaller();
            var version = command.Mode == SetupMode.ApplyUpdate ? installer.ApplyUpdate(dir, command.Staged!) : installer.Rollback(dir);
            output.WriteLine(model.Text.Get(S.DoneSwitched, ("version", version), ("dir", dir), ("previous", dir + ".previous")));
        }
        catch (SetupException e)
        {
            error.WriteLine(model.Text.Describe(e));
            code = 1;
        }
        var exe = Path.Combine(dir, Installer.ProgramFile);
        if (command.Restart && File.Exists(exe)) model.Env.Launch(exe, dir);
        return code;
    }
}
