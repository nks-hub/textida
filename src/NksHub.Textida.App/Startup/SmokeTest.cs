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
            if (OperatingSystem.IsMacOS()) await CheckMacAccessibilityAsync(opened);

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

            await CheckNavigationAsync(temp);

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

    /// <summary>The file panel on a real folder, Go to Anything, the related file, a rename that carries an open document along, and the system recycle bin.</summary>
    private async Task CheckNavigationAsync(string temp)
    {
        string proj = Path.Combine(temp, "proj");
        Directory.CreateDirectory(Path.Combine(proj, "src"));
        File.WriteAllText(Path.Combine(proj, "src", "main.c"), "int main(void) { return 0; }\n");
        File.WriteAllText(Path.Combine(proj, "src", "main.h"), "#pragma once\n");
        Check(await window.ShowFolderAsync(proj) && window.FilePanelOpen && window.Files!.Rows.Count == 1, "the file panel lists the first level of a folder");
        await window.Files!.ExpandAsync(Path.Combine(proj, "src"));
        Check(window.Files.Rows.Select(r => r.Entry.Name).SequenceEqual(["src", "main.c", "main.h"]), "an opened folder lists its entries, folders first");

        var main = window.OpenFile(Path.Combine(proj, "src", "main.c"), null);
        window.GoToRelatedFile();
        Check(window.Active?.Path == Path.Combine(proj, "src", "main.h"), "the related file of main.c is main.h");

        window.ShowQuickOpen("src main");
        for (int i = 0; i < 100 && !window.QuickResults.Any(r => r.Title == "main.c" && r.Category is null); i++) await Task.Delay(50);
        Check(window.QuickResults.Any(r => r.Title == "main.c"), "Go to Anything finds a file by parts of its path");
        window.ClosePalette();

        main.Editor!.Document.Insert(0, "// ");
        string renamed = Path.Combine(proj, "src", "start.c");
        await window.RenameEntryAsync(Path.Combine(proj, "src", "main.c"), "start.c");
        Check(main.Path == renamed && main.Doc.Doc.IsModified && File.Exists(renamed) && !File.Exists(Path.Combine(proj, "src", "main.c")), "renaming an open file renames the tab and keeps the unsaved change");

        window.ShowPalette();
        window.SetPaletteQuery("=1+2*3");
        Check(window.QuickResults is [{ Title: "= 7" }], "the palette calculates 1+2*3");
        window.ClosePalette();
        CheckRecycleBin(temp);
    }

    private void CheckRecycleBin(string temp)
    {
        if (!Services.RecycleBin.IsSupported) return;
        string name = $"textida-smoke-{Environment.ProcessId}.txt";
        string victim = Path.Combine(temp, name);
        File.WriteAllText(victim, "x");
        Services.RecycleBin.Move(victim);
        Check(!File.Exists(victim), "the system recycle bin took the file out of its folder");
        string? found = null;
        if (OperatingSystem.IsMacOS())
        {
            string trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".Trash");
            found = Directory.EnumerateFiles(trash, "textida-smoke-" + Environment.ProcessId + "*").FirstOrDefault();
            Check(found is not null, "the file is in the Trash");
            if (found is not null) File.Delete(found);
        }
        else if (OperatingSystem.IsWindows())
        {
            string bin = Path.Combine(Path.GetPathRoot(victim)!, "$Recycle.Bin", System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value);
            foreach (var meta in Directory.EnumerateFiles(bin, "$I*"))
            {
                var bytes = File.ReadAllBytes(meta);
                if (bytes.Length < 28 || BitConverter.ToInt64(bytes, 0) != 2 || !Encoding.Unicode.GetString(bytes, 28, Math.Max(0, BitConverter.ToInt32(bytes, 24) - 1) * 2).Equals(victim, StringComparison.OrdinalIgnoreCase)) continue;
                found = meta;
                File.Delete(meta);
                string data = Path.Combine(bin, "$R" + Path.GetFileName(meta)[2..]);
                if (File.Exists(data)) File.Delete(data);
            }
            Check(found is not null, "the file is in the recycle bin");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private async Task CheckMacAccessibilityAsync(DocTab tab)
    {
        var editor = tab.Editor!;
        nint root = window.TryGetPlatformHandle()!.Handle;
        editor.TextArea.Focus();
        await NextFrameAsync();
        nint element = 0;
        for (int i = 0; i < 30 && element == 0; i++)
        {
            element = MacAccessibilitySmoke.FindEditor(root);
            if (element == 0) await Task.Delay(100);
        }
        if (element == 0) Console.Error.WriteLine(MacAccessibilitySmoke.Dump(root));
        Check(element != 0, "the accessibility tree of the window contains the editor text element");
        Check(MacAccessibilitySmoke.Value(element) == editor.Document.Text, "AXValue is the document text");
        editor.CaretOffset = 5;
        await NextFrameAsync();
        Check(MacAccessibilitySmoke.SelectedRange(element) == (5, 0), "AXSelectedTextRange follows the caret");
        editor.Select(2, 3);
        await NextFrameAsync();
        Check(MacAccessibilitySmoke.SelectedRange(element) == (2, 3) && MacAccessibilitySmoke.SelectedText(element) == editor.SelectedText, "AXSelectedTextRange and AXSelectedText follow the selection");
        Check(MacAccessibilitySmoke.FocusedRole() == "AXTextArea", "the editor text element is the focused element");
        var strip = window.Groups[0].Strip;
        Check(strip.ContainerFromIndex(strip.SelectedIndex)?.Focus() == true, "the tab strip item takes the keyboard focus");
        await NextFrameAsync();
        string focusedRole = MacAccessibilitySmoke.FocusedRole();
        Check(focusedRole != "AXTextArea", $"the focused element leaves the editor for the tab strip (role {focusedRole})");
        editor.TextArea.Focus();
        await NextFrameAsync();
        Check(MacAccessibilitySmoke.FocusedRole() == "AXTextArea", "the focus returns to the editor text element");
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
