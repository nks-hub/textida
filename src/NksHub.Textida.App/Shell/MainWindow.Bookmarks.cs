using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    /// <summary>The active tab when it shows text (normal or paged surface); bookmarks do not apply to hex and result tabs.</summary>
    private DocTab? TextTab => Active is { Hex: null, Custom: null } tab ? tab : null;

    /// <summary>Sets or removes the bookmark on the line of the caret.</summary>
    internal bool ToggleBookmark()
    {
        if (ActiveHex is { } hex) return ToggleHexBookmark(hex);
        if (TextTab is not { } tab) return false;
        long line = tab.CaretPosition().Line - 1;
        if (!tab.Doc.Bookmarks.Toggle(line, out bool added)) SetStatusMessage(Loc.Get(K.GoToNotIndexed));
        else
        {
            SetStatusMessage(Loc.Get(added ? K.BookmarkAdded : K.BookmarkRemoved, ("line", line + 1)));
            _app.SessionChanged();
        }
        return true;
    }

    /// <summary>Moves the caret to the next or previous bookmark, wrapping around.</summary>
    internal bool StepBookmark(bool forward)
    {
        if (ActiveHex is { } hex) return StepHexBookmark(hex, forward);
        if (TextTab is not { } tab) return false;
        if (tab.Doc.Bookmarks.Step(tab.CaretPosition().Line - 1, forward) is not { } line) SetStatusMessage(Loc.Get(K.BookmarkNone));
        else
        {
            MoveCaret(tab, line, 0);
            FocusView(tab);
        }
        return true;
    }

    internal bool ClearBookmarks()
    {
        if (ActiveHex is { } hex) return ClearHexBookmarks(hex);
        if (TextTab is not { } tab) return false;
        tab.Doc.Bookmarks.Clear();
        SetStatusMessage(Loc.Get(K.BookmarksCleared));
        _app.SessionChanged();
        return true;
    }
}
