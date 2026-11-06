using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Renders the text of a preview (P6.2.4, P9.3.1) into Avalonia controls under <see cref="PreviewPolicy"/> (P6.2.7): no web view, so there is no script and no network of its own.
/// HTML is a small subset (see <see cref="HtmlSubset"/>) and every other tag stays text; raw HTML inside Markdown stays text. A picture comes from a <c>data:</c> address, from the bytes a plugin attached,
/// from a file next to the document, or from the web after the user allowed the host; any other becomes a note with its alt text. A link to the web or a mail address is a button that asks the user
/// before it opens. A heading fragment scrolls within the preview; other targets remain text. A Mermaid flowchart is drawn with shapes (see <see cref="FlowchartLayout"/>), any other diagram is shown as its source.
/// Markdown covers headings, paragraphs, lists, pipe tables, quotes, fenced code, rules, emphasis, strong text and inline code.
/// </summary>
internal static partial class PreviewRenderer
{
    /// <summary>A source line mapped to the top or bottom of a rendered block, including nested blocks.</summary>
    internal readonly record struct Anchor(int Line, Control Control, bool End = false);

    /// <summary>
    /// What a render needs: the font of code and, for allowed links, what a click does (null: links stay text); the pictures (null: none are loaded), the folder of the document that
    /// relative pictures are read from (null: no local picture), the pictures a plugin attached, and where to note the source anchors (null: not needed).
    /// </summary>
    internal sealed record Ctx(FontFamily Mono, Action<Uri>? Open = null, PreviewImages? Images = null, string? BaseDirectory = null,
        IReadOnlyDictionary<string, byte[]>? Attached = null, List<Anchor>? Anchors = null, Action<int>? JumpToLine = null);

    public static Control Render(string format, string text, Ctx ctx)
    {
        var body = new StackPanel { Spacing = 8, Margin = new Thickness(12, 6) };
        switch (format)
        {
            case PreviewFormats.Markdown:
                return new MarkdownReader(MarkdownReaderModel.Build(text), ctx);
            case PreviewFormats.Html:
                HtmlBlocks(HtmlSubset.Parse(text.ReplaceLineEndings("\n"), PluginLimits.PreviewHtmlMaxNodes, PluginLimits.PreviewHtmlMaxDepth), body.Children, ctx, top: true);
                break;
            default:
                body.Children.Add(new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
                break;
        }
        return body;
    }

    private static void Put(Avalonia.Controls.Controls into, Control control, int line, Ctx ctx, bool top)
    {
        into.Add(control);
        if (top) ctx.Anchors?.Add(new Anchor(line, control));
    }


    /// <summary>One cell of a table: a border around its text, in the row and column, styled as a header cell when <paramref name="head"/>.</summary>
    private static Border Cell(SelectableTextBlock text, bool head, int row, int column)
    {
        var cell = new Border { Child = text };
        cell.Classes.Add("previewcell");
        if (head) cell.Classes.Add("previewcellhead");
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        return cell;
    }

    private static Border CodeBlock(string code, Ctx ctx)
    {
        var block = new Border { Child = new SelectableTextBlock { Text = code, FontFamily = ctx.Mono, TextWrapping = TextWrapping.Wrap } };
        block.Classes.Add("previewcode");
        return block;
    }

    private static Border HorizontalRule()
    {
        var rule = new Border();
        rule.Classes.Add("previewrule");
        return rule;
    }

    /// <summary>The picture a reference names, or a note with the alt text when the policy does not take it or it does not decode.</summary>
    private static void AddImage(InlineCollection into, string target, string alt, Ctx ctx, FontWeight weight, FontStyle style)
    {
        if (ImageControl(target, alt, ctx) is { } picture) into.Add(new InlineUIContainer(picture));
        else into.Add(new Run(Loc.Get(K.PreviewPanelImage, ("alt", alt))) { FontWeight = weight, FontStyle = style });
    }

    internal static Control? ImageControl(string target, string alt, Ctx ctx)
    {
        if (PreviewPolicy.ImageBytes(target) is { } data) return Picture(data, alt);
        if (PreviewPolicy.AttachedName(target) is { } name) return ctx.Attached is not null && ctx.Attached.TryGetValue(name, out var attached) ? Picture(attached, alt) : null;
        if (ctx.Images is not { } images) return null;
        if (PreviewPolicy.LocalImagePath(ctx.BaseDirectory, target) is { } path) return images.Local(path, alt, ctx.BaseDirectory);
        return PreviewPolicy.RemoteImage(target) is { } uri ? images.Remote(uri, alt) : null;
    }

    private static Control? Picture(byte[] bytes, string alt) => PreviewImages.Decode(bytes) is { } bitmap ? PreviewImages.Picture(bitmap, alt) : null;

    /// <summary>A link the policy allows becomes a button; any other is text followed by its target.</summary>
    private static void AddLink(InlineCollection into, string label, string target, Ctx ctx, FontWeight weight, FontStyle style)
    {
        var text = Loc.Get(K.PreviewPanelLink, ("text", label), ("url", target));
        if (ctx.Open is { } open && PreviewPolicy.LinkTarget(target) is { } uri) into.Add(new InlineUIContainer(LinkButton(text, uri, open)) { BaselineAlignment = BaselineAlignment.Center });
        else into.Add(new Run(text) { FontWeight = weight, FontStyle = style });
    }

    /// <summary>A link the policy allows: a button, so the keyboard and a screen reader reach it. It only hands the target to <paramref name="open"/>, which asks the user first.</summary>
    private static Button LinkButton(string text, Uri uri, Action<Uri> open)
        => LinkButton(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, text, () => open(uri));

    private static Button LinkButton(TextBlock content, string name, Action open)
    {
        var button = new Button { Content = content };
        button.Classes.Add("previewlink");
        // a button does not inherit the font of the sentence it stands in (the theme gives it its own), which left the link smaller and higher than the words around it
        button.AttachedToVisualTree += (_, _) =>
        {
            if (button.GetVisualAncestors().OfType<TextBlock>().FirstOrDefault() is { } sentence)
            {
                button.Bind(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, sentence.GetObservable(TextBlock.FontSizeProperty));
                button.Bind(Avalonia.Controls.Primitives.TemplatedControl.FontFamilyProperty, sentence.GetObservable(TextBlock.FontFamilyProperty));
                // the object of an inline sits by its middle on the line, so its text lands a little off the baseline of the words around it: move it by the difference (a drawing offset, the line does not change)
                var shift = new TranslateTransform();
                ((TextBlock)button.Content!).RenderTransform = shift; // on the text, not on the button, whose theme sets a transform of its own
                sentence.LayoutUpdated += (_, _) => AlignOnBaseline(button, sentence, shift);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AlignOnBaseline(button, sentence, shift), Avalonia.Threading.DispatcherPriority.Loaded);
            }
        };
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => open();
        return button;
    }

    /// <summary>Puts the text of an inline button on the baseline of the line of <paramref name="sentence"/> it stands in.</summary>
    internal static void AlignOnBaseline(Button button, TextBlock sentence, TranslateTransform shift)
    {
        if (button.Content is not TextBlock inner || inner.TextLayout.TextLines.Count == 0 || sentence.TextLayout.TextLines.Count == 0) return;
        if (inner.TranslatePoint(default, sentence) is not { } at) return;
        double mine = at.Y - shift.Y + inner.TextLayout.TextLines[0].Baseline, top = 0, baseline = sentence.TextLayout.TextLines[^1].Baseline;
        foreach (var line in sentence.TextLayout.TextLines) // the line the button is on, by its text's position
        {
            if (mine < top + line.Height) { baseline = top + line.Baseline; break; }
            top += line.Height;
        }
        double wanted = baseline - mine;
        if (Math.Abs(wanted - shift.Y) > 0.25) shift.Y = wanted;
    }

}
