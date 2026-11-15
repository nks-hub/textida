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
    private bool caret;
    private double layoutWidth = double.NaN;
    private double textWidth;
    private TextLayout? headingLayout, valueLayout, markerLayout;

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
        markerLayout?.Dispose(); markerLayout = null;
        InvalidateMeasure(); InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout(MaximumTextWidth);
        var title = SizeOf(headingLayout!);
        var preview = valueLayout is null ? default : SizeOf(valueLayout);
        if (caret) markerLayout ??= CreateLayout("●", TextElement.GetFontSize(this), MarkerColumn, TextTrimming.None);
        double markerHeight = markerLayout is null ? 0 : SizeOf(markerLayout).Height;
        textWidth = Math.Max(title.Width, preview.Width);
        return new(MarkerColumn + textWidth, Math.Max(markerHeight, title.Height + preview.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        EnsureLayout(Math.Max(1, textWidth));
        return finalSize;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double markerWidth = MarkerColumn;
        EnsureLayout(Math.Max(1, textWidth));
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        bool rightToLeft = FlowDirection == FlowDirection.RightToLeft;
        double textX = rightToLeft ? Bounds.Width - markerWidth - textWidth : markerWidth;
        double titleHeight = SizeOf(headingLayout!).Height;
        Draw(context, headingLayout!, new(textX, 0, textWidth, titleHeight));
        if (valueLayout is not null)
            Draw(context, valueLayout, new(textX, titleHeight, textWidth, SizeOf(valueLayout).Height));
        if (caret)
        {
            markerLayout ??= CreateLayout("●", TextElement.GetFontSize(this), markerWidth, TextTrimming.None);
            double markerX = rightToLeft ? Math.Max(0, Bounds.Width - markerWidth) : 0;
            Draw(context, markerLayout, new(markerX, (Bounds.Height - SizeOf(markerLayout).Height) / 2,
                markerWidth, SizeOf(markerLayout).Height));
        }
    }

    private static void Draw(DrawingContext context, TextLayout layout, Rect bounds)
    {
        using (context.PushClip(bounds)) layout.Draw(context, bounds.TopLeft);
    }

    private Size SizeOf(TextLayout layout)
    {
        var size = new Size(Math.Min(MaximumTextWidth, layout.WidthIncludingTrailingWhitespace), layout.Height);
        return UseLayoutRounding ? LayoutHelper.RoundLayoutSizeUp(size, LayoutHelper.GetLayoutScale(this)) : size;
    }

    private void EnsureLayout(double width)
    {
        if (headingLayout is not null && width == layoutWidth) return;
        DisposeLayouts();
        layoutWidth = width;
        headingLayout = CreateLayout(heading, TextElement.GetFontSize(this), width, TextTrimming.CharacterEllipsis);
        if (value.Length > 0) valueLayout = CreateLayout(value, 11, width, TextTrimming.CharacterEllipsis);
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
        valueLayout?.Dispose(); valueLayout = null;
        markerLayout?.Dispose(); markerLayout = null;
        layoutWidth = double.NaN;
    }

    private void InvalidateText()
    {
        DisposeLayouts();
        InvalidateMeasure(); InvalidateVisual();
    }

    protected override void OnMeasureInvalidated()
    {
        DisposeLayouts();
        base.OnMeasureInvalidated();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
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
