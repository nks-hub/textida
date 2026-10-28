using System.Text;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Agents.Transcripts;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Tools, Agent, Open Agent Transcript: the transcript of a Claude Code session (a <c>.jsonl</c> file of <c>~/.claude/projects</c>) or of a Codex session (<c>~/.codex/sessions</c>) in <see cref="TranscriptWindow"/>, read only. The file is read
/// through its own handle and never written. The values of the secrets files of the open tabs and of the opened folder are hidden in it from the start, and those the transcript itself shows are
/// hidden once seen.
/// </summary>
internal sealed partial class MainWindow
{
    private readonly List<TranscriptWindow> _transcripts = [];

    /// <summary>The open transcript windows of this window; for tests.</summary>
    internal IReadOnlyList<TranscriptWindow> Transcripts => _transcripts;

    internal CommandOutcome ShowTranscript()
    {
        if (Active is { Path: { } path } tab && path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) && !tab.Doc.Doc.IsModified)
        {
            OpenTranscript(path);
            return CommandOutcome.Handled;
        }
        _ = PickTranscriptAsync();
        return CommandOutcome.Handled;
    }

    private async Task PickTranscriptAsync()
    {
        var files = await _app.Dialogs.PickFilesAsync(this);
        if (files.Count > 0) OpenTranscript(files[0]);
    }

    /// <summary>Opens the transcript in a window and starts its index; null (with a message) when the file cannot be read.</summary>
    internal TranscriptWindow? OpenTranscript(string path)
    {
        TranscriptIndex index;
        try
        {
            var masker = new TranscriptMasker(PluginLimitsPlaceholders.TranscriptMinSecretLength);
            LearnWorkspaceSecrets(masker);
            index = TranscriptIndex.Open(path, PluginLimitsPlaceholders.Transcript(), masker);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetStatusMessage(Loc.Get(K.TranscriptOpenFailed, ("path", path), ("reason", e.Message)));
            return null;
        }
        var window = new TranscriptWindow(new TranscriptHost(index, path, () => _tokenRule, OpenTouchedFile, (line, text) => OpenTranscriptLine(path, line, text)));
        _transcripts.Add(window);
        window.Closed += (_, _) => _transcripts.Remove(window);
        window.StartIndexing();
        window.Show(this);
        return window;
    }

    /// <summary>A file the agent touched, opened in the editor; false when it is not there (it may have been moved or deleted since).</summary>
    private bool OpenTouchedFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            OpenFile(path, null);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>One raw line of the transcript as a document that cannot be saved; its values of secrets files are hidden already.</summary>
    private void OpenTranscriptLine(string path, long line, string text)
    {
        var document = Document.CreateVirtual($"transcript-line-{line}.json", Encoding.UTF8.GetBytes(text), AvaloniaEditBackendFactory.Instance, TextCodec.Utf8);
        FocusView(OpenVirtualDocument(document, Loc.Get(K.TranscriptRawTab, ("line", line), ("name", System.IO.Path.GetFileName(path)))));
    }

    /// <summary>Teaches the masker the values of the secrets files the user has at hand: the open tabs and the top of the opened folder.</summary>
    private void LearnWorkspaceSecrets(TranscriptMasker masker)
    {
        int learned = 0;
        foreach (var tab in Tabs.DistinctBy(t => t.Doc))
        {
            if (learned >= PluginLimitsPlaceholders.TranscriptSeedMaxFiles) return;
            if (tab.Surface is null || !SecretValues.IsSecretFile(tab.Doc.Doc.SecretsPath)) continue;
            masker.LearnFile(tab.Surface.Document.Text);
            learned++;
        }
        if (FileRootFolder is not { } root) return;
        try
        {
            foreach (var file in Directory.EnumerateFiles(root))
            {
                if (learned >= PluginLimitsPlaceholders.TranscriptSeedMaxFiles) return;
                if (!SecretValues.IsSecretFile(file) || new FileInfo(file).Length > PluginLimitsPlaceholders.TranscriptSeedMaxFileBytes) continue;
                masker.LearnFile(File.ReadAllText(file));
                learned++;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a folder that cannot be read teaches nothing */ }
    }
}
