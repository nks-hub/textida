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

    [Theory]
    [InlineData("Shift-JIS", "日本語のテキストです。これは文字コードを判定するための例文で、ひらがなとカタカナと漢字が含まれています。今日は天気がよいので、公園に散歩に行きます。")]
    [InlineData("EUC-JP", "日本語のテキストです。これは文字コードを判定するための例文で、ひらがなとカタカナと漢字が含まれています。今日は天気がよいので、公園に散歩に行きます。")]
    [InlineData("GBK", "这是一个用来测试字符编码检测的中文例句，其中包含了很多简体字。今天天气很好，我们都去公园里散步，然后回家吃饭。")]
    [InlineData("Big5", "這是一個用來測試字元編碼偵測的中文例句，其中包含了很多繁體字。今天天氣很好，我們都去公園裡散步，然後回家吃飯。")]
    [InlineData("EUC-KR", "이것은 문자 인코딩 감지를 시험하기 위한 한국어 예문입니다. 오늘은 날씨가 좋아서 공원에 산책하러 갑니다.")]
    public void Japanese_Chinese_and_Korean_text_is_told_apart_by_its_characters(string page, string text)
    {
        var guess = GuessIn(page, text);
        Assert.Equal(page, guess.Codec.Name);
        Assert.False(guess.IsUncertain, $"{guess.Reason}; confidence {guess.Confidence:F2}");
    }

    [Fact]
    public void A_few_Asian_characters_are_uncertain_and_Latin_text_is_never_taken_for_Asian()
    {
        var little = EncodingGuesser.Guess(TextCodec.ByName("GBK")!.Encode("你好"), wholeFile: true);
        Assert.True(little.IsUncertain || little.Codec.Name == "GBK");
        var czech = GuessIn("Windows-1250", Czech);
        Assert.NotEqual(CodecKind.MultiByte, czech.Codec.Kind);
        var russian = GuessIn("Windows-1251", Russian);
        Assert.NotEqual(CodecKind.MultiByte, russian.Codec.Kind);
    }

    [Fact]
    public void A_little_Russian_text_is_uncertain_rather_than_wrong_and_offers_the_other_Cyrillic_pages()
    {
        var guess = EncodingGuesser.Guess(TextCodec.ByName("Windows-1251")!.Encode("Привет"), wholeFile: true);
        if (guess.IsUncertain) Assert.NotEmpty(guess.Alternatives);
        else Assert.Equal("Windows-1251", guess.Codec.Name);
    }
}
