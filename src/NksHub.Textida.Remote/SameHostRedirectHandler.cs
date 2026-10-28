using System.Net;

namespace NksHub.Textida.Remote;

/// <summary>
/// Follows a redirect only to another page of the host the request went to, and never from https to http. A redirect anywhere else is handed back as the answer, so the caller sees the 3xx and the
/// headers it sent (an API key) stay with the host the user agreed to. 301, 302 and 303 turn a request into a GET without a body; 307 and 308 repeat it as it was, which needs a body that can be sent again.
/// The inner handler must not follow redirects itself.
/// </summary>
/// <param name="maxRedirects">Most pages a request is sent on through; the next redirect is handed back.</param>
public sealed class SameHostRedirectHandler(HttpMessageHandler inner, int maxRedirects = 5) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var origin = request.RequestUri!;
        for (int hops = 0; ; hops++)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (hops >= maxRedirects || response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                || response.Headers.Location is not { } location)
                return response;
            var next = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
            bool weaker = request.RequestUri!.Scheme == Uri.UriSchemeHttps && next.Scheme != Uri.UriSchemeHttps;
            if (!string.Equals(next.Host, origin.Host, StringComparison.OrdinalIgnoreCase) || weaker || next.Scheme is not ("http" or "https")) return response;
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther && request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            {
                request.Method = HttpMethod.Get;
                request.Content = null;
            }
            response.Dispose();
            request.RequestUri = next;
        }
    }
}
