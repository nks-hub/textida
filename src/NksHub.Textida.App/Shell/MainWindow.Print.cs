using Avalonia.Media;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Printing (PZ.2.2): the pages carry the syntax colours of the editor on white paper, line numbers and a header with the name and the page number. Windows prints through its own print dialog;
/// Avalonia has no print API, so macOS and Linux write the same pages as a PDF file to print from a viewer.
/// </summary>
internal sealed partial class MainWindow
{
    private static int Rgb(Color c) => (c.R << 16) | (c.G << 8) | c.B;

    /// <summary>Lays the selection, or the whole document, out as source lines of coloured runs; null (with the reason in the status bar) when there is nothing to print.</summary>
    private async Task<(List<List<PrintRun>> Lines, string Title)?> BuildPrintLinesAsync()
    {
        var source = await ReadExportSourceAsync();
        if (source is null || source.Problem is not null)
        {
            SetStatusMessage(source?.Problem ?? Loc.Get(K.ExportNothing));
            return null;
        }
        if (source.Text.Length > PluginLimits.HighlightExportMaxChars)
        {
            SetStatusMessage(Loc.Get(K.ExportTooLarge, ("chars", source.Text.Length), ("limit", PluginLimits.HighlightExportMaxChars)));
            return null;
        }
        const int ink = 0x000000;
        var palette = SyntaxPalette.Light; // paper is white, so the colours of the light theme read on it whatever theme the editor shows
        var lines = await Task.Run(() =>
        {
            var segments = HighlightExport.Segments(source.Text, source.Tokenizer, source.Levels, palette, source.Range, default, source.StartState)
                .Select(s => (s.Start, s.Length, s.Color is { } c ? (int?)Rgb(c) : null));
            return PrintLayout.SourceLines(source.Text, segments, ink);
        });
        return (lines, source.Title);
    }

    /// <summary>File → Print: the print dialog of Windows; elsewhere the PDF file.</summary>
    internal CommandOutcome PrintDocument()
    {
        if (Active is not { Custom: null, Hex: null }) return CommandOutcome.NotHandled;
        _ = PrintDocumentAsync();
        return CommandOutcome.Handled;
    }

    internal CommandOutcome ExportPdf()
    {
        if (Active is not { Custom: null, Hex: null }) return CommandOutcome.NotHandled;
        _ = ExportPdfAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>Hook of the tests: what prints instead of the dialog of Windows.</summary>
    internal Func<string, Func<PrintOptions, IReadOnlyList<PrintPage>>, (WindowsPrinter.Outcome, string?)>? PrintOverride { get; set; }

    internal async Task PrintDocumentAsync()
    {
        if (!OperatingSystem.IsWindows() && PrintOverride is null)
        {
            SetStatusMessage(Loc.Get(K.PrintNoDialog));
            await ExportPdfAsync();
            return;
        }
        if (await BuildPrintLinesAsync() is not { } built) return;
        var (lines, title) = built;
        Func<PrintOptions, IReadOnlyList<PrintPage>> layout = options => PrintLayout.Paginate(lines, options);
        (WindowsPrinter.Outcome Outcome, string? Error) result;
        if (PrintOverride is { } over) result = over(title, layout);
        else
        {
            nint handle = TryGetPlatformHandle()?.Handle ?? 0;
            result = WindowsPrinter.PrintWithDialog(handle, title, layout, true);
        }
        SetStatusMessage(result.Outcome switch
        {
            WindowsPrinter.Outcome.Printed => Loc.Get(K.PrintDone, ("name", title)),
            WindowsPrinter.Outcome.Cancelled => Loc.Get(K.PrintCancelled),
            _ => Loc.Get(K.PrintFailed, ("reason", result.Error ?? "")),
        });
    }

    internal async Task ExportPdfAsync()
    {
        if (await BuildPrintLinesAsync() is not { } built) return;
        var (lines, title) = built;
        string stem = Path.GetFileNameWithoutExtension(Active?.Doc.Title ?? "export");
        string? path = await _app.Dialogs.PickSaveFileAsync(this, Loc.Get(K.PrintPdfTitle), stem + ".pdf");
        if (path is null) return;
        try
        {
            var pages = PrintLayout.Paginate(lines, PdfExport.Options(true));
            await File.WriteAllBytesAsync(path, PdfExport.Write(pages, title));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.ExportFailed, ("reason", e.Message)));
            return;
        }
        SetStatusMessage(Loc.Get(K.PrintPdfDone, ("name", Path.GetFileName(path))));
    }
}
