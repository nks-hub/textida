using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// File > Open Recent (P2.1.2): the recent files of Go to Anything, newest first, read again each time the submenu opens. The first ten rows have the digits 1 to 9 and 0 as
/// access keys, a row shows the name and its folder and gives the whole path as its tip, and a file that no longer exists is marked and offers to leave the list.
/// </summary>
internal sealed partial class MainWindow
{
    private const string ClearRecentCommand = "nks.file.clearRecent";

    /// <summary>The menus with the rows of the recent files put above the submenu that holds the command clearing the list.</summary>
    private List<MenuGroup> WithRecentFiles(List<MenuGroup> groups) => [.. groups.Select(g => (MenuGroup)AttachRecent(g))];

    private MenuNode AttachRecent(MenuNode node) => node switch
    {
        MenuGroup g when g.Items.Any(i => i is MenuCommand { CommandId: ClearRecentCommand }) => g with { Head = RecentMenuItems },
        MenuGroup g => g with { Items = [.. g.Items.Select(AttachRecent)] },
        _ => node,
    };

    /// <summary>The rows of the recent files as the submenu shows them now.</summary>
    internal IReadOnlyList<MenuNode> RecentMenuItems()
    {
        var files = _app.Recent.Files.ToList();
        if (files.Count == 0) return [new MenuCommand(Loc.Get(K.RecentEmpty), () => { }, Enabled: false)];
        var rows = new List<MenuNode>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string path = files[i];
            bool source = UriMask.IsUri(path);
            bool missing = !source && !File.Exists(path);
            string name = source ? UriMask.DisplayName(path) : Path.GetFileName(path);
            string folder = source ? "" : Path.GetDirectoryName(path) ?? "";
            string text = missing ? Loc.Get(K.RecentMissing, ("name", name)) : name;
            if (folder.Length > 0) text += "  ·  " + ShortFolder(folder);
            string prefix = i < 10 ? ((i + 1) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + " " : "";
            rows.Add(new MenuCommand(prefix + text, () => _ = OpenRecentAsync(path))
            {
                AccessAt = prefix.Length > 0 ? 0 : -1,
                Tip = missing ? Loc.Get(K.RecentRemoveMessage, ("path", path)) : path,
            });
        }
        return rows;
    }

    /// <summary>A long folder keeps its root and its last two parts, so the row stays short and still tells two files of the same name apart.</summary>
    private static string ShortFolder(string folder)
    {
        const int MaxLength = 48;
        if (folder.Length <= MaxLength) return folder;
        char separator = folder.Contains('\\') ? '\\' : '/';
        var parts = folder.Split(separator);
        return parts.Length <= 3 ? folder : string.Join(separator, parts[0], "…", parts[^2], parts[^1]);
    }

    /// <summary>Opens a recent file; one that no longer exists asks to be taken off the list instead.</summary>
    internal async Task OpenRecentAsync(string path)
    {
        if (UriMask.IsUri(path)) { await OpenSourceAsync(path); return; }
        if (!File.Exists(path))
        {
            int answer = await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.RecentRemoveTitle), Loc.Get(K.RecentRemoveMessage, ("path", path)),
                [new DialogButton(Loc.Get(K.RecentRemove), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]));
            if (answer == 0) _app.Recent.Remove(path);
            return;
        }
        try { OpenFile(path, null); }
        catch (IOException e) { SetStatusMessage(e.Message); }
    }

    internal void ClearRecentFiles()
    {
        _app.Recent.Clear();
        SetStatusMessage(Loc.Get(K.RecentCleared));
    }
}
