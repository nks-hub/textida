using Avalonia.Automation;
using Avalonia.Input.Platform;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

internal sealed partial class MainWindow
{
    private readonly ToolWindowPanel _structureTool = new();
    private StructureView? _structureView;
    private Core.Documents.Document? _structureDocument;

    internal bool CanShowStructure => Active is { Custom: null, Hex: null } tab && OffersStructuredModes(tab.Doc);

    internal StructureView StructurePanelView => Active?.Structure ?? (_structureView ??= CreateStructureView());

    private StructureView CreateStructureView()
    {
        var view = new StructureView(StructureSourceOfActive,
            async text => { if (Clipboard is { } clipboard) { RememberCopy(text); await clipboard.SetTextAsync(text); } },
            (title, label, initial) => _app.Dialogs.PromptValueAsync(this, title, label, initial, StructureEdits.MaxInputChars));
        StructureNativeDiagnostic.Attach(this, view, StructureSourceOfActive);
        _structureTool.View = view;
        Closed += (_, _) =>
        {
            if (_structureDocument is { } document) document.Changed -= OnStructureDocumentChanged;
            _structureDocument = null;
            view.Dispose();
        };
        return view;
    }

    private StructureSource? StructureSourceOfActive()
    {
        if (!CanShowStructure || Active is not { } tab) return null;
        return StructureSourceOf(tab);
    }

    private StructureSource? StructureSourceOf(DocTab tab)
    {
        if (!OffersStructuredModes(tab.Doc) || tab.Doc.IsHex) return null;
        var document = tab.Doc.Doc;
        var format = tab.Doc.Language.Id switch
        {
            "xml" => StructureFormat.Xml,
            "jsonc" => StructureFormat.Jsonc,
            "yaml" => StructureFormat.Yaml,
            "neon" => StructureFormat.Neon,
            _ => JsonDataReader.IsJsoncFile(tab.Path) ? StructureFormat.Jsonc : StructureFormat.Json,
        };
        return new(tab.Doc, document.Id, document.Revision, document.Codec, document.BomLength, format,
            StructureOffset(tab), tab.Doc.ReadOnly, document.GetSnapshot,
            range => RevealStructureRange(tab, document, range),
            (edit, revision, ct) => ApplyStructureEditAsync(tab, document, edit, revision, ct),
            range => OpenStructureSource(tab, range));
    }

    private static long StructureOffset(DocTab tab)
    {
        if (tab.JsonShown) return tab.Json!.SourceOffset;
        if (tab.Paged is { HasSelection: true } paged) return paged.SelectionRange.Start;
        if (tab.Surface is { HasSelection: true } surface && tab.Doc.TextBackend is { } backend)
            return backend.ToByteOffset(surface.SelectionStart);
        return TextCaretOffset(tab);
    }

    private void RevealStructureRange(DocTab tab, Core.Documents.Document document, ByteRange range)
    {
        if (Active != tab || !ReferenceEquals(tab.Doc.Doc, document)) return;
        long startOffset = Math.Clamp(range.Start, document.BomLength, document.ByteLength);
        range = new(startOffset, Math.Clamp(range.End, startOffset, document.ByteLength));
        if (tab.JsonShown) tab.Json!.RevealOffset(range.Start, range.End);
        else if (tab.Paged is { } paged) paged.SelectBytes(range.Start, range.End);
        else if (tab.Surface is { } surface && tab.Doc.TextBackend is { } backend
            && backend.TryToCharOffset(range.Start, out int start))
        {
            if (!backend.TryToCharOffset(range.End, out int end)) end = start;
            surface.SetSelections([new Sel(start, end)]);
        }
        // Keep the keyboard in the tree so its next arrow key still works there.
    }

    private async Task<bool> ApplyStructureEditAsync(DocTab tab, Core.Documents.Document document, ByteEdit edit, long revision, CancellationToken ct)
    {
        if (Active != tab || !ReferenceEquals(tab.Doc.Doc, document) || document.Revision != revision || tab.Doc.ReadOnly) return false;
        var snapshot = document.GetSnapshot();
        using var prepared = await Task.Run(() => Core.Documents.Document.PrepareReplacement(snapshot, document.Id, revision, edit, ct: ct), ct);
        ct.ThrowIfCancellationRequested();
        if (Active != tab || !ReferenceEquals(tab.Doc.Doc, document) || document.Revision != revision || tab.Doc.ReadOnly) return false;
        return document.ApplyPreparedReplacement(prepared, new EditOptions { BaseRevision = revision, Source = "structure" }).Applied;
    }

    private void SyncStructure()
    {
        foreach (var tab in Tabs.Where(item => item.Structure is not null).ToArray())
        {
            if (!ReferenceEquals(tab.StructureDocument, tab.Doc.Doc))
            {
                if (tab.StructureDocument is { } old) old.Changed -= OnStructureDocumentChanged;
                tab.StructureDocument = tab.Doc.Doc;
                tab.StructureDocument.Changed += OnStructureDocumentChanged;
            }
            if (tab.StructureShown) tab.Structure!.Sync();
        }
        if (_structureView is not { } view) return;
        var document = IsOpen(SidePanel.Structure) ? Active?.Doc.Doc : null;
        if (!ReferenceEquals(document, _structureDocument))
        {
            if (_structureDocument is { } previous) previous.Changed -= OnStructureDocumentChanged;
            _structureDocument = document;
            if (document is not null) document.Changed += OnStructureDocumentChanged;
        }
        if (IsOpen(SidePanel.Structure)) view.Sync();
    }

    private void OnStructureDocumentChanged(object? sender, DocumentChangedEventArgs args) => SyncStructure();

    private void ApplyStructureTexts()
    {
        _structureTool.Title.Text = Loc.Get(K.StructureTitle);
        AutomationProperties.SetName(_structureTool, Loc.Get(K.StructureTitle));
        AutomationProperties.SetName(_structureTool.Close, Loc.Get(K.PanelToolHide, ("panel", Loc.Get(K.StructureTitle))));
        _structureView?.ApplyTexts();
        foreach (var tab in Tabs) tab.Structure?.ApplyTexts();
    }

    internal CommandOutcome ShowStructurePanel()
    {
        if (!CanShowStructure) return CommandOutcome.NotHandled;
        if (Active is { } tab && SetViewMode(tab.Doc, ViewMode.Tree)) tab.Structure?.FocusContent();
        return CommandOutcome.Handled;
    }
}
