using Avalonia.Input.Platform;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    internal static bool OffersStructuredModes(OpenDocument document) => !document.IsResults && !document.IsVirtual
        && document.Language.Id is "json" or "jsonc" or "xml" or "yaml" or "neon";

    internal static bool OffersDelimitedModes(OpenDocument document) => !document.IsResults && !document.IsVirtual
        && (document.Language.Id is "csv" or "tsv"
            || Path.GetExtension(document.Path) is { } extension
            && (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) || extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase)));

    private static bool IsDataMode(ViewMode mode) => mode is ViewMode.Tree or ViewMode.Table or ViewMode.Graph or ViewMode.StructureSplit;

    private StructureView CreateDocumentStructure(DocTab tab)
    {
        var view = new StructureView(() => StructureSourceOf(tab),
            async text => { if (Clipboard is { } clipboard) { RememberCopy(text); await clipboard.SetTextAsync(text); } },
            (title, label, initial) => _app.Dialogs.PromptValueAsync(this, title, label, initial, Core.Data.Structure.StructureEdits.MaxInputChars));
        StructureNativeDiagnostic.Attach(this, view, () => StructureSourceOf(tab));
        tab.StructureDocument = tab.Doc.Doc;
        tab.StructureDocument.Changed += OnStructureDocumentChanged;
        tab.ViewClosed += () =>
        {
            if (tab.StructureDocument is { } document) document.Changed -= OnStructureDocumentChanged;
            tab.StructureDocument = null;
        };
        return view;
    }

    private void ApplyDataMode(DocTab tab, ViewMode mode)
    {
        if (tab.JsonShown) tab.ShowJson(false, OptionsFor(tab));
        if (!OffersStructuredModes(tab.Doc) && OffersDelimitedModes(tab.Doc))
        {
            if (tab.StructureShown && tab.Structure is { } structure)
                tab.ShowStructure(structure, false, false, OptionsFor(tab));
            var previous = tab.Table;
            tab.ShowTable(mode == ViewMode.Table, OptionsFor(tab));
            if (tab.Table is { } table && !ReferenceEquals(previous, table)) WireTable(tab, table);
            return;
        }
        if (!OffersStructuredModes(tab.Doc)) return;
        if (tab.TableShown) tab.ShowTable(false, OptionsFor(tab));
        var view = tab.Structure;
        if (IsDataMode(mode))
        {
            view ??= CreateDocumentStructure(tab);
            view.Presentation = mode switch
            {
                ViewMode.Graph => StructureViewPresentation.Graph,
                ViewMode.Table => StructureViewPresentation.Table,
                _ => StructureViewPresentation.Tree,
            };
            tab.ShowStructure(view, true, mode == ViewMode.StructureSplit, OptionsFor(tab));
        }
        else if (view is not null) tab.ShowStructure(view, false, false, OptionsFor(tab));
    }

    private bool SetDataMode(OpenDocument document, ViewMode mode, long? offset)
    {
        if (!OffersStructuredModes(document) && !OffersDelimitedModes(document)) return false;
        if (document.Mode is not ViewMode.Text && !IsDataMode(document.Mode)
            || mode is not ViewMode.Text && !IsDataMode(mode)) return false;
        if (document.Doc.IsSaving) { SetStatusMessage(Loc.Get(K.HexBusy)); return false; }
        document.ChooseMode(mode);
        foreach (var tab in document.Views.ToArray())
        {
            ApplyDataMode(tab, mode);
            if (offset is { } at) ApplyOffsets(tab, at, at);
            RefreshModeBar(tab);
        }
        if (IsOpen(SidePanel.Structure)) TogglePanel(SidePanel.Structure, false);
        if (document.Path is { } path) _rememberedModes[path] = mode;
        document.NotifyChanged();
        if (Active is { } active && active.Doc == document) FocusView(active);
        UpdateStatus(); RefreshMenuIfContextChanged();
        return true;
    }

    private void OpenStructureSource(DocTab tab, ByteRange range)
    {
        if (Active != tab) return;
        SetViewMode(tab.Doc, ViewMode.Text, range.Start);
        RevealStructureRange(tab, tab.Doc.Doc, range);
        FocusView(tab);
    }
}
