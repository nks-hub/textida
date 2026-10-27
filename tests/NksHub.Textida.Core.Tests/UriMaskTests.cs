using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Core.Tests;

/// <summary>P6.2.1: the secrets of a URI never reach a title, the recent files or a log.</summary>
public class UriMaskTests
{
    [Theory]
    [InlineData("wiki://host/page?token=abc123&x=1", "wiki://host/page?token=***&x=1")]
    [InlineData("wiki://user:hunter2@host/page", "wiki://user:***@host/page")]
    [InlineData("wiki://sk-secret@host/page", "wiki://***@host/page")]
    [InlineData("wiki://host/page?API_KEY=zz&Session=q", "wiki://host/page?API_KEY=***&Session=***")]
    [InlineData("wiki://host/page#access_token=abc&state=s", "wiki://host/page#access_token=***&state=s")]
    [InlineData("wiki://host/a/b.md", "wiki://host/a/b.md")]
    public void MasksTheSecretPartsAndNothingElse(string uri, string masked) => Assert.Equal(masked, UriMask.Mask(uri));

    [Fact]
    public void MasksEveryUriInALineOfText()
    {
        var line = UriMask.MaskText("read wiki://host/p?token=abc failed; retry (wiki://u:pw@h/x)");
        Assert.DoesNotContain("abc", line);
        Assert.DoesNotContain("pw", line);
        Assert.Contains("wiki://host/p?token=***", line);
        Assert.Equal("no uri here: C:\\x\\y", UriMask.MaskText("no uri here: C:\\x\\y"));
    }

    [Fact]
    public void RecognisesUrisAndNotWindowsPaths()
    {
        Assert.True(UriMask.IsUri("wiki://host/p"));
        Assert.False(UriMask.IsUri(@"C:\work\a.txt"));
        Assert.False(UriMask.IsUri("/home/a.txt"));
        Assert.Equal("wiki", UriMask.SchemeOf("WIKI://host/p"));
    }

    [Fact]
    public void TheTabNameIsTheLastSegmentOfTheMaskedUri()
    {
        Assert.Equal("page one", UriMask.DisplayName("wiki://host/docs/page%20one?token=abc"));
        Assert.Equal("host", UriMask.DisplayName("wiki://tok@host"));
    }
}
