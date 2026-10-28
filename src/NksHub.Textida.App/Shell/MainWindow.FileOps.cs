using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What the file panel does to files: create, duplicate, rename, move, delete to the recycle bin. A failure says why and leaves everything as it was;
/// a rename or move reaches the open documents, tabs, recent files and the journal of every window.
/// </summary>
internal sealed partial class MainWindow
{
    internal bool HasFolder => _fileTree is not null;

    internal bool HasFileSelection => FilePanelOpen && SelectedFileRow is not null;

    internal Task NewFileAsync() => CreateAsync(folder: false);

    internal Task NewFolderAsync() => CreateAsync(folder: true);

    private async Task CreateAsync(bool folder)
    {
        if (_fileTree is null) return;
        string directory = TargetDirectory();
        var name = await _app.Dialogs.PromptAsync(this, Loc.Get(folder ? K.FilesNewFolderTitle : K.FilesNewFileTitle), Loc.Get(folder ? K.FilesNewFolderLabel : K.FilesNewFileLabel), "");
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            string path;
            using (OwnWrite()) path = folder ? FileOperations.CreateFolder(directory, name) : FileOperations.CreateFile(directory, name);
            await ShowChangeAsync(directory, path);
            SetStatusMessage(Loc.Get(K.FilesStatusCreated, ("name", Path.GetFileName(path))));
            if (!folder) JumpToFile(path, null);
        }
        catch (FileOperationException e)
        {
            ReportFileError(e);
        }
    }

    internal async Task DuplicateAsync()
    {
        if (SelectedFileRow is not { } row) return;
        try
        {
            string copy;
            using (OwnWrite()) copy = await Task.Run(() => FileOperations.Duplicate(row.Entry.Path));
            await ShowChangeAsync(Path.GetDirectoryName(copy)!, copy);
            SetStatusMessage(Loc.Get(K.FilesStatusCreated, ("name", Path.GetFileName(copy))));
        }
        catch (FileOperationException e)
        {
            ReportFileError(e);
        }
    }

    internal async Task RenameAsync()
    {
        if (SelectedFileRow is not { } row) return;
        var name = await _app.Dialogs.PromptAsync(this, Loc.Get(K.FilesRenameTitle), Loc.Get(K.FilesRenameLabel), row.Entry.Name);
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == row.Entry.Name) return;
        await RenameEntryAsync(row.Entry.Path, name);
    }

    internal Task RenameEntryAsync(string path, string newName) =>
        MoveEntryAsync(path, () => FileOperations.Rename(path, newName), K.FilesStatusRenamed);

    internal async Task MoveAsync()
    {
        if (SelectedFileRow is not { } row || _fileTree is not { } tree) return;
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.FilesMoveTitle), Loc.Get(K.FilesMoveLabel), Path.GetDirectoryName(row.Entry.Path)!);
        if (string.IsNullOrWhiteSpace(answer)) return;
        string target = Path.GetFullPath(Path.Combine(tree.Root, answer.Trim()));
        await MoveEntryAsync(row.Entry.Path, () => FileOperations.Move(row.Entry.Path, target), K.FilesStatusMoved);
    }

    private async Task MoveEntryAsync(string oldPath, Func<string> move, string statusKey)
    {
        try
        {
            string moved;
            using (OwnWrite()) moved = await Task.Run(move);
            foreach (var window in _app.Windows.ToList()) window.ApplyPathMove(oldPath, moved);
            _app.Recent.Rename(oldPath, moved);
            if (_fileTree is { } tree && Path.GetDirectoryName(oldPath) is { } oldParent) await tree.RefreshAsync(oldParent);
            await ShowChangeAsync(Path.GetDirectoryName(moved)!, moved);
            SetStatusMessage(Loc.Get(statusKey, ("name", Path.GetFileName(moved)), ("path", Path.GetDirectoryName(moved)!)));
        }
        catch (FileOperationException e)
        {
            ReportFileError(e);
        }
    }

    internal async Task DeleteAsync()
    {
        if (SelectedFileRow is not { } row) return;
        string path = row.Entry.Path;
        string message = Loc.Get(K.FilesDeleteMessage, ("name", row.Entry.Name));
        if (_docs.Any(d => d.Path is { } p && PathMove.Rebase(p, path, "") is not null && d.HasUnsavedWork)) message += " " + Loc.Get(K.FilesDeleteOpenDirty);
        var ask = new ChoiceRequest(Loc.Get(K.FilesDeleteTitle), message, [new DialogButton(Loc.Get(K.FilesDeleteConfirm)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)]);
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return;
        try
        {
            await Task.Run(() => RecycleBin.Move(path));
            _app.Recent.Remove(path);
            if (_fileTree is { } tree) await tree.RefreshAsync(Path.GetDirectoryName(path)!);
            InvalidateWorkspaceIndex();
            SetStatusMessage(Loc.Get(K.FilesStatusDeleted, ("name", row.Entry.Name)));
        }
        catch (FileOperationException e)
        {
            ReportFileError(e);
        }
    }

    /// <summary>
    /// A file was deleted by a reviewed change: its tabs close. A tab with unsaved work asks first, because closing it drops that work; it can stay open instead
    /// (a save then writes the file again).
    /// </summary>
    internal void ApplyPathDeleted(string path)
    {
        foreach (var od in _docs.Where(d => d.Path is { } p && PathMove.IsInside(p, path)).ToList())
        {
            if (od.HasUnsavedWork) _ = AskDeletedWithWorkAsync(od);
            else CloseViews(od);
        }
    }

    private async Task AskDeletedWithWorkAsync(OpenDocument od)
    {
        var ask = new ChoiceRequest(Loc.Get(K.WorkspaceDeletedTitle), Loc.Get(K.WorkspaceDeletedMessage, ("name", od.Title)),
            [new DialogButton(Loc.Get(K.WorkspaceDeletedClose)), new DialogButton(Loc.Get(K.WorkspaceDeletedKeep), IsDefault: true, IsCancel: true)]);
        if (await _app.Dialogs.ChooseAsync(this, ask) == 0 && _docs.Contains(od)) CloseViews(od);
    }

    private void CloseViews(OpenDocument od)
    {
        foreach (var view in od.Views.ToList()) RemoveTab(view, untrack: true);
    }

    internal async Task RefreshFilesAsync()
    {
        if (_fileTree is not { } tree) return;
        await tree.RefreshAllAsync();
        InvalidateWorkspaceIndex();
    }

    /// <summary>Shows the active file in the panel, opening its folder first when it is outside the current one.</summary>
    internal async Task RevealActiveFileAsync()
    {
        if (Active?.Path is not { } path) return;
        if (_fileTree is null || !_fileTree.Contains(path)) { if (!await ShowFolderAsync(Path.GetDirectoryName(path)!)) return; }
        else ToggleFiles(true);
        await SelectFileAsync(path);
    }

    /// <summary>Opens the folders above a path in the panel and selects its row.</summary>
    internal async Task SelectFileAsync(string path)
    {
        if (_fileTree is { } tree && await tree.RevealAsync(path)) SelectFilePath(path);
    }

    /// <summary>Reads the folder that changed again, opens the folders above <paramref name="path"/> and selects it.</summary>
    private async Task ShowChangeAsync(string directory, string path)
    {
        if (_fileTree is not { } tree) return;
        InvalidateWorkspaceIndex();
        await tree.RevealAsync(path);
        await tree.ExpandAsync(directory);
        await tree.RefreshAsync(directory);
        SelectFilePath(path);
    }

    /// <summary>The reason in the words of the UI language, and that nothing was changed.</summary>
    internal void ReportFileError(FileOperationException e)
    {
        var path = ("path", (object)e.Path);
        string reason = e.Kind switch
        {
            FileFailure.NotFound => Loc.Get(K.FilesErrorNotFound, path),
            FileFailure.AlreadyExists => Loc.Get(K.FilesErrorExists, path),
            FileFailure.AccessDenied => Loc.Get(K.FilesErrorDenied, path),
            FileFailure.InUse => Loc.Get(K.FilesErrorInUse, path),
            FileFailure.DiskFull => Loc.Get(K.FilesErrorDiskFull, path),
            FileFailure.InvalidName => Loc.Get(K.FilesErrorName, path),
            FileFailure.Unsupported => Loc.Get(K.FilesErrorUnsupported, path),
            _ => Loc.Get(K.FilesErrorUnknown, path, ("reason", e.Message)),
        };
        ShowNotice("fileop", Loc.Get(K.FilesErrorFailed, ("reason", reason)), warning: true);
    }
}
