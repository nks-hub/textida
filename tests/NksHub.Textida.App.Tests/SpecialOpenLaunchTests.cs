using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P9.5.3: the closed list of special opens and the two that start another program, the file manager and the default app. The app is tested up to the point where
/// it hands a path to the system; the process that opens a window is never started here. What starts is checked on a harmless console program.
/// </summary>
[Collection("LocState")]
public class SpecialOpenLaunchTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly List<string> _launched = [];
    private bool _launchSucceeds = true;

    public SpecialOpenLaunchTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
        _app.PathOpener = path =>
        {
            _launched.Add(path);
            return Task.FromResult(_launchSucceeds);
        };
    }

    public void Dispose()
    {
        _app.PathOpener = null;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static async Task WaitFor(Func<bool> condition, int milliseconds = 10_000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < milliseconds, "timed out waiting for the UI");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    /// <summary>The panel shows the folder <c>work</c> with the file <c>a.txt</c> and the folder <c>sub</c>; returns the folder.</summary>
    private async Task<string> PanelAsync()
    {
        string root = Path.Combine(_temp.Path, "work");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "abc");
        File.WriteAllText(Path.Combine(root, "sub", "b.txt"), "def");
        Assert.True(await _w.ShowFolderAsync(root));
        return root;
    }

    private async Task RunAsync(string command)
    {
        var run = _app.Workbench.Commands.ExecuteAsync(command, new ShellContext(_w, forMenu: false)).AsTask();
        await WaitFor(() => run.IsCompleted);
        Assert.Equal(CommandOutcome.Handled, await run);
    }

    private string TitleOf(string id) => _app.Workbench.Strings.Get(_app.Workbench.Commands.TryGet(id, out var c) ? c.TitleKey : id);

    // ---- the list ----

    [AvaloniaFact]
    public void The_list_of_special_opens_is_closed()
    {
        Assert.Equal(
            ["nks.open.hex", "nks.open.withEncoding", "nks.format.reread", "nks.open.readOnly", "nks.open.newCopy", "nks.file.openPathAtCaret",
             "nks.search.relatedFile", "nks.open.containingFolder", "nks.open.defaultApp", "nks.open.lastClosed"],
            SpecialOpen.Variants);
    }

    [AvaloniaFact]
    public void Every_special_open_is_a_command_in_the_palette_and_in_the_menu_of_the_panel()
    {
        Assert.All(SpecialOpen.Variants, id => Assert.True(_app.Workbench.Commands.HasHandler(id), id));

        _w.ShowSpecialOpen();
        var palette = _w.QuickResults.Select(r => r.Title).ToList();
        _w.ClosePalette();
        var menu = _w.FileList.ContextMenu!.Items.OfType<MenuItem>().Last().Items.OfType<MenuItem>().Select(m => (string)m.Header!).ToList();

        string[] expected = [.. SpecialOpen.Variants.Select(TitleOf)];
        Assert.Equal(expected, palette);
        Assert.Equal(expected, menu);
    }

    // ---- what is handed to the system ----

    [AvaloniaFact]
    public async Task The_file_manager_gets_the_folder_of_the_active_file_and_the_default_app_gets_the_file()
    {
        string folder = await PanelAsync();
        string file = Path.Combine(folder, "a.txt");
        _w.OpenFile(file, null);

        await RunAsync("nks.open.containingFolder");
        await RunAsync("nks.open.defaultApp");

        Assert.Equal([folder, file], _launched);
        Assert.NotEqual(Loc.Get(K.SpecialLaunchFailed, ("path", file)), _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task From_the_menu_of_the_panel_they_work_on_the_selected_row_whatever_tab_is_active()
    {
        string folder = await PanelAsync();
        string file = Path.Combine(folder, "a.txt");
        string other = _temp.File("other.txt", "x");
        _w.OpenFile(other, null);
        var special = _w.FileList.ContextMenu!.Items.OfType<MenuItem>().Last();
        void Click(string id) => special.Items.OfType<MenuItem>().Single(m => (string)m.Header! == TitleOf(id)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        _w.SelectFilePath(file);
        Assert.Equal(file, _w.SelectedFileRow!.Value.Entry.Path);
        Click("nks.open.defaultApp");
        await WaitFor(() => _launched.Count == 1);
        Click("nks.open.containingFolder");
        await WaitFor(() => _launched.Count == 2);
        string sub = Path.Combine(folder, "sub");
        _w.SelectFilePath(sub); // a folder row opens that folder itself
        Assert.Equal(sub, _w.SelectedFileRow!.Value.Entry.Path);
        Click("nks.open.containingFolder");
        await WaitFor(() => _launched.Count == 3);

        Assert.Equal([file, folder, sub], _launched);
    }

    [AvaloniaFact]
    public async Task A_failed_start_is_said_in_the_status_bar_and_nothing_is_started_for_a_missing_folder_or_without_a_file()
    {
        string folder = await PanelAsync();
        string file = Path.Combine(folder, "a.txt");
        _w.OpenFile(file, null);

        _launchSucceeds = false;
        await RunAsync("nks.open.defaultApp");
        Assert.Equal(Loc.Get(K.SpecialLaunchFailed, ("path", file)), _w.StatusMessageText);
        await RunAsync("nks.open.containingFolder");
        Assert.Equal(Loc.Get(K.SpecialLaunchFailed, ("path", folder)), _w.StatusMessageText);
        _launchSucceeds = true;

        Directory.Delete(folder, recursive: true);
        _launched.Clear();
        await RunAsync("nks.open.containingFolder");
        Assert.Equal(Loc.Get(K.SpecialNotFile, ("path", file)), _w.StatusMessageText);
        Assert.Empty(_launched);

        foreach (var tab in _w.Tabs.ToList()) _w.CloseTab(tab);
        await RunAsync("nks.open.defaultApp");
        Assert.Equal(Loc.Get(K.SpecialNoTarget), _w.StatusMessageText);
        Assert.Empty(_launched);
    }

    [AvaloniaFact]
    public async Task The_palette_row_starts_the_same_launch()
    {
        string folder = await PanelAsync();
        _w.OpenFile(Path.Combine(folder, "a.txt"), null);

        _w.ShowSpecialOpen();
        _w.QuickResults.Single(r => r.Title == TitleOf("nks.open.containingFolder")).Run();
        await WaitFor(() => _launched.Count == 1);

        Assert.Equal([folder], _launched);
    }

    // ---- what the system is asked to start ----

    [AvaloniaFact]
    public void Each_platform_has_its_own_program_and_the_path_stays_one_argument()
    {
        const string path = @"/home/me/my files/a ""b"".txt";

        var windows = SystemLauncher.StartInfo(path, SystemLauncher.Platform.Windows);
        Assert.Equal((path, true), (windows.FileName, windows.UseShellExecute));
        Assert.Empty(windows.ArgumentList);

        var mac = SystemLauncher.StartInfo(path, SystemLauncher.Platform.MacOs);
        Assert.Equal(("open", false), (mac.FileName, mac.UseShellExecute));
        Assert.Equal([path], mac.ArgumentList);

        var linux = SystemLauncher.StartInfo(path, SystemLauncher.Platform.Linux);
        Assert.Equal(("xdg-open", false), (linux.FileName, linux.UseShellExecute));
        Assert.Equal([path], linux.ArgumentList);
    }

    [AvaloniaFact]
    public async Task A_console_program_is_really_started_and_a_program_that_does_not_exist_is_reported()
    {
        string file = _temp.File("target.txt", "lower case\n");
        var info = new ProcessStartInfo(ToolTestKit.Dotnet) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { ToolTestKit.EchoDll, "--format", file }) info.ArgumentList.Add(argument);

        Assert.True(SystemLauncher.Start(info));
        string Read()
        {
            try { return File.ReadAllText(file); }
            catch (IOException) { return ""; } // the program still has the file open
        }
        var sw = Stopwatch.StartNew();
        while (Read() != "LOWER CASE\n" && sw.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
        Assert.Equal("LOWER CASE\n", Read());

        Assert.False(SystemLauncher.Start(new ProcessStartInfo(Path.Combine(_temp.Path, "no-such-program.exe")) { UseShellExecute = false, CreateNoWindow = true }));
    }

    [AvaloniaFact]
    public void The_start_for_the_current_platform_fails_quietly_when_the_system_has_nothing_to_open_the_path_with()
    {
        if (!OperatingSystem.IsWindows()) return; // xdg-open and open are programs that exist or not, their answer is not ours to assert
        Assert.False(SystemLauncher.Start(SystemLauncher.StartInfo(Path.Combine(_temp.Path, "does-not-exist.xyz"), SystemLauncher.Current)));
    }
}
