using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Win32;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P3.7.1: the Explorer menu entry and the optional associations. Every write goes to <c>HKCU\Software\Textida.Tests\{guid}</c> standing in for <c>HKCU\Software\Classes</c>;
/// the real classes key is never opened here.
/// </summary>
[Collection("LocState")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ShellIntegrationTests : IDisposable
{
    private const string Exe = @"C:\Program Files\Some Folder\Textida.exe";
    private readonly string _path = @"Software\Textida.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey? _root;
    private readonly TempDir _temp = new();

    public ShellIntegrationTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the registry exists on Windows only");
        _root = Registry.CurrentUser.CreateSubKey(_path);
    }

    public void Dispose()
    {
        ShellIntegration.ClassesOverride = null;
        ShellIntegration.ExeOverride = null;
        _root?.Dispose();
        if (OperatingSystem.IsWindows()) Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
        _temp.Dispose();
    }

    private static void Walk(RegistryKey key, string prefix, List<string> lines)
    {
        foreach (var name in key.GetValueNames())
            lines.Add($"{prefix}|{name}|{key.GetValueKind(name)}|{(key.GetValue(name) is byte[] b ? Convert.ToHexString(b) : key.GetValue(name))}");
        foreach (var sub in key.GetSubKeyNames())
        {
            lines.Add(prefix + @"\" + sub);
            using var child = key.OpenSubKey(sub)!;
            Walk(child, prefix + @"\" + sub, lines);
        }
    }

    private List<string> Snapshot()
    {
        var lines = new List<string>();
        Walk(_root!, "", lines);
        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    [Fact]
    public void The_menu_entry_runs_the_program_with_the_clicked_file_quoted()
    {
        new ShellIntegration(_root!, Exe).AddMenu("Open with Textida");

        using var verb = _root!.OpenSubKey(@"*\shell\Textida")!;
        Assert.Equal("Open with Textida", verb.GetValue(""));
        Assert.Equal($"\"{Exe}\",0", verb.GetValue("Icon"));
        using var command = verb.OpenSubKey("command")!;
        Assert.Equal($"\"{Exe}\" \"%1\"", command.GetValue(""));
        Assert.True(new ShellIntegration(_root!, Exe).HasMenu());
    }

    [Fact]
    public void Removing_the_menu_leaves_the_root_as_it_was()
    {
        var before = Snapshot();
        var shell = new ShellIntegration(_root!, Exe);
        shell.AddMenu("x");
        Assert.NotEqual(before, Snapshot());
        shell.RemoveMenu();
        Assert.Equal(before, Snapshot());
        Assert.False(shell.HasMenu());
    }

    [Fact]
    public void An_association_adds_an_open_with_entry_and_a_program_id_but_no_default()
    {
        var shell = new ShellIntegration(_root!, Exe);
        shell.Associate(".log");

        Assert.True(shell.IsAssociated(".log"));
        Assert.False(shell.IsAssociated(".txt"));
        using var progIds = _root!.OpenSubKey(@".log\OpenWithProgids")!;
        Assert.Equal(RegistryValueKind.None, progIds.GetValueKind(ShellIntegration.ProgId));
        Assert.Null(_root.OpenSubKey(".log")!.GetValue(""));
        using var command = _root.OpenSubKey(ShellIntegration.ProgId + @"\shell\open\command")!;
        Assert.Equal($"\"{Exe}\" \"%1\"", command.GetValue(""));
    }

    [Fact]
    public void Removing_everything_deletes_what_was_written_and_keeps_what_belongs_to_others()
    {
        // an unrelated program's keys and a neighbouring value in the same OpenWithProgids key
        using (var other = _root!.CreateSubKey(@"*\shell\PSPad\command")) other.SetValue("", @"C:\pspad\PSPad.exe ""%1""");
        using (var other = _root.CreateSubKey(@".txt\OpenWithProgids")) other.SetValue("OtherApp.txt", Array.Empty<byte>(), RegistryValueKind.None);
        using (var other = _root.CreateSubKey(".md")) other.SetValue("", "markdown");
        var before = Snapshot();

        var shell = new ShellIntegration(_root, Exe);
        shell.AddMenu("x");
        shell.Associate(".txt");
        shell.Associate(".md");
        shell.Associate(".json");
        shell.RemoveAll();

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Dissociating_the_last_extension_removes_the_program_id()
    {
        var before = Snapshot();
        var shell = new ShellIntegration(_root!, Exe);
        shell.Associate(".log");
        shell.Associate(".css");
        shell.Dissociate(".log");
        Assert.NotNull(_root!.OpenSubKey(ShellIntegration.ProgId));
        shell.Dissociate(".css");
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void Removing_when_nothing_was_added_changes_nothing()
    {
        var before = Snapshot();
        new ShellIntegration(_root!, Exe).RemoveAll();
        Assert.Equal(before, Snapshot());
    }

    private static async Task Until(Func<bool> condition, string what)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(sw.ElapsedMilliseconds < 30_000, "Not reached: " + what);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task The_commands_add_associate_and_remove_through_the_window_without_touching_the_real_classes()
    {
        var app = TestApp.Prepare(_temp, new ScriptedDialogs());
        try
        {
            var w = app.CreateWindow(null);
            ShellIntegration.ClassesOverride = () => _root!.CreateSubKey("Classes");
            ShellIntegration.ExeOverride = Exe;
            var shell = new ShellIntegration(_root!.CreateSubKey("Classes"), Exe);

            await app.Workbench.Commands.ExecuteAsync("nks.tools.shellMenuAdd", new ShellContext(w, forMenu: false)).AsTask();
            Assert.True(shell.HasMenu());
            using (var verb = _root.OpenSubKey(@"Classes\*\shell\Textida")!) Assert.Equal(Loc.Get(K.ShellMenuLabel), verb.GetValue(""));

            await app.Workbench.Commands.ExecuteAsync("nks.tools.shellAssociate", new ShellContext(w, forMenu: false)).AsTask();
            await Until(() => w.IsPaletteOpen && w.QuickResults.Count == ShellIntegration.Extensions.Count, "the extension list");
            w.RunPaletteSelection();
            await Until(() => shell.IsAssociated(ShellIntegration.Extensions[0]), "the first extension to be associated");
            await Until(() => w.IsPaletteOpen && w.QuickResults.Count == ShellIntegration.Extensions.Count, "the list again");
            w.ClosePalette();
            await Until(() => w.ShellTask.IsCompleted, "the list to end");

            await app.Workbench.Commands.ExecuteAsync("nks.tools.shellRemove", new ShellContext(w, forMenu: false)).AsTask();
            Assert.False(shell.HasMenu());
            Assert.All(ShellIntegration.Extensions, e => Assert.False(shell.IsAssociated(e)));
            Assert.Null(_root.OpenSubKey(@"Classes\" + ShellIntegration.ProgId));
        }
        finally { TestApp.Cleanup(app); }
    }
}
