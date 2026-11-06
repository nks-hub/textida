using Avalonia.Headless.XUnit;
using Avalonia.Media;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

public class PreviewLinkTests
{
    [AvaloniaFact]
    public void A_link_in_a_sentence_has_the_size_and_font_of_the_words_around_it_and_sits_on_their_line()
    {
        using var host = new ReaderHost("Some words and a [link](https://example.invalid) in a paragraph.", 800, 300);
        var layout = host.Reader.LayoutOf(0)!;
        var line = Assert.Single(layout.TextLines); // the link flows in the line of the sentence instead of standing in a box of its own
        var runs = line.TextRuns.Where(r => r.Properties is not null && r.Length > 0).ToList();
        var link = Assert.Single(runs, r => r.Text.ToString() == "link");
        var words = runs.First(r => r.Text.ToString().StartsWith("Some", StringComparison.Ordinal));
        Assert.Equal(words.Properties!.FontRenderingEmSize, link.Properties!.FontRenderingEmSize);
        Assert.Equal(words.Properties.Typeface.FontFamily, link.Properties.Typeface.FontFamily);
        Assert.NotEqual(((ISolidColorBrush)words.Properties.ForegroundBrush!).Color, ((ISolidColorBrush)link.Properties.ForegroundBrush!).Color);
    }
}
