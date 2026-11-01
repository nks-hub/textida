using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Tests;

public class PreviewLinkTests
{
    [AvaloniaFact]
    public void A_link_in_a_sentence_has_the_size_and_font_of_the_words_around_it_and_sits_on_their_baseline()
    {
        var control = PreviewRenderer.Render(PreviewFormats.Markdown, "Some words and a [link](https://example.invalid) in a paragraph.",
            new PreviewRenderer.Ctx(new FontFamily("Consolas"), Open: _ => { }));
        var window = new Window { Width = 800, Height = 300, Content = control };
        window.Show();
        window.UpdateLayout();

        var button = control.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("previewlink"));
        var sentence = button.GetVisualAncestors().OfType<TextBlock>().First();
        Assert.Equal(sentence.FontSize, button.FontSize);
        Assert.Equal(sentence.FontFamily, button.FontFamily);
        var container = Assert.Single(sentence.Inlines!.OfType<InlineUIContainer>());
        Assert.Equal(BaselineAlignment.Center, container.BaselineAlignment);
        var inner = button.GetVisualDescendants().OfType<TextBlock>().First();
        window.UpdateLayout();
        double linkBaseline = inner.TranslatePoint(default, sentence)!.Value.Y + inner.TextLayout.TextLines[0].Baseline;
        Assert.Equal(sentence.TextLayout.TextLines[0].Baseline, linkBaseline, 0.5); // the text of the link is on the baseline of the words around it
        window.Close();
    }
}
