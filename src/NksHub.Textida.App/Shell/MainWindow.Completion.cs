using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Completion;
using NksHub.Textida.Core.Secrets;
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
            string text = other.Document.Text;
            if (other.IsSecretFile && !(current && other.SecretsRevealed))
            {
                var spans = SecretValues.Find(text);
                if (current && SecretValues.SpanAt(spans, caret) is not null) return null;
                text = SecretValues.Mask(text, spans);
                sources.Add(new CompletionSource(text));
            }
            else sources.Add(new CompletionSource(text, current ? caret : -1));
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

    internal CommandOutcome ShowCompletion()
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        var offer = GatherCompletion();
        var rows = new List<QuickRow>();
        if (offer is not null)
        {
            ReportLibraryProblems();
            rows.AddRange(offer.Words.Select(word => new QuickRow(word, Loc.Get(K.CompleteWord), null, null, true, null, word, () => InsertWord(surface, word))));
            rows.AddRange(offer.Clips.Select(e => new QuickRow(
                e.Clip.Title, e.Clip.Language ?? e.PluginName, FirstLines(e.Clip.Text, 1), null, true, null, $"{e.Clip.Title}, {e.PluginName}",
                () => _ = InsertClipAsync(e, replaceWord: true), () => PreviewOf(e.Clip.Text, e.Clip.Fields))));
        }
        ShowChoice(Loc.Get(K.CompleteWatermark), Loc.Get(K.A11yComplete), rows, emptyText: Loc.Get(K.CompleteEmpty));
        return CommandOutcome.Handled;
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
