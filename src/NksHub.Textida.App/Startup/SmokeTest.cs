using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Startup;

/// <summary>
/// <c>--smoke</c>: runs inside the published program (NativeAOT) and checks what a trimmed build could silently lose.
/// It creates a document, reads a file in a single-byte encoding, round-trips a request through the real IPC transport,
/// switches the language and the theme, then exits with 0. Any failure prints the reason and exits with 1.
/// </summary>
internal sealed class SmokeTest(App app, MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
{
    private const string Sample = "smoke";
    private readonly List<string> _log = [];

    public async Task RunAsync()
    {
        var watchdog = Task.Delay(TimeSpan.FromSeconds(60)).ContinueWith(_ => Fail("timeout after 60 s"), TaskScheduler.Default);
        string? temp = null;
        try
        {
            await NextFrameAsync();

            var doc = window.NewDocument();
            Check(window.Tabs.Count == 1 && window.Active == doc, "new document is the only and active tab");
            doc.Editor.Text = Sample;
            Check(doc.Editor.Text == Sample, "editor accepts text");
            _log.Add($"transparency={window.ActualTransparencyLevel}");

            temp = Path.Combine(Path.GetTempPath(), "textida-smoke-" + Environment.ProcessId);
            Directory.CreateDirectory(temp);
            var file = Path.Combine(temp, "latin2.txt");
            // "Příliš žluťoučký kůň" in Windows-1250, then two more lines
            await File.WriteAllBytesAsync(file, [0x50, 0xF8, 0xED, 0x6C, 0x69, 0x9A, 0x20, 0x9E, 0x6C, 0x75, 0x9D, 0x6F, 0x75, 0xE8, 0x6B, 0xFD, 0x20, 0x6B, 0xF9, 0xF2, 0x0D, 0x0A, 0x32, 0x0D, 0x0A, 0x33]);

            var channel = "smoke-" + Environment.ProcessId;
            await using (var server = new IpcServer(channel, request => Dispatcher.UIThread.InvokeAsync(() => app.OpenRequestAsync(request))))
            {
                var reply = await IpcClient.SendAsync(channel, new IpcRequest { Files = [new IpcFile { Path = file, Line = 3 }] }, TimeSpan.FromSeconds(5));
                Check(reply.Ok, $"IPC reply ok ({reply.Error})");
            }
            var opened = window.Active;
            Check(opened is { Path: not null } && opened.Path == file, "IPC request opened the file in a tab");
            Check(opened!.Editor.Text!.StartsWith("Příliš žluťoučký kůň", StringComparison.Ordinal), "Windows-1250 file decoded");
            Check(opened.CaretPosition().Line == 3, "caret is on the requested line");

            var csHeader = window.MenuHeaders[0];
            app.SetLanguage("en");
            Check(window.MenuHeaders[0] != csHeader && window.MenuHeaders.All(h => h.Length > 0), "language switch changes menu headers");
            app.SetLanguage("cs");
            Check(window.MenuHeaders[0] == csHeader, "switch back restores the default language");
            app.SetTheme("dark");
            await NextFrameAsync();
            app.SetTheme("light");
            await NextFrameAsync();
            app.SetTheme("system");

            Console.Out.WriteLine("SMOKE_OK " + string.Join(' ', _log));
            Console.Out.Flush();
            desktop.Shutdown(0);
        }
        catch (Exception e)
        {
            RemoveTemp(temp);
            Fail(e.Message);
        }
        finally
        {
            RemoveTemp(temp);
            GC.KeepAlive(watchdog);
        }
    }

    private static void RemoveTemp(string? dir)
    {
        if (dir is not null) try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private void Check(bool condition, string what)
    {
        if (!condition) throw new InvalidOperationException(what);
        _log.Add("ok:" + what.Replace(' ', '_'));
    }

    private Task NextFrameAsync()
    {
        var tcs = new TaskCompletionSource();
        window.RequestAnimationFrame(_ => window.RequestAnimationFrame(_ => tcs.TrySetResult()));
        return tcs.Task;
    }

    private void Fail(string reason)
    {
        Console.Error.WriteLine("SMOKE_FAIL " + reason);
        Console.Error.Flush();
        Environment.Exit(1);
    }
}
