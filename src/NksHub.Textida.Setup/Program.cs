using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using NksHub.Textida.Install;

[assembly: SupportedOSPlatform("windows")]

namespace NksHub.Textida.Setup;

public static partial class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // a GUI program on Windows: reattach to the launching console so the messages of the silent mode are visible there
        AttachConsole(-1);
        var command = SetupCommand.Parse(args);
        if (command.Error is { } problem)
        {
            Console.Error.WriteLine("TextidaSetup: " + problem);
            Console.Error.WriteLine(SetupCommand.Usage);
            return 2;
        }

        var env = SetupEnvironment.ForThisUser(command);
        try
        {
            if (!command.NeedsNoWindow)
            {
                SetupApp.Startup = (command, env);
                NativeBundle.Prepare(env.SetupExe);
                var result = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                env.RunPendingRemoval(); // the window is closed, so the uninstaller is about to end
                return result;
            }
            if (RelocateIfInside(command, env, args)) return 0;
            return SetupRunner.Run(command, env, Console.Out, Console.Error);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine("TextidaSetup: " + e.Message);
            return 1;
        }
    }

    /// <summary>The window is drawn by the software renderer: a setup needs no GPU, and it starts the same on a machine without one.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<SetupApp>().UsePlatformDetect().With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] }).LogToTrace();

    /// <summary>
    /// A folder cannot be moved from under the program that runs from it. A switch of the folders started from the install folder therefore starts a copy of the setup from a temporary folder,
    /// which waits for this process to end, and returns true.
    /// </summary>
    private static bool RelocateIfInside(SetupCommand command, SetupEnvironment env, string[] args)
    {
        if (command.Mode is not (SetupMode.ApplyUpdate or SetupMode.Rollback) || command.TempCopy || env.SetupExe is not { } self) return false;
        var dir = command.Dir ?? new SetupModel(env).Installed?.Dir;
        if (dir is null || !SetupModel.IsInside(self, dir)) return false;

        if (command.WaitPid > 0) env.WaitForProcess(command.WaitPid);
        var temp = Path.Combine(Path.GetTempPath(), "textida-setup-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(temp);
        var copy = Path.Combine(temp, Path.GetFileName(self));
        PayloadFile.CopyWithoutPayload(self, copy);
        var start = new ProcessStartInfo(copy) { UseShellExecute = false, WorkingDirectory = temp };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        foreach (var argument in new[] { "--temp-copy", "--wait-pid", Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
        Process.Start(start)?.Dispose();
        return true;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
