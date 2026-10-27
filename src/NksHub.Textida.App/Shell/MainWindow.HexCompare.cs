using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>Opens the binary comparison of the document in a hex tab with a file (P5.6.5).</summary>
internal sealed partial class MainWindow
{
    /// <summary>The comparison windows that are open, newest last; the window closes them with itself.</summary>
    internal List<HexCompareWindow> CompareWindows { get; } = [];

    /// <summary>Asks for a file and compares it with the document of the tab, unsaved changes included.</summary>
    internal async Task<HexCompareWindow?> CompareHexWithFileAsync(DocTab tab)
    {
        var files = await _app.Dialogs.PickFilesAsync(this);
        return files.Count == 0 ? null : OpenCompare(tab, files[0]);
    }

    /// <summary>Compares the document with its own file on disk: what the next save would change.</summary>
    internal HexCompareWindow? CompareHexWithSaved(DocTab tab)
    {
        if (tab.Path is not { } path || !File.Exists(path))
        {
            SetStatusMessage(Loc.Get(K.HexCompareNoFile));
            return null;
        }
        return OpenCompare(tab, path);
    }

    private HexCompareWindow? OpenCompare(DocTab tab, string path)
    {
        OpenDocument right;
        try { right = OpenDocument.Open(path, LargeFileBytes); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.HexCompareCannotRead, ("reason", e.Message)));
            return null;
        }
        var window = new HexCompareWindow(tab.Doc, tab.Title, right, System.IO.Path.GetFileName(path), rightIsFile: true, ownsRight: true, Options);
        CompareWindows.Add(window);
        window.Closed += (_, _) => CompareWindows.Remove(window);
        window.Show(this);
        return window;
    }
}
