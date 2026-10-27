using System.Text;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>The bookmarks of the hex view (P5.6.3): named, coloured regions that follow the bytes, a panel, the dialog and the export and import.</summary>
internal sealed partial class MainWindow
{
    private void WireHexBookmarks(HexView hex)
    {
        hex.BookmarkList.ActionRequested += (action, bookmark) => _ = RunHexBookmarkAction(hex, action, bookmark);
        hex.Marks.Changed += () => _app.SessionChanged();
    }

    private async Task RunHexBookmarkAction(HexView hex, HexBookmarkAction action, HexBookmark? bookmark)
    {
        switch (action)
        {
            case HexBookmarkAction.GoTo when bookmark is not null: GoToHexBookmark(hex, bookmark); break;
            case HexBookmarkAction.Add: HexBookmarkAdd(hex); break;
            case HexBookmarkAction.Edit when bookmark is not null: await EditHexBookmarkAsync(hex, bookmark); break;
            case HexBookmarkAction.Delete when bookmark is not null: RemoveHexBookmark(hex, bookmark); break;
            case HexBookmarkAction.Export: await ExportHexBookmarksAsync(hex); break;
            case HexBookmarkAction.Import: await ImportHexBookmarksAsync(hex); break;
        }
    }

    private static void GoToHexBookmark(HexView hex, HexBookmark bookmark)
    {
        hex.Select(bookmark.Range.Start, bookmark.Range.End);
        hex.CenterIfOutside(bookmark.Range.Start);
        hex.Focus();
    }

    /// <summary>The bytes a new bookmark covers: the selection, else the value the data inspector shows at the caret, else the byte under the caret.</summary>
    private static (ByteRange Range, string? Type)? NewHexBookmarkRange(HexView hex)
    {
        var editor = hex.Editor;
        if (editor.HasSelection) return (editor.Selection, null);
        long length = editor.Document.ByteLength;
        if (editor.Caret >= length) return null;
        if (hex.Inspector.TypedValue() is { } typed)
        {
            long end = Math.Min(length, editor.Caret + typed.Length);
            return (new ByteRange(editor.Caret, end), end - editor.Caret == typed.Length ? typed.Type : null);
        }
        return (new ByteRange(editor.Caret, editor.Caret + 1), null);
    }

    private HexBookmark? HexBookmarkAdd(HexView hex)
    {
        if (NewHexBookmarkRange(hex) is not { } target)
        {
            SetStatusMessage(Loc.Get(K.HexBmNoSelection));
            return null;
        }
        var added = hex.Marks.Add(target.Range, type: target.Type);
        SetStatusMessage(Loc.Get(K.HexBmAdded, ("offset", hex.OffsetText(target.Range.Start)), ("length", target.Range.Length)));
        _app.SessionChanged();
        return added;
    }

    private void RemoveHexBookmark(HexView hex, HexBookmark bookmark)
    {
        if (!hex.Marks.Remove(bookmark.Id)) return;
        SetStatusMessage(Loc.Get(K.HexBmRemoved, ("name", bookmark.Name.Length > 0 ? bookmark.Name : hex.OffsetText(bookmark.Range.Start))));
        _app.SessionChanged();
    }

    /// <summary>
    /// Ctrl+F2 in hex: a selection that is exactly a bookmark removes it, any other selection becomes one; without a selection the narrowest bookmark under the caret goes,
    /// or, where there is none, the value of the data inspector (or one byte) is marked.
    /// </summary>
    internal bool ToggleHexBookmark(HexView hex)
    {
        var editor = hex.Editor;
        var marks = hex.Marks;
        if (editor.HasSelection)
        {
            if (marks.Items.FirstOrDefault(b => b.Range == editor.Selection) is { } same) RemoveHexBookmark(hex, same);
            else HexBookmarkAdd(hex);
        }
        else if (marks.Containing(editor.Caret) is [{ } narrowest, ..]) RemoveHexBookmark(hex, narrowest);
        else HexBookmarkAdd(hex);
        return true;
    }

    internal bool StepHexBookmark(HexView hex, bool forward)
    {
        long from = hex.Editor.HasSelection ? hex.Editor.Selection.Start : hex.Caret;
        if (hex.Marks.Step(from, forward) is not { } next) SetStatusMessage(Loc.Get(K.HexBmNone));
        else
        {
            RecordJump();
            GoToHexBookmark(hex, next);
        }
        return true;
    }

    internal bool ClearHexBookmarks(HexView hex)
    {
        hex.Marks.Clear();
        SetStatusMessage(Loc.Get(K.HexBmCleared));
        _app.SessionChanged();
        return true;
    }

    internal CommandOutcome ToggleHexBookmarkPanel()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        var panel = hex.BookmarkList;
        if (!panel.IsVisible) panel.FocusList();
        else if (panel.IsKeyboardFocusWithin) { panel.IsVisible = false; hex.Focus(); }
        else panel.FocusList();
        return CommandOutcome.Handled;
    }

    /// <summary>Opens the properties of the bookmark under the caret or the selection; where there is none a new one is made from the selection, and cancelling the dialog drops it again.</summary>
    internal async Task EditHexBookmarkAsync(HexView hex, HexBookmark? bookmark = null)
    {
        bool created = false;
        if (bookmark is null)
        {
            var editor = hex.Editor;
            bookmark = editor.HasSelection ? hex.Marks.Items.FirstOrDefault(b => b.Range == editor.Selection) : hex.Marks.Containing(editor.Caret).FirstOrDefault();
            if (bookmark is null)
            {
                bookmark = HexBookmarkAdd(hex);
                created = true;
            }
            if (bookmark is null) return;
        }
        string region = Loc.Get(K.HexBmRange, ("start", hex.OffsetText(bookmark.Range.Start)), ("end", hex.OffsetText(bookmark.Range.End)), ("length", bookmark.Range.Length));
        var form = await _app.Dialogs.HexBookmarkAsync(this, new HexBookmarkForm(bookmark.Name, bookmark.Color, bookmark.Comment, bookmark.Type, region));
        if (form is null)
        {
            if (created) hex.Marks.Remove(bookmark.Id);
            return;
        }
        hex.Marks.Update(bookmark.Id, form.Name, form.Color, form.Comment, bookmark.Type);
        _app.SessionChanged();
    }

    // ---- export and import ----

    internal async Task ExportHexBookmarksAsync(HexView hex)
    {
        if (hex.Marks.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.HexBmNone));
            return;
        }
        string? path = await _app.Dialogs.PickSaveFileAsync(this, Loc.Get(K.HexBmExportTitle), System.IO.Path.GetFileNameWithoutExtension(ActiveDocPathOrTitle()) + ".bookmarks.csv");
        if (path is null) return;
        try
        {
            string text = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? hex.Marks.ToJson() : hex.Marks.ToCsv();
            await File.WriteAllTextAsync(path, text, new UTF8Encoding(false));
            SetStatusMessage(Loc.Plural(K.HexBmExported, hex.Marks.Count, ("path", path)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.HexBmExportFailed, ("reason", e.Message)));
        }
    }

    private string ActiveDocPathOrTitle() => Active?.Path ?? Active?.Title ?? "document";

    internal async Task ImportHexBookmarksAsync(HexView hex)
    {
        var files = await _app.Dialogs.PickFilesAsync(this);
        if (files.Count == 0) return;
        try
        {
            var result = hex.Marks.Import(await File.ReadAllTextAsync(files[0], Encoding.UTF8));
            SetStatusMessage(Loc.Get(K.HexBmImported, ("added", result.Added), ("skipped", result.Skipped)));
            _app.SessionChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            SetStatusMessage(Loc.Get(K.HexBmImportFailed, ("reason", e.Message)));
        }
    }
}
