using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>HTML tools on the text of the active editor (P4.6.5): close the open tag, go to the partner tag, select, remove or wrap an element, strip tags.</summary>
internal sealed partial class MainWindow
{
    /// <summary>Writes the end tag of the nearest open tag before the caret at the caret.</summary>
    internal CommandOutcome HtmlCloseTag()
    {
        return RunEdit((s, sels) =>
        {
            var d = s.Document;
            int caret = sels[0].End;
            if (HtmlTags.UnclosedBefore(d.Text, caret) is not { } name)
            {
                SetStatusMessage(Loc.Get(K.HtmlNoOpenTag));
                return sels;
            }
            string tag = "</" + name + ">";
            d.Insert(caret, tag);
            return [Sel.At(caret + tag.Length), .. sels.Skip(1)];
        });
    }

    /// <summary>Moves the caret to the start of the partner of the tag at the caret.</summary>
    internal CommandOutcome HtmlMatchTag()
    {
        if (ActiveEditor is not { } surface) return CommandOutcome.NotHandled;
        if (!HtmlTags.TryMatch(surface.Document.Text, surface.CaretOffset, out int partner)) SetStatusMessage(Loc.Get(K.HtmlNoTag));
        else surface.SetSelections([Sel.At(partner)]);
        return CommandOutcome.Handled;
    }

    /// <summary>Selects the element around the selection; again, the element around that.</summary>
    internal CommandOutcome HtmlSelectElement()
    {
        if (ActiveEditor is not { } surface) return CommandOutcome.NotHandled;
        var sel = surface.GetSelections()[0];
        if (!HtmlTags.TryElement(surface.Document.Text, sel.Start, sel.End, out int start, out int end)) SetStatusMessage(Loc.Get(K.HtmlNoTag));
        else surface.SetSelections([new Sel(start, end)]);
        return CommandOutcome.Handled;
    }

    /// <summary>Removes the start and end tag of the element at the caret and keeps its content.</summary>
    internal CommandOutcome HtmlRemoveTag() => RunEdit((s, sels) =>
    {
        var d = s.Document;
        var tags = HtmlTags.TagsToRemove(d.Text, sels[0].Start);
        if (tags.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.HtmlNoTag));
            return sels;
        }
        foreach (var t in tags) d.Remove(t.Start, t.End - t.Start);
        return [Sel.At(tags[^1].Start)];
    });

    /// <summary>Removes the tags and comments from the selections, from the whole text when nothing is selected.</summary>
    internal CommandOutcome HtmlStripTags() => RunEdit((s, sels) =>
    {
        var d = s.Document;
        IEnumerable<(int Start, int End)> ranges = sels.Any(x => !x.IsEmpty)
            ? sels.Where(x => !x.IsEmpty).OrderByDescending(x => x.Start).Select(x => (x.Start, x.End))
            : [(0, d.TextLength)];
        int first = 0;
        foreach (var (start, end) in ranges)
        {
            d.Replace(start, end - start, HtmlTags.Strip(d.GetText(start, end - start)));
            first = start;
        }
        return [Sel.At(first)];
    });

    /// <summary>The pixel size of an image file, or null when its header does not tell it. Throws <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> for a file that cannot be read.</summary>
    internal static (int Width, int Height)? ImageSizeOf(string path)
    {
        using var stream = File.OpenRead(path);
        return HtmlInsert.TryImageSize(stream, out int width, out int height) ? (width, height) : null;
    }

    /// <summary>
    /// Writes <paramref name="piece"/> (text and caret offset in it) for every selection, one after the other in document order. <paramref name="pieceFor"/> gets the text of the
    /// selection as it was before anything was written.
    /// </summary>
    private void InsertPieces(Func<string, HtmlInsert.Piece> pieceFor) => RunEdit((s, sels) =>
    {
        var ordered = sels.OrderBy(x => x.Start).ToList();
        var pieces = ordered.Select(x => pieceFor(s.Document.GetText(x.Start, x.Length))).ToList();
        return LineOperations.ReplaceSelectionsWithCarets(s.Document, ordered, i => (pieces[i].Text, [pieces[i].Caret]));
    });

    /// <summary>The text that goes into an attribute for a selection: a selection on one line, none otherwise.</summary>
    private static string SingleLine(string selected) => selected.Contains('\n') || selected.Contains('\r') ? "" : selected;

    /// <summary>Asks for an image file and writes its <c>img</c> tag, with the size of the picture, in place of every selection; a selected text becomes the alt text.</summary>
    internal async Task HtmlInsertImageAsync()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return;
        var path = await _app.Dialogs.PickFileAsync(this, Loc.Get(K.HtmlImageTitle), Loc.Get(K.HtmlImageFilter), HtmlInsert.ImagePatterns);
        if (path is null || !ReferenceEquals(ActiveEditor, surface)) return;
        (int Width, int Height)? size;
        try { size = ImageSizeOf(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.HtmlImageUnreadable, ("reason", e.Message)));
            return;
        }
        string reference = HtmlInsert.RelativeReference(ActiveDirectory(), path);
        InsertPieces(selected => HtmlInsert.Image(reference, size, SingleLine(selected)));
        if (size is null) SetStatusMessage(Loc.Get(K.HtmlImageNoSize));
    }

    /// <summary>
    /// Asks for an address and writes a link to it around every selection. A file that exists is written as a reference relative to the document; with nothing selected
    /// the link of an image file holds the picture, with its width and height, and any other link shows its address.
    /// </summary>
    internal async Task HtmlInsertLinkAsync()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return;
        var typed = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.HtmlLinkTitle), Loc.Get(K.HtmlLinkLabel), ""))?.Trim();
        if (string.IsNullOrEmpty(typed) || !ReferenceEquals(ActiveEditor, surface)) return;
        string? directory = ActiveDirectory();
        string? file = HtmlInsert.LocalFile(typed, directory);
        string reference = file is null ? typed : HtmlInsert.RelativeReference(directory, file);
        string shown = typed.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal);
        if (file is not null && HtmlInsert.IsImageFile(file))
        {
            (int Width, int Height)? size = null;
            try { size = ImageSizeOf(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SetStatusMessage(Loc.Get(K.HtmlImageUnreadable, ("reason", e.Message)));
                return;
            }
            shown = HtmlInsert.Image(reference, size).Text;
            if (size is null) SetStatusMessage(Loc.Get(K.HtmlImageNoSize));
        }
        InsertPieces(selected => HtmlInsert.Link(reference, selected.Length > 0 ? selected : shown));
    }

    /// <summary>Asks for a tag name and wraps the selection in it.</summary>
    internal async Task HtmlWrapTagAsync()
    {
        if (ActiveEditor is not { IsReadOnly: false }) return;
        var name = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.HtmlWrapTitle), Loc.Get(K.HtmlWrapLabel), "div"))?.Trim();
        if (name is null) return;
        if (!HtmlTags.IsName(name))
        {
            SetStatusMessage(Loc.Get(K.HtmlWrapInvalid));
            return;
        }
        RunEdit((s, sels) =>
        {
            var d = s.Document;
            var result = new List<Sel>();
            int shift = 0;
            foreach (var x in sels.OrderBy(x => x.Start))
            {
                string open = "<" + name + ">", close = "</" + name + ">";
                d.Insert(x.End + shift, close);
                d.Insert(x.Start + shift, open);
                result.Add(new Sel(x.Start + shift + open.Length, x.End + shift + open.Length));
                shift += open.Length + close.Length;
            }
            return result;
        });
    }
}
