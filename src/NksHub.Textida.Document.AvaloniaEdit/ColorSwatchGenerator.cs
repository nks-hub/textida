using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// Puts a small swatch of the colour in front of every colour value of the text (<see cref="ColorLiterals"/>); a click on it calls <paramref name="clicked"/>.
/// The swatch is a control hosted in the line and takes no character of the document, so offsets, selections and saving are the same with or without it.
/// </summary>
/// <param name="active">Whether swatches are drawn at all (the language of the document, hidden secrets).</param>
/// <param name="maxLineChars">A line longer than this is not searched for colours.</param>
/// <param name="tip">The tooltip and the screen reader name of a swatch.</param>
/// <param name="clicked">The colour, the document offset where it starts and the swatch.</param>
public sealed class ColorSwatchGenerator(Func<bool> active, int maxLineChars, Func<ColorMatch, string> tip, Action<ColorMatch, int, Control> clicked) : VisualLineElementGenerator
{
    private readonly List<(int Offset, ColorMatch Match)> _line = [];
    private bool _found;

    public override void StartGeneration(ITextRunConstructionContext context)
    {
        base.StartGeneration(context);
        _line.Clear();
        _found = false;
    }

    public override void FinishGeneration()
    {
        _line.Clear();
        base.FinishGeneration();
    }

    /// <summary>The last document line of the visual line is known only once generation runs, so the colours are looked for on the first question.</summary>
    private void Find()
    {
        if (_found) return;
        _found = true;
        if (!active()) return;
        var visual = CurrentContext.VisualLine;
        for (var line = visual.FirstDocumentLine; line is not null && line.LineNumber <= visual.LastDocumentLine.LineNumber; line = line.NextLine)
        {
            if (line.Length > maxLineChars) continue;
            foreach (var match in ColorLiterals.Find(CurrentContext.Document.GetText(line.Offset, line.Length))) _line.Add((line.Offset + match.Start, match));
        }
    }

    public override int GetFirstInterestedOffset(int startOffset)
    {
        Find();
        foreach (var (offset, _) in _line)
            if (offset >= startOffset) return offset;
        return -1;
    }

    public override VisualLineElement? ConstructElement(int offset)
    {
        Find();
        foreach (var (at, match) in _line)
            if (at == offset) return new InlineObjectElement(0, Swatch(match, offset));
        return null;
    }

    private Border Swatch(ColorMatch match, int offset)
    {
        var c = match.Color;
        var text = tip(match);
        var swatch = new Border
        {
            Width = 11,
            Height = 11,
            Margin = new Thickness(0, 0, 4, 0),
            CornerRadius = new CornerRadius(2),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(110, 128, 128, 128)),
            Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(c.A * 255), c.R, c.G, c.B)),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(swatch, text);
        AutomationProperties.SetName(swatch, text);
        swatch.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(swatch).Properties.IsLeftButtonPressed) return;
            clicked(match, offset, swatch);
            e.Handled = true;
        };
        return swatch;
    }
}
