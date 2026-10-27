using System.Globalization;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AvaloniaEdit.Document;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The clipboard history, the special paste (keep the caret, as HTML, from a file), and the line and block commands that need a dialog or a setting: insert into lines, sort, select by range,
/// align, wrap, date and time, GUID. The text work is in <see cref="LineOperations"/>; each command here runs it as one undo step through <see cref="RunEdit"/>.
/// </summary>
internal sealed partial class MainWindow
{
    private const int PreviewLines = 8;

    // ---- clipboard history ----

    /// <summary>Remembers a text that was copied or cut, for <see cref="ShowClipboardHistory"/>.</summary>
    internal void RememberCopy(string? text) => _app.ClipboardHistory.Add(text);

    /// <summary>Ctrl+Shift+V: the last copied texts with a preview; the chosen one is pasted at every caret as one undo step.</summary>
    internal CommandOutcome ShowClipboardHistory()
    {
        bool canPaste = Active?.Paged is not null || ActiveEditor is { IsReadOnly: false };
        if (!canPaste) return CommandOutcome.NotHandled;
        var rows = _app.ClipboardHistory.Items.Select(text =>
        {
            string line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            int lines = text.Count(c => c == '\n') + (text.EndsWith('\n') ? 0 : 1);
            string size = Loc.Plural(K.ClipLines, lines);
            return new QuickRow(line, size, null, null, true, null, $"{size}: {line}", () => PasteFromHistory(text), () => FirstLines(text));
        }).ToList();
        ShowChoice(Loc.Get(K.ClipWatermark), Loc.Get(K.A11yClip), rows, emptyText: Loc.Get(K.ClipEmpty));
        return CommandOutcome.Handled;
    }

    private void PasteFromHistory(string text)
    {
        if (Active?.Paged is { } paged) paged.PasteText(text);
        else if (ActiveEditor is { IsReadOnly: false } surface) PasteText(surface, text);
    }

    // ---- paste without moving the caret ----

    /// <summary>The clipboard text goes in as Paste would put it, but the caret stays where it was (at the start of the new text); with several carets each keeps its place, in a block selection the caret returns to the top left.</summary>
    internal async Task<CommandOutcome> PasteKeepCaretAsync()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        var text = Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null;
        if (string.IsNullOrEmpty(text)) return CommandOutcome.Handled;
        text = TextUtilities.NormalizeNewLines(text, surface.Doc.NewLine(surface.Settings.ConfiguredEol));
        if (surface.IsBlockSelection)
        {
            using (surface.Doc.Doc.BeginUndoGroup())
            {
                int top = surface.GetSelections().Min(s => s.Start);
                string newLine = surface.Doc.NewLine(surface.Settings.ConfiguredEol);
                surface.SetSelections(ColumnEdit.Paste(surface.Document, surface.GetSelections(), text, newLine));
                surface.TextArea.ClearSelection();
                surface.ToggleBlockModeOff();
                surface.SetSelections([Sel.At(top)]);
            }
            return CommandOutcome.Handled;
        }
        var pieces = MultiCaret.SplitForCarets(text, surface.GetSelections().Count);
        return RunEdit((s, sels) => LineOperations.ReplaceSelections(s.Document, sels, i => pieces?[i] ?? text, caretBefore: true));
    }

    // ---- paste as HTML ----

    /// <summary>The HTML form of the clipboard (what a browser or a word processor copied) goes in as source text, the way Paste puts text in; without one the status bar says so.</summary>
    internal async Task<CommandOutcome> PasteAsHtmlAsync()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        string? html = null;
        if (Clipboard is { } clipboard && await clipboard.TryGetDataAsync() is { } data)
        {
            using (data)
                if (await data.TryGetValueAsync(ClipboardHtml.Format) is { } raw) html = ClipboardHtml.Decode(raw);
        }
        if (string.IsNullOrEmpty(html)) SetStatusMessage(Loc.Get(K.LinesPasteHtmlNone));
        else if (ReferenceEquals(ActiveEditor, surface) && !surface.IsReadOnly) PasteText(surface, html);
        return CommandOutcome.Handled;
    }

    // ---- insert from file ----

    /// <summary>The text of a file the user picks, its encoding guessed as on open and its line endings turned into those of the document, at every caret as one undo step.</summary>
    internal CommandOutcome InsertFromFile()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        _ = InsertFromFileAsync(surface);
        return CommandOutcome.Handled;
    }

    private async Task InsertFromFileAsync(EditorSurface surface)
    {
        var picked = await _app.Dialogs.PickFilesAsync(this);
        if (picked.Count == 0) return;
        string path = picked[0];
        string? text;
        try { text = await Task.Run(() => ReadInsertedFile(path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            SetStatusMessage(Loc.Get(K.ErrorOpenFailed, ("path", path), ("reason", e.Message)));
            return;
        }
        if (text is null)
        {
            SetStatusMessage(Loc.Get(K.LinesInsertFileTooLarge, ("name", Path.GetFileName(path)), ("limit", PluginLimitsPlaceholders.InsertFromFileMaxBytes)));
            return;
        }
        if (!ReferenceEquals(ActiveEditor, surface)) return;
        text = TextUtilities.NormalizeNewLines(text, surface.Doc.NewLine(surface.Settings.ConfiguredEol));
        RunEdit((s, sels) => LineOperations.ReplaceSelections(s.Document, sels, _ => text));
    }

    /// <summary>The decoded text of the file; null when it is larger than <see cref="PluginLimitsPlaceholders.InsertFromFileMaxBytes"/>.</summary>
    private static string? ReadInsertedFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > PluginLimitsPlaceholders.InsertFromFileMaxBytes) return null;
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        var codec = EncodingGuesser.Guess(bytes.AsSpan(0, Math.Min(bytes.Length, OpenDocument.SampleBytes)), bytes.Length <= OpenDocument.SampleBytes).Codec;
        return codec.Decode(bytes).Text;
    }

    // ---- insert into lines ----

    internal CommandOutcome InsertIntoLines(bool repeat)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        _ = InsertIntoLinesAsync(surface, repeat);
        return CommandOutcome.Handled;
    }

    private async Task InsertIntoLinesAsync(EditorSurface surface, bool repeat)
    {
        var options = _app.LastParameters.Insert;
        if (!repeat || options is null)
        {
            options = await _app.Dialogs.InsertIntoLinesAsync(this, options ?? new LineInsertOptions(), o => InsertPreview(surface, o));
            if (options is null) return;
        }
        _app.LastParameters.Insert = options;
        if (ReferenceEquals(ActiveEditor, surface)) RunEdit((s, sels) => LineOperations.InsertIntoLines(s.Document, sels, options));
    }

    /// <summary>The first lines of what the command would change, with the choices applied to a copy of them.</summary>
    private static string InsertPreview(EditorSurface surface, LineInsertOptions options)
    {
        var d = surface.Document;
        var sels = surface.GetSelections();
        bool lone = sels.Count == 1 && sels[0].IsEmpty;
        var chosen = sels.OrderBy(s => s.Start).First();
        int first = lone ? 1 : d.GetLineByOffset(chosen.Start).LineNumber;
        var end = d.GetLineByOffset(chosen.End);
        int selected = lone ? d.LineCount : !chosen.IsEmpty && chosen.End == end.Offset && end.LineNumber > first ? end.LineNumber - 1 : end.LineNumber;
        int last = Math.Min(selected, first + PreviewLines - 1);
        int from = d.GetLineByNumber(first).Offset;
        var copy = new TextDocument(d.GetText(from, d.GetLineByNumber(last).EndOffset - from));
        IReadOnlyList<Sel> inside = options.Place == LineInsertPlace.Selections
            ? [.. Enumerable.Range(1, copy.LineCount).Select(n => Sel.At(copy.GetLineByNumber(n).Offset))]
            : [new Sel(0, copy.TextLength)];
        LineOperations.InsertIntoLines(copy, inside, options);
        return copy.Text;
    }

    // ---- sort ----

    internal CommandOutcome SortLines(bool repeat)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        _ = SortLinesAsync(surface, repeat);
        return CommandOutcome.Handled;
    }

    private async Task SortLinesAsync(EditorSurface surface, bool repeat)
    {
        var options = _app.LastParameters.Sort;
        if (!repeat || options is null)
        {
            options = await _app.Dialogs.SortLinesAsync(this, options ?? new SortOptions(), o => LineOperations.SortPreview(surface.Document, surface.GetSelections(), o, PreviewLines));
            if (options is null) return;
        }
        _app.LastParameters.Sort = options;
        if (ReferenceEquals(ActiveEditor, surface)) RunEdit((s, sels) => LineOperations.SortLines(s.Document, sels, options));
    }

    // ---- select lines by range ----

    internal CommandOutcome SelectLinesByRange(bool repeat)
    {
        if (ActiveEditor is not { } surface) return CommandOutcome.NotHandled;
        _ = SelectLinesByRangeAsync(surface, repeat);
        return CommandOutcome.Handled;
    }

    private async Task SelectLinesByRangeAsync(EditorSurface surface, bool repeat)
    {
        string? text = _app.LastParameters.Ranges;
        if (!repeat || text is null)
        {
            text = await _app.Dialogs.PromptAsync(this, Loc.Get(K.LinesRangesTitle), Loc.Get(K.LinesRangesLabel), text ?? "", t => RangesPreview(surface.Document, t));
            if (text is null) return;
        }
        _app.LastParameters.Ranges = text;
        if (!LineText.TryParseRanges(text, out var ranges)) { SetStatusMessage(Loc.Get(K.LinesRangesInvalid)); return; }
        var sels = LineOperations.SelectLineRanges(surface.Document, ranges);
        if (sels.Count == 0) { SetStatusMessage(Loc.Get(K.LinesRangesBeyond, ("total", surface.Document.LineCount))); return; }
        surface.ToggleBlockModeOff();
        surface.SetSelections(sels);
    }

    private static string RangesPreview(TextDocument d, string text)
    {
        if (text.Trim().Length == 0) return "";
        if (!LineText.TryParseRanges(text, out var ranges)) return Loc.Get(K.LinesRangesInvalid);
        var inside = ranges.Where(r => r.From <= d.LineCount).ToList();
        if (inside.Count == 0) return Loc.Get(K.LinesRangesBeyond, ("total", d.LineCount));
        return Loc.Get(K.LinesRangesResult, ("selections", inside.Count), ("lines", inside.Sum(r => Math.Min(r.To, d.LineCount) - r.From + 1)));
    }

    // ---- align and wrap ----

    internal CommandOutcome Justify(Justification mode) =>
        RunEdit((s, sels) => LineOperations.JustifyLines(s.Document, sels, mode));

    /// <summary>
    /// Wraps the paragraphs at the right margin column; without one set the user is told where to set it. With <paramref name="shortLines"/> a line
    /// shorter than <see cref="PluginLimitsPlaceholders.ReformatShortLinePercent"/> percent of the margin also ends its paragraph.
    /// </summary>
    internal CommandOutcome Reformat(bool shortLines = false)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        int width = surface.Settings.RightMargin;
        if (width <= 0)
        {
            SetStatusMessage(Loc.Get(K.LinesReformatNoMargin));
            return CommandOutcome.Handled;
        }
        int shortLine = shortLines ? Math.Max(1, width * PluginLimitsPlaceholders.ReformatShortLinePercent / 100) : 0;
        return RunEdit((s, sels) => LineOperations.ReformatParagraphs(s.Document, sels, width, shortLine));
    }

    // ---- date, time and GUID ----

    /// <summary>The date and time in the format of the setting, with names in the language of the interface, at every caret.</summary>
    internal CommandOutcome InsertDateTime()
    {
        if (ActiveEditor is not { IsReadOnly: false }) return CommandOutcome.NotHandled;
        string text;
        try { text = _app.Clock().ToString(_app.Workbench.Settings.GetString("editor.dateTimeFormat"), CultureInfo.GetCultureInfo(Loc.Language)); }
        catch (FormatException e)
        {
            SetStatusMessage(Loc.Get(K.LinesDateFormatInvalid, ("reason", e.Message)));
            return CommandOutcome.Handled;
        }
        return RunEdit((s, sels) => LineOperations.ReplaceSelections(s.Document, sels, _ => text));
    }

    /// <summary>A new GUID (lower case, hyphens, no braces) at every caret, each caret its own.</summary>
    internal CommandOutcome InsertGuid() =>
        RunEdit((s, sels) => LineOperations.ReplaceSelections(s.Document, sels, _ => Guid.NewGuid().ToString()));
}
