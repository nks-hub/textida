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
