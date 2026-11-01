using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>Tools, Run Script on Selection (prototype, PZ.8.3): the <c>transform</c> function of a JavaScript file runs in the editor on the selection and the result is a change the user reviews. Off until the setting <c>scripting.enabled</c> is on.</summary>
internal sealed partial class MainWindow
{
    internal const string ScriptingSetting = "scripting.enabled";
    private const long ScriptMaxBytes = 256 * 1024;

    /// <summary>The time limit of a script; tests shorten it.</summary>
    internal TimeSpan ScriptTimeout { get; set; } = InEditorScript.DefaultTimeout;

    internal CommandOutcome RunEditorScript()
    {
        if (Active is not { Surface: not null }) return CommandOutcome.NotHandled;
        _ = RunEditorScriptAsync();
        return CommandOutcome.Handled;
    }

    internal async Task RunEditorScriptAsync()
    {
        if (!_app.Workbench.Settings.GetBool(ScriptingSetting))
        {
            SetStatusMessage(Loc.Get(K.ScriptingOff));
            return;
        }
        if (Active is not { Surface: { } surface } tab || tab.Doc.IsLarge) return;
        string? path = (await _app.Dialogs.PickFilesAsync(this)).FirstOrDefault();
        if (path is null) return;
        string script;
        try
        {
            if (new FileInfo(path).Length > ScriptMaxBytes) { SetStatusMessage(Loc.Get(K.ScriptingTooLarge, ("limit", ScriptMaxBytes >> 10))); return; }
            script = await File.ReadAllTextAsync(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.ScriptingFailed, ("reason", e.Message)));
            return;
        }
        string text = surface.Text;
        int start = surface.SelectionLength > 0 ? surface.SelectionStart : 0, length = surface.SelectionLength > 0 ? surface.SelectionLength : text.Length;
        string result;
        try { result = await Task.Run(() => InEditorScript.Transform(script, text.Substring(start, length), line => _app.Output.Append(OutputChannel.Tasks, "script", line), ScriptTimeout)); }
        catch (ScriptException e)
        {
            _app.Output.Append(OutputChannel.Tasks, "script", e.Message);
            SetStatusMessage(Loc.Get(K.ScriptingFailed, ("reason", e.Message)));
            return;
        }
        if (result == text.Substring(start, length))
        {
            SetStatusMessage(Loc.Get(K.ScriptingNoChange));
            return;
        }
        var doc = tab.Doc.Doc;
        byte[] bytes;
        try { bytes = doc.Codec.Encode(text[..start] + result + text[(start + length)..]); }
        catch (UnrepresentableCharactersException)
        {
            SetStatusMessage(Loc.Get(K.SpellDiacriticsEncoding, ("encoding", doc.Codec.Name)));
            return;
        }
        if (doc.Codec.Bom.Length > 0 && !bytes.AsSpan().StartsWith(doc.Codec.Bom)) bytes = [.. doc.Codec.Bom, .. bytes];
        if (ExternalReview.BuildRevert(doc, bytes) is not { } change) return;
        await ShowProposalAsync(Guid.NewGuid().ToString("N"), Loc.Get(K.ScriptingTitle, ("script", Path.GetFileName(path))), "script:" + Path.GetFileName(path), tab, change, CancellationToken.None);
    }
}
