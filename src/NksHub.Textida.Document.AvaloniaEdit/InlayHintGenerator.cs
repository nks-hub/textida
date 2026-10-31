using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// Shows inlay hints (a parameter name, an inferred type) between two characters of the text. A hint is a control hosted in the line that takes no character of the document, so offsets,
/// selections, copying and saving are the same with or without it. The hints are set by offset (UTF-16 units of the document) and sorted by the caller.
/// </summary>
public sealed class InlayHintGenerator : VisualLineElementGenerator
{
    private (int Offset, string Label)[] _hints = [];

    /// <summary>The hints shown now, by offset.</summary>
    public IReadOnlyList<(int Offset, string Label)> Hints => _hints;

    /// <summary>Replaces the hints; the caller redraws the view.</summary>
    public void SetHints(IEnumerable<(int Offset, string Label)> hints) => _hints = [.. hints.OrderBy(h => h.Offset)];

    public override int GetFirstInterestedOffset(int startOffset)
    {
        int low = 0, high = _hints.Length;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (_hints[middle].Offset < startOffset) low = middle + 1;
            else high = middle;
        }
        if (low >= _hints.Length) return -1;
        // a hint behind the visual line is not this generation's business
        return _hints[low].Offset <= CurrentContext.VisualLine.LastDocumentLine.EndOffset ? _hints[low].Offset : -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        foreach (var (at, label) in _hints)
        {
            if (at != offset) continue;
            var text = new TextBlock
            {
                Text = label,
                FontStyle = FontStyle.Italic,
                Opacity = 0.65,
                Margin = new Thickness(2, 0),
                IsHitTestVisible = false,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            AutomationProperties.SetName(text, label);
            return new InlineObjectElement(0, text);
        }
        return null;
    }
}
