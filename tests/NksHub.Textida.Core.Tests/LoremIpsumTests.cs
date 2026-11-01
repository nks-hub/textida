using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Tests;

public class LoremIpsumTests
{
    [Fact]
    public void The_text_begins_with_the_classic_opening_and_is_the_same_every_time()
    {
        string words = LoremIpsum.Generate(LoremUnit.Words, 5);
        Assert.Equal("lorem ipsum dolor sit amet", words);
        Assert.StartsWith("Lorem ipsum dolor sit amet", LoremIpsum.Generate(LoremUnit.Sentences, 1));
        Assert.Equal(LoremIpsum.Generate(LoremUnit.Paragraphs, 3), LoremIpsum.Generate(LoremUnit.Paragraphs, 3));
    }

    [Theory]
    [InlineData(LoremUnit.Words, 1)]
    [InlineData(LoremUnit.Words, 120)]
    [InlineData(LoremUnit.Sentences, 7)]
    [InlineData(LoremUnit.Paragraphs, 4)]
    public void The_text_has_as_many_units_as_asked_for(LoremUnit unit, int count)
    {
        string text = LoremIpsum.Generate(unit, count, "\r\n");
        switch (unit)
        {
            case LoremUnit.Words: Assert.Equal(count, text.Split(' ').Length); break;
            case LoremUnit.Sentences: Assert.Equal(count, text.Count(c => c == '.')); Assert.EndsWith(".", text); break;
            default:
                Assert.Equal(count, text.Split("\r\n\r\n").Length);
                Assert.All(text.Split("\r\n\r\n"), p => Assert.InRange(p.Count(c => c == '.'), 4, 6)); // four to six sentences
                Assert.DoesNotContain("\n\n", text.Replace("\r\n\r\n", "")); // only the given line break is used
                break;
        }
    }

    [Fact]
    public void A_count_outside_the_range_is_brought_into_it()
    {
        Assert.Equal(1, LoremIpsum.Generate(LoremUnit.Words, 0).Split(' ').Length);
        Assert.Equal(1, LoremIpsum.Generate(LoremUnit.Words, -5).Split(' ').Length);
        Assert.Equal(LoremIpsum.MaxCount, LoremIpsum.Generate(LoremUnit.Words, int.MaxValue).Split(' ').Length);
    }
}
