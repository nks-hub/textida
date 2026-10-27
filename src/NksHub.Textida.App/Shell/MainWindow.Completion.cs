using System.Runtime.CompilerServices;
using AvaloniaEdit.Document;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Completion;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.App.Shell;

/// <summary>What Ctrl+J offers: the words that continue the word in front of the caret and the clips of the language of the document.</summary>
internal sealed record CompletionCandidates(string Prefix, IReadOnlyList<string> Words, IReadOnlyList<ClipEntry> Clips);

/// <summary>
/// Completion of words and clips (Ctrl+J). The words come from the text of all open tabs; a secrets file (<c>.env</c>) gives only what is not a hidden value, unless it is
/// the file being edited and its values are shown. The clips are those of the library whose language is the document's or none. The choice replaces the word in front
/// of every caret (a selection is replaced as it is), as one undo step.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The offer for the active editor, or null when there is none or the caret stands in a hidden value.</summary>
    internal CompletionCandidates? GatherCompletion()
    {
        if (ActiveEditor is not { } surface || Active is not { } tab) return null;
        var document = surface.Document;
        int caret = Math.Clamp(surface.CaretOffset, 0, document.TextLength);
        string prefix = "";
        if (surface.GetSelections().All(s => s.IsEmpty))
        {
            var line = document.GetLineByOffset(caret);
            string before = document.GetText(line.Offset, caret - line.Offset);
            prefix = before[WordCompletion.PrefixStart(before, before.Length)..];
        }

        var sources = new List<CompletionSource>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var candidate in new[] { tab }.Concat(_app.Windows.SelectMany(w => w.Tabs)))
        {
            if (candidate.Surface is not { } other || !seen.Add(candidate.Doc)) continue;
            bool current = ReferenceEquals(candidate, tab);
            if (other.IsSecretFile && !(current && other.SecretsRevealed))
            {
                string text = other.Document.Text;
                var spans = SecretValues.Find(text);
                if (current && SecretValues.SpanAt(spans, caret) is not null) return null;
                text = SecretValues.Mask(text, spans);
                sources.Add(new CompletionSource(text));
            }
            else if (current)
            {
                var line = other.Document.GetLineByOffset(caret); // the word at the caret is all that is read of the text
                sources.Add(new CompletionSource(other.Document.GetText(line.Offset, line.Length), caret - line.Offset, WordIndexOf(other)));
            }
            else sources.Add(new CompletionSource("", -1, WordIndexOf(other)));
        }

        string language = tab.Doc.Language.Id;
        string folded = TextMatching.Fold(prefix);
        var clips = _app.Clips.Clips
            .Where(e => e.Clip.Language is null || string.Equals(e.Clip.Language, language, StringComparison.OrdinalIgnoreCase))
            .Where(e => folded.Length == 0 || TextMatching.Fold(e.Clip.Title).Contains(folded, StringComparison.Ordinal))
            .OrderBy(e => e.Clip.Language is null)
            .ToList();
        return new CompletionCandidates(prefix, WordCompletion.Words(sources, prefix, PluginLimitsPlaceholders.CompletionMaxWords), clips);
    }

    private static readonly ConditionalWeakTable<EditorSurface, WordIndexTracker> WordIndexes = new();

    /// <summary>The words of the text as it is now; built once, then kept up to date by the edits.</summary>
    private static WordIndex WordIndexOf(EditorSurface surface)
    {
        if (!WordIndexes.TryGetValue(surface, out var tracker) || !ReferenceEquals(tracker.Document, surface.Document))
        {
            tracker?.Detach();
            tracker = new WordIndexTracker(surface.Document);
            WordIndexes.AddOrUpdate(surface, tracker);
        }
        return tracker.Index;
    }

    /// <summary>
    /// A <see cref="WordIndex"/> of a document that follows its edits: the lines an edit touches are taken out as they were and put in as they are. An edit in a very long line,
    /// or one that is not seen whole, drops the index and the next offer builds it again.
    /// </summary>
    private sealed class WordIndexTracker
    {
        private const int LongestLine = 1 << 16;
        private WordIndex? _index;
        private string? _before;

        public WordIndexTracker(TextDocument document)
        {
            Document = document;
            document.Changing += OnChanging;
            document.Changed += OnChanged;
        }

        public TextDocument Document { get; }

        public WordIndex Index => _index ??= WordIndex.Build(Document.Text);

        public void Detach()
        {
            Document.Changing -= OnChanging;
            Document.Changed -= OnChanged;
        }

        private void OnChanging(object? sender, DocumentChangeEventArgs e)
        {
            if (_index is null) return;
            _before = _before is null ? LinesOf(e.Offset, e.RemovalLength) : null; // a change inside a change is not followed
            if (_before is null) _index = null;
        }

        private void OnChanged(object? sender, DocumentChangeEventArgs e)
        {
            string? before = _before;
            _before = null;
            if (_index is null) return;
            string? after = before is null ? null : LinesOf(e.Offset, e.InsertionLength);
            if (after is null) _index = null;
            else _index.Change(before!, after);
        }

        /// <summary>The whole lines that a stretch of the text touches, or null when they are too long to follow.</summary>
        private string? LinesOf(int offset, int length)
        {
            var first = Document.GetLineByOffset(offset);
            var last = Document.GetLineByOffset(offset + length);
            int size = last.Offset + last.Length - first.Offset;
            return size > LongestLine ? null : Document.GetText(first.Offset, size);
        }
    }

    internal CommandOutcome ShowCompletion()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        if (Active is { } tab && _app.Plugins.AsksPluginsForCompletion(tab)) _ = ShowCompletionWithPluginsAsync(surface, tab);
        else ShowCompletionRows(surface, []);
        return CommandOutcome.Handled;
    }

    /// <summary>
    /// Asks the plugins for their offers first and lists them in the same popup as the words and clips. What a plugin answered for another revision than the one the document
    /// has now is not listed, and neither is anything when the window or the document changed while the plugins answered.
    /// </summary>
    private async Task ShowCompletionWithPluginsAsync(EditorSurface surface, DocTab tab)
    {
        var document = tab.Doc.Doc;
        var answer = await _app.Plugins.RequestCompletionAsync(tab);
        if (!ReferenceEquals(ActiveEditor, surface) || !ReferenceEquals(tab.Doc.Doc, document)) return;
        ShowCompletionRows(surface, answer is { Stale: false } && answer.Revision == document.Revision ? answer.Items : []);
    }

    private void ShowCompletionRows(EditorSurface surface, IReadOnlyList<CompletionItem> pluginItems)
    {
        var offer = GatherCompletion();
        var rows = new List<QuickRow>();
        rows.AddRange(pluginItems.Select(item => new QuickRow(
            item.Label, item.Detail ?? item.Kind ?? item.PluginId, FirstLines(item.Text, 1), null, true, null, item.Label, () => ApplyPluginCompletion(surface, item))));
        if (offer is not null)
        {
            ReportLibraryProblems();
            rows.AddRange(offer.Words.Select(word => new QuickRow(word, Loc.Get(K.CompleteWord), null, null, true, null, word, () => InsertWord(surface, WordCompletion.MatchCase(word, offer.Prefix)))));
            rows.AddRange(offer.Clips.Select(e => new QuickRow(
                e.Clip.Title, e.Clip.Language ?? e.PluginName, FirstLines(e.Clip.Text, 1), null, true, null, $"{e.Clip.Title}, {e.PluginName}",
                () => _ = InsertClipAsync(e, replaceWord: true), () => PreviewOf(e.Clip.Text, e.Clip.Fields))));
        }
        ShowChoice(Loc.Get(K.CompleteWatermark), Loc.Get(K.A11yComplete), rows, emptyText: Loc.Get(K.CompleteEmpty));
    }

    /// <summary>Writes the offer of a plugin: the text and its additional edits elsewhere in the file as one undo step. Refused when the document moved on since the plugin answered.</summary>
    private void ApplyPluginCompletion(EditorSurface surface, CompletionItem item)
    {
        if (!ReferenceEquals(ActiveEditor, surface) || surface.IsReadOnly) return;
        _app.Plugins.ApplyCompletion(Active!, item);
    }

    /// <summary>The selections with every empty one stretched back over the word in front of its caret (not into the selection before it).</summary>
    private static IReadOnlyList<Sel> WidenToWord(EditorSurface surface, IReadOnlyList<Sel> selections)
    {
        var document = surface.Document;
        var widened = new List<Sel>(selections.Count);
        int previousEnd = 0;
        foreach (var s in selections.OrderBy(s => s.Start))
        {
            Sel result = s;
            if (s.IsEmpty)
            {
                var line = document.GetLineByOffset(s.Caret);
                string before = document.GetText(line.Offset, s.Caret - line.Offset);
                int start = Math.Max(line.Offset + WordCompletion.PrefixStart(before, before.Length), previousEnd);
                result = new Sel(start, s.Caret);
            }
            widened.Add(result);
            previousEnd = result.End;
        }
        return widened;
    }

    private void InsertWord(EditorSurface surface, string word)
    {
        if (!ReferenceEquals(ActiveEditor, surface) || surface.IsReadOnly) return;
        RunEdit((s, selections) => LineOperations.ReplaceSelectionsWithCarets(s.Document, WidenToWord(s, selections), _ => (word, [])));
    }
}
