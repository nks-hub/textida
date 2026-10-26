using System.Text;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Startup;

/// <summary>
/// <c>--smoke</c>: runs inside the published program (NativeAOT) and checks what a trimmed build could silently lose.
/// It edits and saves a single-byte file and checks that only the edited byte changed, opens a file in the paged read-only mode,
/// uses the command palette, the settings view and a split, round-trips a request through the real IPC transport,
/// switches the language and the theme, then exits with 0. Any failure prints the reason and exits with 1.
/// </summary>
internal sealed class SmokeTest(App app, MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
{
    private const string Sample = "smoke";
    private const string Lines = "a\nb\nc";
    private readonly List<string> _log = [];

    public async Task RunAsync()
    {
        var watchdog = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => Fail("timeout after 90 s"), TaskScheduler.Default);
        string? temp = null;
        try
        {
            await NextFrameAsync();

            var doc = window.NewDocument();
            Check(window.Tabs.Count() == 1 && window.Active == doc, "new document is the only and active tab");
            doc.Editor!.Document.Text = Sample;
            Check(doc.Editor.Document.Text == Sample && doc.Doc.Doc.IsModified, "editor accepts text and the document is modified");
            Check(window.StatusRightText.Contains("UTF-8", StringComparison.Ordinal), "status bar shows the encoding");
            _log.Add($"transparency={window.ActualTransparencyLevel}");

            temp = Path.Combine(Path.GetTempPath(), "textida-smoke-" + Environment.ProcessId);
            Directory.CreateDirectory(temp);
            var file = Path.Combine(temp, "latin2.txt");
            // "Příliš žluťoučký kůň" in Windows-1250, then two more lines
            byte[] original = [0x50, 0xF8, 0xED, 0x6C, 0x69, 0x9A, 0x20, 0x9E, 0x6C, 0x75, 0x9D, 0x6F, 0x75, 0xE8, 0x6B, 0xFD, 0x20, 0x6B, 0xF9, 0xF2, 0x0D, 0x0A, 0x32, 0x0D, 0x0A, 0x33];
            await File.WriteAllBytesAsync(file, original);

            var channel = "smoke-" + Environment.ProcessId;
            await using (var server = new IpcServer(channel, request => Dispatcher.UIThread.InvokeAsync(() => app.OpenRequestAsync(request))))
            {
                var reply = await IpcClient.SendAsync(channel, new IpcRequest { Files = [new IpcFile { Path = file, Line = 3 }] }, TimeSpan.FromSeconds(5));
                Check(reply.Ok, $"IPC reply ok ({reply.Error})");
            }
            var opened = window.Active;
            Check(opened is { Path: not null } && opened.Path == file, "IPC request opened the file in a tab");
            Check(opened!.Editor!.Document.Text.StartsWith("Příliš žluťoučký kůň", StringComparison.Ordinal), "Windows-1250 file decoded");
            Check(opened.CaretPosition().Line == 3, "caret is on the requested line");
            Check(!opened.Doc.ReadOnly && window.StatusRightText.Contains("Windows-1250", StringComparison.Ordinal), "the file is editable and the status bar names its encoding");

            opened.Editor.Document.Replace(1, 1, "r"); // ř -> r: one byte changes
            Check(opened.Doc.Doc.IsModified, "an edit marks the tab modified");
            Check(await window.SaveAsync(opened), "save succeeds");
            var saved = await File.ReadAllBytesAsync(file);
            var expected = (byte[])original.Clone();
            expected[1] = 0x72;
            Check(saved.SequenceEqual(expected), "the saved file differs from the original only in the edited byte");
            Check(!opened.Doc.Doc.IsModified, "the tab is clean after saving");

            // a file above the switch size opens in the paged read-only mode
            app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
            var big = Path.Combine(temp, "big.log");
            await File.WriteAllBytesAsync(big, Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(1, 120_000).Select(i => $"line {i}\n"))));
            var largeTab = window.OpenFile(big, null);
            Check(largeTab.Doc.IsLarge && largeTab.Doc.ReadOnly && largeTab.Paged is not null, "a file above the threshold opens paged and read-only");
            Check(largeTab.Doc.SizeBadge is not null, "the large file has a size badge");
            window.CloseTab(largeTab);
            window.CloseTab(opened);

            // palette, settings view, undo, split
            var edit = window.NewDocument();
            edit.Editor!.Document.Text = Lines;
            edit.Editor.CaretOffset = 0;
            window.ShowPalette();
            window.SetPaletteQuery("save");
            Check(window.PaletteResults.Any(r => r.CommandId == "nks.file.save"), "the palette finds Save");
            window.ClosePalette();
            Check(await app.Workbench.Commands.ExecuteAsync("nks.edit.duplicateLine", new ShellContext(window, true)) == NksHub.Textida.Workbench.Commands.CommandOutcome.Handled
                && edit.Editor.Document.Text == "a\na\nb\nc", "a line command runs through the registry");
            window.Undo();
            Check(edit.Editor.Document.Text == "a\nb\nc", "undo takes the line command back in one step");
            var second = window.SplitView();
            Check(second is not null && window.Groups.Count == 2 && second!.Doc == edit.Doc, "split shows the same document in a second group");
            app.OpenSettings("tab");
            Check(app.SettingsView is { } sv && sv.ShownIds.Contains("editor.tabSize"), "the settings view finds the tab settings");
            app.SettingsView!.Close();
            Check(app.Journal.IsActive, "the journal is active");

            var csHeader = window.MenuHeaders[0];
            foreach (var lang in Loc.Languages.Skip(1))
            {
                app.SetLanguage(lang);
                Check(Loc.Language == lang && window.MenuHeaders[0] != csHeader && window.MenuHeaders.All(h => h.Length > 0), $"switch to {lang} changes menu headers");
            }
            app.SetLanguage(Loc.DefaultLanguage);
            Check(window.MenuHeaders[0] == csHeader, "switch back restores the default language");
            app.SetTheme("dark");
            await NextFrameAsync();
            app.SetTheme("light");
            await NextFrameAsync();
            app.SetTheme("system");

            Console.Out.WriteLine("SMOKE_OK " + string.Join(' ', _log));
            Console.Out.Flush();
            RemoveTemp(temp);
            desktop.Shutdown(0);
        }
        catch (Exception e)
        {
            RemoveTemp(temp);
            Fail(e.Message);
        }
        finally
        {
            GC.KeepAlive(watchdog);
        }
    }

    private static void RemoveTemp(string? dir)
    {
        if (dir is not null) try { Directory.Delete(dir, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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
