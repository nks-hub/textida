using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    private readonly PlaceList _edits = new();
    private readonly JumpHistory _jumps = new();

    /// <summary>Where the last edits happened, newest first; the places follow the text through further edits.</summary>
    internal PlaceList EditLocations => _edits;

    internal JumpHistory Jumps => _jumps;

    /// <summary>The last message of the status bar, for tests.</summary>
    internal string StatusMessageText => _message;

    // ---- places ----

    /// <summary>The caret of the active tab as a place, or null without a tab.</summary>
    internal Place? CurrentPlace()
    {
        if (Active is not { } tab) return null;
        var doc = tab.Doc.Doc;
        if (tab.Hex is { } hex) return Place.At(doc, hex.Caret);
        var (line, column) = tab.CaretPosition();
        var place = Place.At(doc, doc.TryGetOffset(new LinePosition(line - 1, column - 1), out long offset) ? offset : 0);
        place.Snippet = SnippetOf(place);
        return place;
    }

    /// <summary>The text of the line a live place is on, trimmed; null for a place of a closed document.</summary>
    private string? SnippetOf(Place place)
    {
        if (place.Document is not { } doc || _docs.FirstOrDefault(d => ReferenceEquals(d.Doc, doc))?.TextBackend is not { } text) return null;
        place.Refresh();
        int line = (int)Math.Clamp(place.Line + 1, 1, text.TextDocument.LineCount);
        var l = text.TextDocument.GetLineByNumber(line);
        var s = text.TextDocument.GetText(l.Offset, Math.Min(l.Length, 160)).Trim();
        return s.Length == 0 ? null : s;
    }

    /// <summary>Puts the caret on a 0-based line and column of the tab, clamped to the text.</summary>
    internal void MoveCaret(DocTab tab, long line, long column)
    {
        if (tab.Surface is { } surface)
        {
            var d = surface.Document;
            var l = d.GetLineByNumber((int)Math.Clamp(line + 1, 1, d.LineCount));
            surface.SetSelections([Sel.At(l.Offset + (int)Math.Clamp(column, 0, l.Length))]);
        }
        else tab.GoToLine((int)Math.Max(1, line + 1), Math.Max(1, column + 1));
    }

    /// <summary>Remembers where the caret is before the app moves it somewhere else, so Alt+Left comes back.</summary>
    internal void RecordJump()
    {
        if (CurrentPlace() is { } here) _jumps.Jumped(here);
    }

    /// <summary>Goes to a place: in its open document, or in its file opened again.</summary>
    internal DocTab? GoToPlace(Place place)
    {
        place.Refresh();
        DocTab? tab = null;
        if (place.Document is { } doc && _docs.FirstOrDefault(d => ReferenceEquals(d.Doc, doc)) is { } od && od.Views.Count > 0) tab = SelectTab(od.Views[0]);
        else if (place.Path is { } path)
        {
            try { tab = OpenFile(path, null); }
            catch (IOException e) { SetStatusMessage(e.Message); }
        }
        if (tab is null) return null;
        if (tab.Hex is { } hexView && place.Offset is { } offset)
        {
            hexView.MoveTo(offset);
            hexView.CenterIfOutside(offset);
        }
        else MoveCaret(tab, place.Line, place.Column);
        FocusView(tab);
        return tab;
    }

    /// <summary>Opens a file the user chose and puts the caret on the line; the place left behind is remembered.</summary>
    internal DocTab? JumpToFile(string path, int? line, int? column = null)
    {
        RecordJump();
        try
        {
            var tab = OpenFile(path, null);
            if (line is { } l) MoveCaret(tab, l - 1, (column ?? 1) - 1);
            return tab;
        }
        catch (IOException e)
        {
            SetStatusMessage(e.Message);
            return null;
        }
    }

    internal void JumpBack() => Jump(back: true);

    internal void JumpForward() => Jump(back: false);

    private void Jump(bool back)
    {
        var here = CurrentPlace();
        var target = here is null ? null : back ? _jumps.Back(here) : _jumps.Forward(here);
        if (target is null) SetStatusMessage(Loc.Get(back ? K.NavNoBack : K.NavNoForward));
        else GoToPlace(target);
    }

    // ---- bookkeeping when documents come and go ----

    /// <summary>The window closes: the folder watcher and the file index stop.</summary>
    private void ReleaseNavigation()
    {
        _fileTree?.Dispose();
        _fileTree = null;
        InvalidateWorkspaceIndex();
    }

    private void TrackEdits(OpenDocument od)
    {
        if (!od.IsVirtual) _edits.Attach(od.Doc); // the owner's updates are not edits of the user
    }

    /// <summary>The last view of a document closes: its places become file positions that survive in the lists.</summary>
    private void ForgetDocument(OpenDocument od)
    {
        _edits.Detach(od.Doc, place => place.Snippet = SnippetOf(place) ?? place.Snippet);
        _jumps.Detach(od.Doc);
    }

    /// <summary>A file or folder was renamed or moved on disk: open documents, tabs, recent files and places follow it.</summary>
    internal void ApplyPathMove(string oldPath, string newPath)
    {
        foreach (var od in _docs.ToList())
            if (od.Path is { } p && PathMove.Rebase(p, oldPath, newPath) is { } moved)
            {
                od.MoveTo(moved);
                DismissBars(od);
            }
        _edits.Rename(oldPath, newPath);
        _jumps.Rename(oldPath, newPath);
        ApplyLanguageOptions();
        RefreshAll();
        _app.SessionChanged();
    }

    // ---- related file and the path under the caret ----

    internal void GoToRelatedFile()
    {
        if (SpecialTarget() is not { } path)
        {
            SetStatusMessage(Loc.Get(K.RelatedUnsaved));
            return;
        }
        var rules = RelatedFiles.ParseRules(_app.Workbench.Settings.GetString("files.relatedFileRules"));
        var found = RelatedFiles.Find(path, rules);
        if (found.Count == 0) { SetStatusMessage(Loc.Get(K.RelatedNone, ("name", Path.GetFileName(path)))); return; }
        if (found.Count == 1) { JumpToFile(found[0], null); return; }
        ShowChoice(Loc.Get(K.PaletteRelatedWatermark), Loc.Get(K.A11yRelated), [.. found.Select(f =>
            new QuickRow(Path.GetFileName(f), null, Display(Path.GetDirectoryName(f)!), null, true, null, Path.GetFileName(f), () => JumpToFile(f, null), () => ReadPreview(f)))]);
    }

    /// <summary>Reads a path from the text at the caret (<c>src/a.cs:12</c> too) and opens the file, or shows the folder in the file panel.</summary>
    internal void OpenPathAtCaret()
    {
        if (Active?.Surface is not { } surface)
        {
            SetStatusMessage(Loc.Get(K.FilesOpenPathNone));
            return;
        }
        var d = surface.Document;
        var line = d.GetLineByOffset(surface.CaretOffset);
        if (PathAtCaret.Find(d.GetText(line.Offset, line.Length), surface.CaretOffset - line.Offset) is not { } reference)
        {
            SetStatusMessage(Loc.Get(K.FilesOpenPathNone));
            return;
        }
        string text = reference.Text;
        if (text.StartsWith("~/", StringComparison.Ordinal)) text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text[2..]);
        var bases = new[] { Active.Path is { } p ? Path.GetDirectoryName(p) : null, FileRootFolder }.OfType<string>();
        string? resolved = Path.IsPathRooted(text)
            ? text
            : bases.Select(b => Path.GetFullPath(Path.Combine(b, text))).FirstOrDefault(c => File.Exists(c) || Directory.Exists(c));
        if (resolved is not null && File.Exists(resolved)) JumpToFile(resolved, reference.Line, reference.Column);
        else if (resolved is not null && Directory.Exists(resolved)) _ = ShowFolderAsync(resolved);
        else SetStatusMessage(Loc.Get(K.FilesErrorNotFound, ("path", reference.Text)));
    }
}
