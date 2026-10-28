using System.Net;
using System.Text;
using NksHub.Textida.Remote;

namespace NksHub.Textida.Remote.Tests;

[Trait("Category", "Network")]
public class SameHostRedirectTests
{
    /// <summary>The pages the handler asked for, as method, address and body at the moment of the call, and the answers it was given in turn.</summary>
    private sealed class Pages(params Func<HttpResponseMessage>[] answers) : HttpMessageHandler
    {
        public List<(string Method, string Uri, string Body)> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add((request.Method.Method, request.RequestUri!.AbsoluteUri, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return answers[Math.Min(Asked.Count, answers.Length) - 1]();
        }
    }

    private static Func<HttpResponseMessage> Redirect(HttpStatusCode status, string location) => () => new HttpResponseMessage(status) { Headers = { Location = new Uri(location, UriKind.RelativeOrAbsolute) } };

    private static Func<HttpResponseMessage> Ok => () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("done") };

    private static async Task<HttpResponseMessage> PostAsync(Pages pages, string address, int maxRedirects = 5)
    {
        using var client = new HttpClient(new SameHostRedirectHandler(pages, maxRedirects), disposeHandler: false);
        return await client.PostAsync(address, new StringContent("payload", Encoding.UTF8, "application/json"));
    }

    [Fact]
    public async Task A_307_to_another_page_of_the_host_repeats_the_request_with_its_body()
    {
        var pages = new Pages(Redirect(HttpStatusCode.TemporaryRedirect, "/mcp/"), Ok);

        var answer = await PostAsync(pages, "https://Server.example.test/mcp");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal([("POST", "https://server.example.test/mcp", "payload"), ("POST", "https://server.example.test/mcp/", "payload")], pages.Asked);
    }

    [Fact]
    public async Task A_302_turns_a_post_into_a_get_without_a_body()
    {
        var pages = new Pages(Redirect(HttpStatusCode.Found, "https://server.example.test/other"), Ok);

        await PostAsync(pages, "https://server.example.test/mcp");

        Assert.Equal([("POST", "https://server.example.test/mcp", "payload"), ("GET", "https://server.example.test/other", "")], pages.Asked);
    }

    [Theory]
    [InlineData("https://tracker.example.test/collect")] // another host
    [InlineData("https://server.example.test.tracker.example.test/")] // a host that only begins like it
    [InlineData("http://server.example.test/plain")] // the same host, but without encryption
    public async Task A_redirect_anywhere_else_is_handed_back_and_nothing_is_sent_there(string location)
    {
        var pages = new Pages(Redirect(HttpStatusCode.TemporaryRedirect, location), Ok);

        var answer = await PostAsync(pages, "https://server.example.test/mcp");

        Assert.Equal(HttpStatusCode.TemporaryRedirect, answer.StatusCode);
        Assert.Equal(new Uri(location), answer.Headers.Location);
        Assert.Single(pages.Asked);
    }

    [Fact]
    public async Task A_plain_address_may_be_sent_on_to_another_page_and_to_the_secure_one_of_the_same_host()
    {
        var pages = new Pages(Redirect(HttpStatusCode.PermanentRedirect, "https://server.example.test/mcp"), Ok);

        var answer = await PostAsync(pages, "http://server.example.test/mcp");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(2, pages.Asked.Count);
    }

    [Fact]
    public async Task Only_the_given_number_of_redirects_is_followed_and_the_next_one_is_the_answer()
    {
        var pages = new Pages(Redirect(HttpStatusCode.TemporaryRedirect, "/again"));

        var answer = await PostAsync(pages, "https://server.example.test/mcp", maxRedirects: 2);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, answer.StatusCode);
        Assert.Equal(3, pages.Asked.Count);
    }
}
