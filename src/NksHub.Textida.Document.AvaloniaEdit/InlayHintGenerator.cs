using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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
            // the hint must not make its line taller: it takes the font of the editor, a little smaller, and sits in a box lower than the ascent of the text, so the line keeps the height of its text
            var view = CurrentContext.TextView;
            double lineHeight = view.DefaultLineHeight;
            var text = new TextBlock
            {
                Text = label,
                FontStyle = FontStyle.Italic,
                FontFamily = view.GetValue(TextElement.FontFamilyProperty),
                FontSize = view.GetValue(TextElement.FontSizeProperty) * 0.85,
                Opacity = 0.65,
                IsHitTestVisible = false,
            };
            // an inline object hangs below the baseline of its line, so a box lower than the descent of the text keeps the line as it is; the text is drawn upwards from the bottom of the box on a canvas, which does not clip it
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double boxHeight = lineHeight * 0.2;
            Canvas.SetTop(text, boxHeight - text.DesiredSize.Height + text.FontSize * 0.45); // the baseline of the hint on the baseline of the line
            var box = new Border { Width = text.DesiredSize.Width + 4, Height = boxHeight, Child = new Canvas { Children = { text } }, IsHitTestVisible = false };
            AutomationProperties.SetName(text, label);
            return new InlineObjectElement(0, box);
        }
        return null;
    }
}
