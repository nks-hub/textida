using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Colors;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Colours in the text: a swatch in front of every colour value of the languages of the web, and a picker that opens on a click on the swatch (or on the command
/// at the caret). The picked colour replaces the value in the notation it was written in, as one undo step; the document is not touched while the picker is open.
/// </summary>
internal sealed partial class EditorSurface
{
    /// <summary>The language modes whose text holds CSS colours: style sheets and the markup, scripts and templates that embed them.</summary>
    private static readonly HashSet<string> ColorLanguages = ["css", "html", "xml", "javascript", "typescript", "php", "razor", "latte"];

    private ColorSwatchGenerator? _swatches;

    /// <summary>Colour values show a swatch in this document.</summary>
    public bool ColorsShown => ColorLanguages.Contains(Doc.Language.Id) && !SecretsHidden;

    private void InitColors()
    {
        _swatches = new ColorSwatchGenerator(() => ColorsShown, WrapLineLimit,
            match => Loc.Get(K.ColorSwatchTip, ("value", ColorLiterals.FormatLike(match, match.Color))), OpenColorPicker);
        TextArea.TextView.ElementGenerators.Add(_swatches);
        Doc.LanguageChanged += OnColorLanguageChanged;
    }

    private void OnColorLanguageChanged() => TextArea.TextView.Redraw();

    /// <summary>Opens the picker on the colour value at the caret, in any language. False when the caret is not on one.</summary>
    public bool PickColorAtCaret()
    {
        var line = Document.GetLineByOffset(CaretOffset);
        int column = CaretOffset - line.Offset;
        foreach (var match in ColorLiterals.Find(Document.GetText(line.Offset, line.Length)))
        {
            if (column < match.Start || column > match.End) continue;
            OpenColorPicker(match, line.Offset + match.Start, this);
            return true;
        }
        return false;
    }

    private void OpenColorPicker(ColorMatch match, int offset, Control anchor)
    {
        string original = Document.GetText(offset, match.Length);
        var panel = new ColorPickerPanel(match);
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft };
        bool cancelled = false;
        panel.CancelRequested += () =>
        {
            cancelled = true;
            flyout.Hide();
        };
        flyout.Closed += (_, _) =>
        {
            if (!cancelled) ReplaceColor(offset, original, match, panel.Color);
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Writes <paramref name="color"/> over the colour value that stood at <paramref name="offset"/>, in the notation of <paramref name="match"/>, as one undo step.
    /// False when the text there is not <paramref name="original"/> any more (it was edited while the picker was open), the document is read-only or the value would not change.
    /// </summary>
    internal bool ReplaceColor(int offset, string original, ColorMatch match, Rgba color)
    {
        if (IsReadOnly || offset < 0 || offset + original.Length > Document.TextLength || Document.GetText(offset, original.Length) != original) return false;
        string text = ColorLiterals.FormatLike(match, color);
        if (text == original) return false;
        using (Doc.Doc.BeginUndoGroup()) Document.Replace(offset, original.Length, text);
        return true;
    }
}
