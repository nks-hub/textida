using Avalonia.Controls;
using Avalonia.VisualTree;
using Markdig.Syntax;

namespace NksHub.Textida.App.Shell;

internal static partial class PreviewRenderer
{
    internal sealed record TextSelection(SourceSpan Source, int Start, int End);

    private static SourceSpan? SourceOf(Control control)
    {
        if (control.Tag is SourceSpan own) return own;
        foreach (var parent in control.GetVisualAncestors().OfType<Control>())
            if (parent.Tag is SourceSpan source) return source;
        return null;
    }

    internal static TextSelection? KeepSelection(Control? body, string? before, string after)
    {
        if (body is null || before is null) return null;
        var text = body.GetVisualDescendants().OfType<SelectableTextBlock>().FirstOrDefault(t => t.SelectionStart != t.SelectionEnd);
        if (text is null || SourceOf(text) is not { } source) return null;
        int prefix = 0, suffix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        if (source.End < prefix) return new(source, text.SelectionStart, text.SelectionEnd);
        if (source.Start < before.Length - suffix) return null;
        int delta = after.Length - before.Length;
        return new(new SourceSpan(source.Start + delta, source.End + delta), text.SelectionStart, text.SelectionEnd);
    }

    internal static void RestoreSelection(Control body, TextSelection? selection)
    {
        if (selection is null) return;
        foreach (var text in body.GetVisualDescendants().OfType<SelectableTextBlock>())
            if (SourceOf(text) is { } span && span.Start == selection.Source.Start && span.End == selection.Source.End)
            {
                text.SelectionStart = selection.Start;
                text.SelectionEnd = selection.End;
                return;
            }
    }
}
