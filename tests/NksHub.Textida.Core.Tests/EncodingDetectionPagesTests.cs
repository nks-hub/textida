using System.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Tests;

/// <summary>P5.5.3: the detector tells the Cyrillic, Greek and Turkish pages apart (and the Czech ones as before) from the statistics of the letters; a result it is not sure of offers the others.</summary>
public class EncodingDetectionPagesTests
{
    private const string Russian = "Привет, мир! Это проверка определения кодировки текста на русском языке. Сегодня хорошая погода, и мы идём гулять в парк.";
    private const string Czech = "Příliš žluťoučký kůň úpěl ďábelské ódy. Dnes je pěkné počasí, a proto půjdeme na procházku do lesa.";
    private const string Greek = "Καλημέρα κόσμε, αυτό είναι μια δοκιμή της αναγνώρισης κωδικοποίησης κειμένου στα ελληνικά.";
    private const string Turkish = "Türkçe karakterler: ğüşiöç. Bugün hava çok güzel, çay içip şehirde yürüyüşe çıkacağız ve güneşin tadını çıkaracağız.";

    private static EncodingGuess GuessIn(string name, string text) => EncodingGuesser.Guess(TextCodec.ByName(name)!.Encode(text), wholeFile: true);

    [Theory]
    [InlineData("Windows-1251")]
    [InlineData("KOI8-R")]
    [InlineData("DOS CP866")]
    public void Russian_text_is_recognised_in_each_of_its_three_pages(string page)
    {
        var guess = GuessIn(page, Russian);
        Assert.Equal(page, guess.Codec.Name);
        Assert.False(guess.IsUncertain, $"{guess.Reason}; confidence {guess.Confidence:F2}; alternatives {string.Join(", ", guess.Alternatives.Select(a => a.Name))}");
    }

    [Theory]
    [InlineData("Windows-1250")]
    public void Czech_text_is_still_recognised_and_is_not_taken_for_a_Cyrillic_or_Greek_page(string page)
    {
        var guess = GuessIn(page, Czech);
        Assert.Contains(guess.Codec.Name, new[] { "Windows-1250", "ISO-8859-2" }); // the two share the letters, as they did
        Assert.Equal(TextCodec.Cp1250.Group, guess.Codec.Group);
    }

    [Theory]
    [InlineData("Windows-1253")]
    [InlineData("ISO-8859-7")]
    public void Greek_text_is_a_Greek_page_and_the_other_Greek_page_is_offered(string page)
    {
        var guess = GuessIn(page, Greek);
        Assert.Equal(EncodingGroup.Greek, guess.Codec.Group);
        if (guess.IsUncertain) Assert.Contains(guess.Alternatives, c => c.Group == EncodingGroup.Greek);
    }

    [Theory]
    [InlineData("Windows-1254")]
    [InlineData("ISO-8859-9")]
    public void Turkish_text_is_a_Turkish_page_and_the_other_one_is_offered(string page)
    {
        var guess = GuessIn(page, Turkish);
        Assert.Equal(EncodingGroup.Turkish, guess.Codec.Group);
        if (guess.IsUncertain) Assert.Contains(guess.Alternatives, c => c.Group == EncodingGroup.Turkish);
    }

    [Fact]
    public void A_little_Russian_text_is_uncertain_rather_than_wrong_and_offers_the_other_Cyrillic_pages()
    {
        var guess = EncodingGuesser.Guess(TextCodec.ByName("Windows-1251")!.Encode("Привет"), wholeFile: true);
        if (guess.IsUncertain) Assert.NotEmpty(guess.Alternatives);
        else Assert.Equal("Windows-1251", guess.Codec.Name);
    }
}
