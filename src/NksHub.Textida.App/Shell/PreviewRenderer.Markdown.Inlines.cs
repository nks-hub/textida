using Markdig.Extensions.Footnotes;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax.Inlines;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

internal static partial class PreviewRenderer
{
    private static void MarkdownInlines(ContainerInline? container, InlineCollection into, FontWeight weight, FontStyle style, MarkdownPlan plan, Ctx ctx)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    into.Add(new Run(literal.Content.ToString()) { FontWeight = weight, FontStyle = style });
                    break;
                case HtmlEntityInline entity:
                    into.Add(new Run(entity.Transcoded.ToString()) { FontWeight = weight, FontStyle = style });
                    break;
                case HtmlInline html:
                    into.Add(new Run(html.Tag) { FontWeight = weight, FontStyle = style });
                    break;
                case CodeInline code:
                    var mono = new Run(code.Content) { FontFamily = ctx.Mono, FontWeight = weight, FontStyle = style };
                    mono.Classes.Add("previewinlinecode");
                    into.Add(mono);
                    break;
                case LineBreakInline { IsHard: true }:
                    into.Add(new LineBreak());
                    break;
                case LineBreakInline:
                    into.Add(new Run(" ") { FontWeight = weight, FontStyle = style });
                    break;
                case EmphasisInline { DelimiterChar: '~' } struck:
                    var strike = new Span { TextDecorations = TextDecorations.Strikethrough };
                    strike.Classes.Add("previewstrike");
                    MarkdownInlines(struck, strike.Inlines, weight, style, plan, ctx);
                    into.Add(strike);
                    break;
                case EmphasisInline emphasis:
                    MarkdownInlines(emphasis, into, emphasis.DelimiterCount >= 2 ? FontWeight.Bold : weight,
                        emphasis.DelimiterCount == 1 ? FontStyle.Italic : style, plan, ctx);
                    break;
                case LinkInline { IsImage: true } image:
                    AddImage(into, image.Url ?? "", MarkdownSyntax.Text(image), ctx, weight, style);
                    break;
                case LinkInline link:
                    MarkdownLink(link, link.Url ?? "", MarkdownSyntax.Text(link), into, weight, style, plan, ctx);
                    break;
                case AutolinkInline link:
                    MarkdownLink(null, link.IsEmail ? "mailto:" + link.Url : link.Url, link.Url, into, weight, style, plan, ctx);
                    break;
                case FootnoteLink note:
                    FootnoteReference(note, into, weight, style, ctx);
                    break;
                case TaskList task:
                    into.Add(new InlineUIContainer(new CheckBox { IsChecked = task.Checked, IsEnabled = false, Focusable = false }) { BaselineAlignment = BaselineAlignment.Center });
                    break;
                case ContainerInline nested:
                    MarkdownInlines(nested, into, weight, style, plan, ctx);
                    break;
                default:
                    if (inline.Span.Start >= 0 && inline.Span.End < plan.Source.Length)
                        into.Add(new Run(plan.Source.Substring(inline.Span.Start, inline.Span.Length)) { FontWeight = weight, FontStyle = style });
                    break;
            }
        }
    }

    private static void MarkdownLink(ContainerInline? label, string target, string plain, InlineCollection into, FontWeight weight, FontStyle style, MarkdownPlan plan, Ctx ctx)
    {
        Action? open = null;
        if (target.StartsWith('#') && ctx.JumpToLine is { } jump)
        {
            string fragment;
            try { fragment = Uri.UnescapeDataString(target[1..]); }
            catch (UriFormatException) { fragment = target[1..]; }
            if (plan.Targets.TryGetValue(fragment, out int line)) open = () => jump(line);
        }
        else if (ctx.Open is { } external && PreviewPolicy.LinkTarget(target) is { } uri) open = () => external(uri);
        if (open is null)
        {
            if (label is not null) MarkdownInlines(label, into, weight, style, plan, ctx);
            else into.Add(new Run(plain) { FontWeight = weight, FontStyle = style });
            if (plain != target) into.Add(new Run(" (" + target + ")") { FontWeight = weight, FontStyle = style });
            return;
        }
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (label is not null) MarkdownInlines(label, text.Inlines!, weight, style, plan, ctx);
        else text.Inlines!.Add(new Run(plain) { FontWeight = weight, FontStyle = style });
        var button = LinkButton(text, Loc.Get(K.PreviewPanelLink, ("text", plain), ("url", target)), open);
        ToolTip.SetTip(button, target);
        into.Add(new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Center });
    }

    /// <summary>
    /// A footnote mark in the text or the back link after the note. Either jumps within the preview: the mark to the note, the back link to the first mark that cites it
    /// (a note cited several times has one back link per mark, all to the first). Without a jump handler the mark stays plain text.
    /// </summary>
    private static void FootnoteReference(FootnoteLink link, InlineCollection into, FontWeight weight, FontStyle style, Ctx ctx)
    {
        string label = link.IsBackLink ? "\u21A9" : "[" + link.Index + "]";
        int? line = link.IsBackLink ? link.Footnote.Links.FirstOrDefault(l => !l.IsBackLink)?.Line : link.Footnote.Line;
        if (ctx.JumpToLine is not { } jump || line is not { } target)
        {
            into.Add(new Run(label) { FontWeight = weight, FontStyle = style });
            return;
        }
        string name = link.IsBackLink ? Loc.Get(K.PreviewFootnoteBack) : Loc.Get(K.PreviewFootnoteName, ("number", link.Index));
        var button = LinkButton(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, name, () => jump(target));
        button.Classes.Add("previewfootnote");
        ToolTip.SetTip(button, name);
        into.Add(new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Center });
    }
}
