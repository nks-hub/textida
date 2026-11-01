using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// A protected path (PZ.4.2). Nothing is listed as protected: a file is opened and saved as the user, and only when the system answers "no access" the editor offers the helper, says what it would do and waits for the answer.
/// A save through the helper is the normal save of the document with the last step, the copy over the target, done by the helper that the system lets the user allow; the editor itself stays as it is.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The largest file the helper reads for the editor.</summary>
    internal const long ProtectedReadMaxBytes = 64L << 20;

    private bool CanSaveElevated(DocTab tab) => tab.Doc.Path is not null && tab.Hex is null && !tab.Doc.IsLarge && !tab.Doc.IsVirtual && tab.Doc.Source is null && _app.Elevation.IsAvailable;

    /// <summary>Asks whether the helper may save the file, then saves through it. The caller has seen the system refuse; <paramref name="reason"/> is what it said.</summary>
    private async Task<bool> OfferElevatedSaveAsync(DocTab tab, bool overwrite, string reason)
    {
        var od = tab.Doc;
        string path = od.Path!;
        var ask = new ChoiceRequest(Loc.Get(K.ElevateSaveTitle), Loc.Get(K.ElevateSaveMessage, ("path", path), ("reason", reason)),
            [new DialogButton(Loc.Get(K.ElevateSaveAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
            [Loc.Get(K.ElevateDetailBytes, ("bytes", od.Doc.ByteLength)), Loc.Get(K.ElevateDetailHow)], "elevate");
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return false;
        od.Doc.ProtectedWriter = new ElevatedWriter(_app.Elevation);
        try { return await TrackedSaveAsync(od, () => SaveBackgroundAsync(tab, overwrite)); }
        finally { od.Doc.ProtectedWriter = null; }
    }

    /// <summary>After an open failed because of the rights: offers to read the file through the helper.</summary>
    internal async Task OfferElevatedOpenAsync(string path)
    {
        if (!_app.Elevation.IsAvailable) return;
        await OpenProtectedAsync(path);
    }

    internal CommandOutcome OpenProtected()
    {
        _ = OpenProtectedFromPickerAsync();
        return CommandOutcome.Handled;
    }

    private async Task OpenProtectedFromPickerAsync()
    {
        if (!_app.Elevation.IsAvailable)
        {
            SetStatusMessage(Loc.Get(K.ElevateUnavailable));
            return;
        }
        string? path = (await _app.Dialogs.PickFilesAsync(this)).FirstOrDefault();
        if (path is not null) await OpenProtectedAsync(path);
    }

    /// <summary>Reads <paramref name="path"/> through the helper, after the user agreed, and opens it as a document of that path; saving it asks again.</summary>
    internal async Task<DocTab?> OpenProtectedAsync(string path)
    {
        path = Path.GetFullPath(path);
        if (Tabs.FirstOrDefault(t => string.Equals(t.Path, path, PathComparison)) is { } open)
        {
            SelectTab(open);
            return open;
        }
        var ask = new ChoiceRequest(Loc.Get(K.ElevateOpenTitle), Loc.Get(K.ElevateOpenMessage, ("path", path)),
            [new DialogButton(Loc.Get(K.ElevateOpenAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
            [Loc.Get(K.ElevateDetailRead, ("limit", ProtectedReadMaxBytes >> 20)), Loc.Get(K.ElevateDetailHow)], "elevate");
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return null;

        string staging = Path.Combine(Path.GetTempPath(), "textida-protected-" + Guid.NewGuid().ToString("N") + ".tmp");
        byte[] bytes;
        try
        {
            SetStatusMessage(Loc.Get(K.ElevateWaiting));
            await Task.Run(() => _app.Elevation.Read(path, staging, ProtectedReadMaxBytes));
            bytes = await File.ReadAllBytesAsync(staging);
        }
        catch (ElevationCancelledException)
        {
            SetStatusMessage(Loc.Get(K.ElevateCancelled));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.ElevateFailed, ("reason", e.Message)));
            return null;
        }
        finally { try { File.Delete(staging); } catch (IOException) { /* in the temporary folder */ } }

        var od = OpenDocument.FromProtectedBytes(path, bytes);
        Adopt(od);
        var tab = AddView(od, _activeGroup);
        FocusView(tab);
        SetStatusMessage(Loc.Get(K.ElevateOpened, ("name", Path.GetFileName(path))));
        return tab;
    }

}
