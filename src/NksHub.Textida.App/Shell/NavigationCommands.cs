using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>Handlers of the navigation and file panel commands: Go to Anything, places, related file, the panel on the disk and what it does to files.</summary>
internal static class NavigationCommands
{
    public static void Register(App app)
    {
        var registry = app.Workbench.Commands;

        void Do(string id, Action<MainWindow> run) =>
            registry.SetHandler(id, (_, _) => ValueTask.FromResult(app.ActiveWindow is { } w ? Run(w, run) : CommandOutcome.NotHandled));

        void Fire(string id, Func<MainWindow, Task> run) => Do(id, w => Observe(w, run(w)));

        Do("nks.search.gotoAnything", w => w.ShowQuickOpen());
        Do("nks.search.recentEdits", w => w.ShowEditLocations());
        Do("nks.search.jumpBack", w => w.JumpBack());
        Do("nks.search.jumpForward", w => w.JumpForward());
        Do("nks.search.relatedFile", w => w.GoToRelatedFile());
        Do("nks.file.openPathAtCaret", w => w.OpenPathAtCaret());
        Fire("nks.file.openFolder", w => w.OpenFolderWithDialogAsync());
        Do("nks.view.toggleFilePanel", w => w.ToggleFiles());
        Fire("nks.files.newFile", w => w.NewFileAsync());
        Fire("nks.files.newFolder", w => w.NewFolderAsync());
        Fire("nks.files.duplicate", w => w.DuplicateAsync());
        Fire("nks.files.rename", w => w.RenameAsync());
        Fire("nks.files.move", w => w.MoveAsync());
        Fire("nks.files.delete", w => w.DeleteAsync());
        Fire("nks.files.refresh", w => w.RefreshFilesAsync());
        Fire("nks.files.reveal", w => w.RevealActiveFileAsync());
    }

    private static CommandOutcome Run(MainWindow window, Action<MainWindow> run)
    {
        run(window);
        return CommandOutcome.Handled;
    }

    /// <summary>A file operation runs after the command returned; a failure the operation did not report itself is shown in the status bar instead of vanishing.</summary>
    private static async void Observe(MainWindow window, Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            window.SetStatusMessage(e.Message);
        }
    }
}
