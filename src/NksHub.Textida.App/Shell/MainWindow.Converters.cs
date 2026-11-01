using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Converters;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>A file shown through a converter: the converter, the file it came from and the bytes the file had when it was read or last saved.</summary>
internal sealed class ConvertedFile(IFileConverter converter, string path, byte[] original)
{
    public IFileConverter Converter { get; } = converter;
    public string Path { get; } = path;
    public byte[] Original { get; set; } = original;
}

/// <summary>
/// Open and save through a file type converter (PZ.5.2; the contract is <see cref="IFileConverter"/>). The converted text opens in a tab that has no path of its own; Save asks the converter for the bytes, reads them back
/// to check that the file shows the same text again, and writes the file only if that holds and the converter names no loss, or the user agrees to exactly that save. A converter that cannot write opens a read only tab.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The converters offered for a file; tests replace it.</summary>
    internal Func<string, IReadOnlyList<IFileConverter>> ConverterSource { get; set; } = FileConverters.For;

    internal CommandOutcome OpenThroughConverter()
    {
        _ = OpenThroughConverterAsync();
        return CommandOutcome.Handled;
    }

    internal async Task OpenThroughConverterAsync()
    {
        string? path = (await _app.Dialogs.PickFilesAsync(this)).FirstOrDefault();
        if (path is null) return;
        var converters = ConverterSource(path);
        if (converters.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.ConverterNone, ("file", Path.GetFileName(path))));
            return;
        }
        if (converters.Count == 1) { OpenConverted(path, converters[0]); return; }
        ShowChoice(Loc.Get(K.ConverterWatermark), Loc.Get(K.ConverterA11y), [.. converters.Select(c => new QuickRow(c.Name, null, c.CanWrite ? null : Loc.Get(K.ConverterReadOnly), null, true, null, c.Name, () => OpenConverted(path, c)))]);
    }

    /// <summary>Reads the file through the converter and opens the text in a tab; null (with the reason in the status bar) when the converter cannot read it.</summary>
    internal DocTab? OpenConverted(string path, IFileConverter converter)
    {
        byte[] bytes;
        ConverterRead read;
        try
        {
            bytes = File.ReadAllBytes(path);
            read = converter.Read(bytes);
        }
        catch (Exception e) when (e is ConverterException or IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.ConverterFailed, ("file", Path.GetFileName(path)), ("reason", e.Message)));
            return null;
        }
        var tab = OpenGeneratedText(read.Text, "plaintext");
        tab.Doc.Converted = new ConvertedFile(converter, Path.GetFullPath(path), bytes);
        tab.Doc.TitleOverride = () => Loc.Get(K.ConverterTitle, ("name", Path.GetFileName(path)), ("converter", converter.Name));
        tab.Doc.UserReadOnly = !converter.CanWrite;
        tab.Doc.Doc.MarkClean();
        SetStatusMessage(Loc.Get(converter.CanWrite ? K.ConverterOpened : K.ConverterOpenedReadOnly, ("file", Path.GetFileName(path)), ("converter", converter.Name)));
        return tab;
    }

    private async Task<bool> SaveConvertedAsync(DocTab tab, ConvertedFile file)
    {
        if (!file.Converter.CanWrite)
        {
            SetStatusMessage(Loc.Get(K.ConverterReadOnlyRefused, ("converter", file.Converter.Name)));
            return false;
        }
        string text = tab.Surface?.Text ?? tab.Doc.Doc.ReadText(new Core.Documents.ByteRange(tab.Doc.Doc.BomLength, tab.Doc.Doc.ByteLength));
        RoundTripReport report;
        try { report = ConverterRoundTrip.Check(file.Converter, text, file.Original); }
        catch (ConverterException e)
        {
            SetStatusMessage(Loc.Get(K.ConverterFailed, ("file", Path.GetFileName(file.Path)), ("reason", e.Message)));
            return false;
        }
        if (report.NeedsConsent)
        {
            var details = new List<string>(report.Losses);
            if (!report.Stable) details.Add(Loc.Get(K.ConverterUnstable));
            var ask = new ChoiceRequest(Loc.Get(K.ConverterLossyTitle), Loc.Get(K.ConverterLossyMessage, ("file", file.Path), ("converter", file.Converter.Name)),
                [new DialogButton(Loc.Get(K.ConverterSaveAnyway)), new DialogButton(Loc.Get(K.DialogCancel), IsDefault: true, IsCancel: true)], details, "converters");
            if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return false;
        }
        try
        {
            string temp = file.Path + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            await File.WriteAllBytesAsync(temp, report.Bytes);
            File.Move(temp, file.Path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.StatusSaveFailed, ("reason", e.Message)));
            return false;
        }
        file.Original = report.Bytes;
        tab.Doc.Doc.MarkClean();
        tab.Doc.NotifyChanged();
        SetStatusMessage(Loc.Get(K.ConverterSaved, ("file", Path.GetFileName(file.Path))));
        return true;
    }
}
