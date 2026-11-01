using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The commands of the plugin points of the hex view (PZ.8.4): a hash, a text form of the selection and structure templates, each from the plugins that may read bytes (<c>hex.read</c>).
/// The plugin is sent a copy of the bytes (a hidden value as stars) and answers with text; what comes back is shown or opened as a new unsaved document, so no answer writes anything.
/// </summary>
internal sealed partial class MainWindow
{
    private IReadOnlyList<HexToolItem> HexPluginItems(HexKind kind, Func<HexOffer, Task> run)
    {
        var offers = _app.HexPlugins.Offers(kind);
        if (offers.Count == 0) SetStatusMessage(Loc.Get(K.HexPluginNone));
        return [.. offers.Select(o => new HexToolItem($"{o.Title} ({o.PluginName})", () => run(o)))];
    }

    internal IReadOnlyList<HexToolItem> HexPluginHashItems(HexView hex) => HexPluginItems(HexKind.Hash, o => HexPluginHashAsync(hex, o));

    internal IReadOnlyList<HexToolItem> HexPluginExportItems(HexView hex) => HexPluginItems(HexKind.Exporter, o => HexPluginExportAsync(hex, o));

    internal IReadOnlyList<HexToolItem> HexPluginTemplateItems(HexView hex) => HexPluginItems(HexKind.Templates, o => HexPluginTemplatesAsync(hex, o));

    /// <summary>Copies the bytes of the range for a plugin as the view shows them; null with a status line when there are too many or the file changed under the view.</summary>
    private byte[]? ReadForPlugin(HexView hex, ByteRange range, HexOffer offer)
    {
        if (range.Length > HexAnswers.MaxData)
        {
            SetStatusMessage(Loc.Get(K.HexPluginTooLarge, ("bytes", range.Length), ("limit", HexAnswers.MaxData)));
            return null;
        }
        var data = new byte[range.Length];
        try
        {
            int read = hex.ShownBytes(hex.Editor.Document.GetSnapshot()).Read(range.Start, data);
            return read == data.Length ? data : data[..read];
        }
        catch (IOException)
        {
            hex.FileChangedBehind();
            SetStatusMessage(Loc.Get(K.HexFindStale));
            return null;
        }
    }

    /// <summary>Hashes the selection, or the whole file without one, with the hash of a plugin and shows the value in the dialog of the built-in hashes.</summary>
    internal async Task HexPluginHashAsync(HexView hex, HexOffer offer)
    {
        var editor = hex.Editor;
        var range = editor.HasSelection ? editor.Selection : new ByteRange(0, editor.Document.ByteLength);
        string scope = editor.HasSelection
            ? Loc.Get(K.HexSumScopeSelection, ("bytes", range.Length), ("start", hex.OffsetText(range.Start)))
            : Loc.Get(K.HexSumScopeFile, ("bytes", range.Length));
        bool masked = hex.TouchesHidden(range.Start, range.End);
        if (ReadForPlugin(hex, range, offer) is not { } data) return;
        string value;
        try { value = await _app.HexPlugins.HashAsync(offer, data); }
        catch (HexPluginException e)
        {
            SetStatusMessage(Loc.Get(K.HexPluginFailed, ("reason", e.Message)));
            return;
        }
        await ShowChecksumResultAsync(offer.Title, Loc.Get(K.HexSumResult, ("name", offer.Title), ("scope", scope)), [value], masked ? Loc.Get(K.HexSumMasked) : "");
    }

    /// <summary>Opens the text a plugin makes of the selected bytes as a new unsaved document in the mode its name suggests.</summary>
    internal async Task HexPluginExportAsync(HexView hex, HexOffer offer)
    {
        var editor = hex.Editor;
        if (!editor.HasSelection)
        {
            SetStatusMessage(Loc.Get(K.HexNeedsSelection));
            return;
        }
        var range = editor.Selection;
        if (ReadForPlugin(hex, range, offer) is not { } data) return;
        (string Name, string Text) made;
        try { made = await _app.HexPlugins.ExportAsync(offer, data, range.Start); }
        catch (HexPluginException e)
        {
            SetStatusMessage(Loc.Get(K.HexPluginFailed, ("reason", e.Message)));
            return;
        }
        var tab = OpenGeneratedText(made.Text, Editing.LanguageModes.ForPath(made.Name).Id);
        tab.Doc.TitleOverride = () => made.Name;
        SetStatusMessage(Loc.Get(K.HexPluginExported, ("plugin", offer.PluginName), ("bytes", range.Length), ("name", made.Name)));
    }

    /// <summary>Puts the templates of a plugin into the list of the structure panel, until the run ends; one the subset of Kaitai Struct does not accept is named in the note of the panel.</summary>
    internal async Task HexPluginTemplatesAsync(HexView hex, HexOffer offer)
    {
        IReadOnlyList<HexTemplateText> templates;
        try { templates = await _app.HexPlugins.TemplatesAsync(offer); }
        catch (HexPluginException e)
        {
            SetStatusMessage(Loc.Get(K.HexPluginFailed, ("reason", e.Message)));
            return;
        }
        if (templates.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.HexPluginNoTemplates, ("plugin", offer.PluginName)));
            return;
        }
        int taken = templates.Count(t => hex.Structure.LoadText(t.Name, t.Ksy));
        SetStatusMessage(Loc.Get(K.HexPluginTemplates, ("plugin", offer.PluginName), ("templates", taken)));
    }
}
