using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Core.Syntax;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>Export with highlighting (P9.3.2): HTML and RTF files, and a copy that puts text, HTML and RTF on the clipboard together.</summary>
internal sealed partial class MainWindow
{
    internal enum ExportForm { Html, Rtf }

    /// <summary>What an export takes: the text, the part of it chosen (null for all), and what colors it. A large file is taken by its selection alone.</summary>
    private sealed record ExportSource(string Text, (int Start, int Length)? Range, Tokenizer? Tokenizer, LogLevelRules? Levels, string Title);

    private ExportSource? ReadExportSource()
    {
        if (Active is not { Custom: null, Hex: null } tab) return null;
        var od = tab.Doc;
        var tokenizer = LanguageModes.GrammarId(od) is { } id ? LanguageModes.Tokenizer(id) : null;
        var levels = od.IsLog ? od.LogLevels : null;
        string title = od.Title;
        if (tab.Paged is { } paged)
        {
            string selected = paged.SelectedText();
            return selected.Length == 0 ? null : new ExportSource(selected, null, tokenizer, levels, title);
        }
        if (tab.Surface is not { } surface || tab.TableShown) return null;
        string newLine = od.NewLine(surface.Settings.ConfiguredEol);
        if (surface.IsMulti || surface.IsBlockSelection)
            return (surface.SecretsHidden ? surface.MaskedSelection(newLine) : surface.SelectedTexts(newLine)) is { Length: > 0 } picked ? new ExportSource(picked, null, tokenizer, levels, title) : null;
        string text = surface.Text;
        if (surface.SecretsHidden) // the export carries the placeholder, as the clipboard does
        {
            var spans = surface.Secrets();
            string shown = surface.SelectionLength > 0 ? SecretValues.Mask(text.Substring(surface.SelectionStart, surface.SelectionLength), spans, surface.SelectionStart) : SecretValues.Mask(text, spans);
            return new ExportSource(shown, null, tokenizer, levels, title);
        }
        return new ExportSource(text, surface.SelectionLength > 0 ? (surface.SelectionStart, surface.SelectionLength) : null, tokenizer, levels, title);
    }

    private ExportTheme CurrentExportTheme()
    {
        var variant = ActualThemeVariant;
        bool dark = variant == ThemeVariant.Dark;
        Color Pick(string key, Color fallback) => this.TryFindResource(key, variant, out var v) && v is ISolidColorBrush brush ? brush.Color : fallback;
        var options = Options;
        return new ExportTheme(Pick("BgEditor", dark ? Colors.Black : Colors.White), Pick("Fg", dark ? Colors.White : Colors.Black),
            dark ? SyntaxPalette.Dark : SyntaxPalette.Light, options.FullFontFamily, options.FontSize * 0.75);
    }

    /// <summary>Writes the export of the source; the work runs off the UI thread. Null (with the reason in the status bar) when there is nothing to export or the text is over the limit.</summary>
    private async Task<(string Html, string Rtf)?> BuildExportAsync(ExportSource? source, ExportForm? only)
    {
        if (source is null)
        {
            SetStatusMessage(Loc.Get(K.ExportNothing));
            return null;
        }
        if (source.Text.Length > PluginLimitsPlaceholders.HighlightExportMaxChars)
        {
            SetStatusMessage(Loc.Get(K.ExportTooLarge, ("chars", source.Text.Length), ("limit", PluginLimitsPlaceholders.HighlightExportMaxChars)));
            return null;
        }
        var theme = CurrentExportTheme();
        return await Task.Run(() => (
            only == ExportForm.Rtf ? "" : HighlightExport.Html(source.Text, source.Tokenizer, source.Levels, theme, only == ExportForm.Html ? source.Title : null, source.Range),
            only == ExportForm.Html ? "" : HighlightExport.Rtf(source.Text, source.Tokenizer, source.Levels, theme, source.Range)));
    }

    /// <summary>File → Export as HTML / RTF: asks for the file and writes the selection, or the whole document, with the colors of the editor.</summary>
    internal CommandOutcome ExportHighlighted(ExportForm form)
    {
        if (Active is not { Custom: null, Hex: null }) return CommandOutcome.NotHandled;
        _ = ExportHighlightedAsync(form);
        return CommandOutcome.Handled;
    }

    private async Task ExportHighlightedAsync(ExportForm form)
    {
        string stem = Path.GetFileNameWithoutExtension(Active?.Doc.Title ?? "export");
        if (await BuildExportAsync(ReadExportSource(), form) is not { } built) return;
        string? path = await _app.Dialogs.PickSaveFileAsync(this, Loc.Get(K.ExportTitle), stem + (form == ExportForm.Html ? ".html" : ".rtf"));
        if (path is null) return;
        try
        {
            if (form == ExportForm.Html) await File.WriteAllTextAsync(path, built.Html, new UTF8Encoding(false));
            else await File.WriteAllTextAsync(path, built.Rtf, Encoding.ASCII);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.ExportFailed, ("reason", e.Message)));
            return;
        }
        SetStatusMessage(Loc.Get(K.ExportDone, ("name", Path.GetFileName(path))));
    }

    /// <summary>Edit → Copy as: with formatting (text, HTML and RTF together) or the HTML source as plain text.</summary>
    internal CommandOutcome CopyHighlighted(bool htmlSource)
    {
        if (Active is not { Custom: null, Hex: null }) return CommandOutcome.NotHandled;
        _ = CopyHighlightedAsync(htmlSource);
        return CommandOutcome.Handled;
    }

    private async Task CopyHighlightedAsync(bool htmlSource)
    {
        var source = ReadExportSource();
        if (await BuildExportAsync(source, null) is not { } built || source is null) return;
        string plain = source.Range is { } r ? source.Text.Substring(r.Start, r.Length) : source.Text;
        if (htmlSource) WriteClipboard(built.Html, null);
        else WriteClipboard(plain, built.Html, built.Rtf);
        SetStatusMessage(Loc.Get(K.ExportCopied));
    }
}
