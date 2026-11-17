using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace NksHub.Textida.App.Shell;

internal sealed class StructureRowHeader : Control
{
    private const double MarkerWidth = 14;
    private const double MaximumTextWidth = 700;
    private string heading = "", value = "";
    private bool caret, measured;
    private Size naturalHeadingSize;
    private double layoutWidth = double.NaN, layoutScale = double.NaN;
    private double textWidth;
    private TextLayout? headingLayout;
    private string DisplayText => heading.Length > 0 && value.Length > 0 ? heading + ": " + value : heading + value;

    static StructureRowHeader()
    {
        ClipToBoundsProperty.OverrideDefaultValue<StructureRowHeader>(true);
        AffectsRender<StructureRowHeader>(TextElement.ForegroundProperty);
    }

    protected override bool BypassFlowDirectionPolicies => true;
    private double MarkerColumn => UseLayoutRounding
        ? LayoutHelper.RoundLayoutValueUp(MarkerWidth, LayoutHelper.GetLayoutScale(this)) : MarkerWidth;

    public void SetText(string title, string preview)
    {
        if (heading == title && value == preview) return;
        heading = title; value = preview;
        InvalidateText();
    }

    public void SetCaret(bool marked)
    {
        if (caret == marked) return;
        caret = marked;
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var title = MeasureText();
        return new(MarkerColumn + textWidth, Math.Max(24, title.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        MeasureText();
        EnsureLayout(Math.Max(1, textWidth));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double markerWidth = MarkerColumn;
        MeasureText();
        EnsureLayout(Math.Max(1, textWidth));
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        bool rightToLeft = FlowDirection == FlowDirection.RightToLeft;
        double textX = rightToLeft ? Bounds.Width - markerWidth - textWidth : markerWidth;
        double titleHeight = SizeOf(headingLayout!).Height;
        Draw(context, headingLayout!, new(textX, (Bounds.Height - titleHeight) / 2, textWidth, titleHeight));
        if (caret)
        {
            double markerX = rightToLeft ? Math.Max(0, Bounds.Width - markerWidth) : 0;
            context.DrawEllipse(TextElement.GetForeground(this), null, new Point(markerX + markerWidth / 2, Bounds.Height / 2), 2, 2);
        }
    }

    private static void Draw(DrawingContext context, TextLayout layout, Rect bounds)
    {
        using (context.PushClip(bounds)) layout.Draw(context, bounds.TopLeft);
    }

    private Size MeasureText()
    {
        if (!measured)
        {
            EnsureLayout(MaximumTextWidth);
            naturalHeadingSize = RawSizeOf(headingLayout!);
            measured = true;
        }
        var title = RoundSize(naturalHeadingSize);
        textWidth = title.Width;
        return title;
    }

    private static Size RawSizeOf(TextLayout layout)
        => new(Math.Min(MaximumTextWidth, layout.WidthIncludingTrailingWhitespace), layout.Height);

    private Size RoundSize(Size size)
        => UseLayoutRounding ? LayoutHelper.RoundLayoutSizeUp(size, LayoutHelper.GetLayoutScale(this)) : size;

    private Size SizeOf(TextLayout layout) => RoundSize(RawSizeOf(layout));

    private void EnsureLayout(double width)
    {
        double scale = LayoutHelper.GetLayoutScale(this);
        if (headingLayout is not null && scale == layoutScale
            && (width == layoutWidth || CanUseNaturalLayout(width))) return;
        DisposeLayouts();
        layoutWidth = width; layoutScale = scale;
        headingLayout = CreateLayout(DisplayText, TextElement.GetFontSize(this), width, TextTrimming.CharacterEllipsis);
    }

    private bool CanUseNaturalLayout(double width)
        => layoutWidth == MaximumTextWidth && width <= MaximumTextWidth
            && FlowDirection == FlowDirection.LeftToRight
            && TextBlock.GetTextAlignment(this) is TextAlignment.Start or TextAlignment.Left
            && FitsNaturalLine(headingLayout!, DisplayText, width);

    private static bool FitsNaturalLine(TextLayout layout, string text, double width)
    {
        if (layout.TextLines.Count != 1) return false;
        var line = layout.TextLines[0];
        return !line.HasCollapsed && !line.HasOverflowed && line.Start == 0
            && line.Length == text.Length && line.WidthIncludingTrailingWhitespace <= width;
    }

    private TextLayout CreateLayout(string text, double size, double width, TextTrimming trimming)
    {
        long started = Core.TypingTrace.Begin();
        try
        {
            return new(text,
                new Typeface(TextElement.GetFontFamily(this), TextElement.GetFontStyle(this),
                    TextElement.GetFontWeight(this), TextElement.GetFontStretch(this)),
                size, TextElement.GetForeground(this), TextBlock.GetTextAlignment(this), TextWrapping.NoWrap,
                trimming, GetValue(TextBlock.TextDecorationsProperty), FlowDirection,
                maxWidth: width, lineHeight: TextBlock.GetLineHeight(this),
                letterSpacing: GetValue(TextElement.LetterSpacingProperty), maxLines: GetValue(TextBlock.MaxLinesProperty),
                fontFeatures: GetValue(TextElement.FontFeaturesProperty));
        }
        finally { Core.TypingTrace.End("structure.header.layout", started); }
    }

    private void DisposeLayouts()
    {
        headingLayout?.Dispose(); headingLayout = null;
        layoutWidth = layoutScale = double.NaN;
    }

    private void InvalidateText()
    {
        measured = false;
        DisposeLayouts();
        InvalidateMeasure(); InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        measured = false;
        DisposeLayouts();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        var property = change.Property;
        if (property == TextElement.FontFamilyProperty || property == TextElement.FontSizeProperty
            || property == TextElement.FontStyleProperty || property == TextElement.FontWeightProperty
            || property == TextElement.FontStretchProperty || property == TextElement.ForegroundProperty
            || property == TextElement.FontFeaturesProperty || property == TextElement.LetterSpacingProperty
            || property == TextBlock.TextAlignmentProperty || property == TextBlock.LineHeightProperty
            || property == TextBlock.MaxLinesProperty || property == TextBlock.TextDecorationsProperty
            || property == FlowDirectionProperty || property == UseLayoutRoundingProperty)
            InvalidateText();
    }
}
