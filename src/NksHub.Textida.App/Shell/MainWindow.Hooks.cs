using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Agents.Hooks;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, Agent, Edit Agent Hooks: the hooks of the open settings file of Claude Code or <c>hooks.json</c> of Codex in a form (<see cref="HooksEditorWindow"/>). The editor never starts a hook.
/// What it changes in the file goes through the change review of the tab, as a proposal the user applies, and is saved by the normal save of the tab.
/// </summary>
internal sealed partial class MainWindow
{
    private HooksEditorWindow? _hooksEditor;

    internal HooksEditorWindow? HooksEditor => _hooksEditor;

    internal CommandOutcome ShowHooksEditor()
    {
        if (_hooksEditor is { } open)
        {
            open.Activate();
            return CommandOutcome.Handled;
        }
        if (Active is not { Hex: null, Custom: null } tab || tab.Doc.IsResults) return CommandOutcome.NotHandled;
        if (HookFiles.IsToml(tab.Path))
        {
            SetStatusMessage(Loc.Get(K.HooksNoteToml));
            return CommandOutcome.Handled;
        }
        if (tab.Surface is not { } surface || tab.Doc.Doc.ByteLength > PluginLimitsPlaceholders.HooksMaxFileBytes)
        {
            SetStatusMessage(Loc.Get(K.HooksNoteTooLarge, ("size", OpenDocument.FormatSize(PluginLimitsPlaceholders.HooksMaxFileBytes))));
            return CommandOutcome.Handled;
        }
        var host = new HooksHost(tab.Path, tab.Title, HookFiles.ProductOf(tab.Path), PluginLimitsPlaceholders.Hooks(), () => surface.Document.Text, () => tab.Doc.Doc.Revision, text => ProposeHooksAsync(tab, text));
        var window = _hooksEditor = new HooksEditorWindow(host);
        window.Closed += (_, _) => _hooksEditor = null;
        window.Show(this);
        return CommandOutcome.Handled;
    }

    /// <summary>
    /// Shows the new text of the file in the change review, change by change, and returns true when the user applied some of it. The review is against the text the tab holds now, so nothing
    /// the user typed meanwhile is overwritten.
    /// </summary>
    private async Task<bool> ProposeHooksAsync(DocTab tab, string text)
    {
        var doc = tab.Doc.Doc;
        byte[] bytes;
        var codec = doc.Codec;
        try { bytes = codec.Encode(text); }
        catch (UnrepresentableCharactersException)
        {
            SetStatusMessage(Loc.Get(K.HooksNoteEncoding, ("encoding", codec.Name)));
            return false;
        }
        if (codec.Bom.Length > 0 && !bytes.AsSpan().StartsWith(codec.Bom)) bytes = [.. codec.Bom, .. bytes];
        var change = ExternalReview.BuildRevert(doc, bytes);
        if (change is null) return false;
        var decision = await ShowProposalAsync(Guid.NewGuid().ToString("N"), Loc.Get(K.HooksProposal, ("file", tab.Title)), "hooks", tab, change, CancellationToken.None);
        return decision.Outcome is ProposalOutcome.Applied or ProposalOutcome.Partial;
    }
}
