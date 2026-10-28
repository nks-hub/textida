using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>The special ways to open a file (P9.5.3), one list for the palette and the context menu of the file panel.</summary>
internal static class SpecialOpen
{
    // OPEN: list of variants to confirm
    /// <summary>The commands of the "Open Special" list, in the order shown. Commands that exist elsewhere are reused as they are.</summary>
    public static readonly string[] Variants =
    [
        "nks.open.hex",
        "nks.open.withEncoding",
        "nks.format.reread",
        "nks.open.readOnly",
        "nks.open.newCopy",
        "nks.file.openPathAtCaret",
        "nks.search.relatedFile",
        "nks.open.containingFolder",
        "nks.open.defaultApp",
        "nks.open.lastClosed",
    ];
}

internal sealed partial class MainWindow
{
    private const int ClosedPathsKept = 20;

    private readonly List<string> _closedPaths = [];
    private string? _specialTarget;

    /// <summary>The path a special open works on: the row chosen in the file panel menu, else the file of the active tab.</summary>
    private string? SpecialTarget() => _specialTarget ?? Active?.Path;

    /// <summary>Runs a command with the selected row of the file panel as the target of special opens.</summary>
    private async Task RunSpecialFromPanelAsync(string commandId)
    {
        _specialTarget = SelectedFileRow?.Entry.Path;
        try { await RunFileCommand(commandId); }
        finally { _specialTarget = null; }
    }

    internal void RememberClosed(OpenDocument od)
    {
        if (od.Path is not { } path || od.IsVirtual || od.IsResults) return;
        _closedPaths.RemoveAll(p => string.Equals(p, path, PathComparison));
        _closedPaths.Add(path);
        if (_closedPaths.Count > ClosedPathsKept) _closedPaths.RemoveAt(0);
    }

    /// <summary>The existing files to open: the target, or the ones the user picks when there is no target.</summary>
    private async Task<IReadOnlyList<string>> SpecialFilesAsync()
    {
        if (SpecialTarget() is not { } path) return await _app.Dialogs.PickFilesAsync(this);
        if (File.Exists(path)) return [path];
        SetStatusMessage(Loc.Get(K.SpecialNotFile, ("path", path)));
        return [];
    }

    private async Task OpenSpecialAsync(Func<string, DocTab> open)
    {
        foreach (var path in await SpecialFilesAsync())
        {
            try { open(path); }
            catch (IOException e) { SetStatusMessage(e.Message); }
        }
    }

    internal Task OpenTargetAsHexAsync() => OpenSpecialAsync(path => OpenHex(path, null));

    internal Task OpenTargetReadOnlyAsync() => OpenSpecialAsync(path =>
    {
        var tab = OpenFile(path, null);
        tab.Doc.UserReadOnly = true;
        ApplyLock(tab.Doc);
        SetStatusMessage(Loc.Get(K.SpecialReadOnly, ("name", Path.GetFileName(path))));
        return tab;
    });

    /// <summary>Opens the file and offers the encodings to read it again with.</summary>
    internal async Task OpenTargetWithEncodingAsync()
    {
        if ((await SpecialFilesAsync()).FirstOrDefault() is not { } path) return;
        try { OpenFile(path, null); }
        catch (IOException e) { SetStatusMessage(e.Message); return; }
        ShowEncodingMenu(false);
    }

    /// <summary>The text of the file in a new unsaved document. shortcut: the encoding is the one .NET detects from the BOM, and large files are refused; use "Open with Encoding" for those.</summary>
    internal Task OpenTargetAsNewCopyAsync() => OpenSpecialAsync(path =>
    {
        string name = Path.GetFileName(path);
        if (new FileInfo(path).Length > LargeFileBytes) throw new IOException(Loc.Get(K.SpecialTooLarge, ("name", name)));
        string text = File.ReadAllText(path);
        var tab = NewDocument();
        tab.Surface!.Document.Insert(0, text);
        return tab;
    });

    internal async Task ShowContainingFolderAsync()
    {
        if (SpecialTarget() is not { } path) { SetStatusMessage(Loc.Get(K.SpecialNoTarget)); return; }
        string? folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (folder is null || !Directory.Exists(folder)) { SetStatusMessage(Loc.Get(K.SpecialNotFile, ("path", path))); return; }
        if (!await LaunchPathAsync(folder)) SetStatusMessage(Loc.Get(K.SpecialLaunchFailed, ("path", folder)));
    }

    /// <summary>Hands a file or folder to the program the system assigns to it.</summary>
    private Task<bool> LaunchPathAsync(string path) =>
        _app.PathOpener is { } open ? open(path) : Task.FromResult(SystemLauncher.Start(SystemLauncher.StartInfo(path, SystemLauncher.Current)));

    internal async Task OpenWithDefaultAppAsync()
    {
        if (SpecialTarget() is not { } path) { SetStatusMessage(Loc.Get(K.SpecialNoTarget)); return; }
        if (!await LaunchPathAsync(path)) SetStatusMessage(Loc.Get(K.SpecialLaunchFailed, ("path", path)));
    }

    /// <summary>Opens the file of the tab that was closed last and still exists.</summary>
    internal void ReopenLastClosed()
    {
        while (_closedPaths.Count > 0)
        {
            string path = _closedPaths[^1];
            _closedPaths.RemoveAt(_closedPaths.Count - 1);
            if (!File.Exists(path)) continue;
            try { OpenFile(path, null); }
            catch (IOException e) { SetStatusMessage(e.Message); }
            return;
        }
        SetStatusMessage(Loc.Get(K.SpecialNoClosed));
    }

    /// <summary>The palette list of the special ways to open; each row runs its command.</summary>
    internal void ShowSpecialOpen()
    {
        var wb = _app.Workbench;
        var rows = SpecialOpen.Variants.Where(wb.Commands.HasHandler).Select(id =>
        {
            string title = wb.Strings.Get(wb.Commands.TryGet(id, out var c) ? c.TitleKey : id);
            return new QuickRow(title, null, null, null, true, null, title, () => _ = RunFileCommand(id));
        }).ToList();
        ShowChoice(Loc.Get(K.SpecialWatermark), Loc.Get(K.SpecialWatermark), rows);
    }
}
