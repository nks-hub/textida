using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using NksPad.App.Profile;
using NksPad.App.Startup;

namespace NksPad.App;

public static partial class Program
{
    /// <summary>Name of the single-instance channel of normal runs.</summary>
    internal const string MainChannel = "main";

    private const string Usage = "usage: textida [--new-window] [--wait] [--line N] [--] [path ...]";

    [STAThread]
    public static int Main(string[] args)
    {
        // The executable is a GUI program on Windows; reattach to the launching console so errors and --wait output are visible there.
        if (OperatingSystem.IsWindows()) AttachConsole(-1);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var parsed = CommandLine.Parse(args, Directory.GetCurrentDirectory());
        if (parsed.Error is { } error)
        {
            Console.Error.WriteLine("textida: " + error);
            Console.Error.WriteLine(Usage);
            return 2;
        }

        IDisposable? instanceLock = null;
        if (!parsed.Smoke)
        {
            instanceLock = IpcEndpoint.TryAcquire(MainChannel);
            if (instanceLock is null) return ForwardToRunningInstance(parsed);
        }

        App.Startup = new StartupContext(parsed, new ProfileStore(ProfileLocation.ResolveForThisMachine()), parsed.Smoke ? "smoke" : MainChannel, instanceLock);
        try { return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { instanceLock?.Dispose(); }
    }

    private static int ForwardToRunningInstance(CommandLineArgs parsed)
    {
        var request = new IpcRequest { NewWindow = parsed.NewWindow, Wait = parsed.Wait, Files = parsed.Files.Select(f => new IpcFile { Path = f.Path, Line = f.Line }).ToList() };
        try
        {
            var reply = IpcClient.SendAsync(MainChannel, request, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            if (reply.Ok) return 0;
            Console.Error.WriteLine("textida: " + reply.Error);
            return 1;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("textida: " + e.Message);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int processId);
}
