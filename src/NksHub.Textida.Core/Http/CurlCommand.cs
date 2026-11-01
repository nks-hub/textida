using System.Text;

namespace NksHub.Textida.Core.Http;

/// <summary>Converts a request to a <c>curl</c> command and a <c>curl</c> command to a request. Only what a request is made of is carried: method, URL, headers, body and the user of <c>-u</c> (as a Basic header).</summary>
public static class CurlCommand
{
    /// <summary>The command for <paramref name="request"/>, one option per line with a backslash at the end of the line, arguments in single quotes.</summary>
    public static string From(HttpRequest request)
    {
        var sb = new StringBuilder("curl");
        if (!request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)) sb.Append(" -X ").Append(request.Method);
        sb.Append(' ').Append(Quote(request.Url));
        foreach (var (name, value) in request.Headers) sb.Append(" \\\n  -H ").Append(Quote(name + ": " + value));
        if (request.Body.Length > 0) sb.Append(" \\\n  --data-raw ").Append(Quote(request.Body));
        return sb.ToString();
    }

    private static string Quote(string s) => "'" + s.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// The request of a <c>curl</c> command; null when the text is not one or has no URL. Arguments may be in single or double quotes or bare, a backslash and a line break continue the command;
    /// the options that do not change what is sent (<c>-L -k -s -S -i -v --compressed</c> ...) are skipped. Without <c>-X</c> the method is POST when there is a body and GET otherwise.
    /// </summary>
    public static HttpRequest? Parse(string text)
    {
        var words = Split(text);
        if (words.Count == 0 || !words[0].Equals("curl", StringComparison.Ordinal)) return null;
        string? method = null, url = null, user = null;
        var headers = new List<(string, string)>();
        var body = new List<string>();
        for (int i = 1; i < words.Count; i++)
        {
            string w = words[i];
            string? Next() => i + 1 < words.Count ? words[++i] : null;
            switch (w)
            {
                case "-X" or "--request": method = Next(); break;
                case "-H" or "--header":
                    if (Next() is { } header && header.IndexOf(':') is var colon and > 0) headers.Add((header[..colon].Trim(), header[(colon + 1)..].Trim()));
                    break;
                case "-d" or "--data" or "--data-raw" or "--data-binary" or "--data-ascii" or "--data-urlencode": if (Next() is { } data) body.Add(data); break;
                case "-u" or "--user": user = Next(); break;
                case "--url": url = Next(); break;
                case "-A" or "--user-agent": if (Next() is { } agent) headers.Add(("User-Agent", agent)); break;
                case "-e" or "--referer": if (Next() is { } referer) headers.Add(("Referer", referer)); break;
                case "-b" or "--cookie": if (Next() is { } cookie) headers.Add(("Cookie", cookie)); break;
                case "-o" or "--output" or "-m" or "--max-time" or "--connect-timeout" or "-x" or "--proxy" or "--cacert" or "--cert" or "--key" or "-w" or "--write-out": Next(); break;
                default:
                    if (w.StartsWith('-')) break; // a flag without a value that does not change the request
                    url ??= w;
                    break;
            }
        }
        if (url is null) return null;
        if (user is not null) headers.Add(("Authorization", "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(user))));
        string joined = string.Join("&", body);
        return new HttpRequest(null, (method ?? (body.Count > 0 ? "POST" : "GET")).ToUpperInvariant(), url, headers, joined, 0, 0, 0);
    }

    private static List<string> Split(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length && text[i + 1] is '\n' or '\r')
            {
                i += text[i + 1] == '\r' && i + 2 < text.Length && text[i + 2] == '\n' ? 2 : 1;
                continue;
            }
            if (c == '\'')
            {
                inWord = true;
                int end = text.IndexOf('\'', i + 1);
                if (end < 0) end = text.Length;
                current.Append(text, i + 1, end - i - 1);
                i = end;
            }
            else if (c == '"')
            {
                inWord = true;
                for (i++; i < text.Length && text[i] != '"'; i++)
                {
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\' or '$' or '`') i++;
                    current.Append(text[i]);
                }
            }
            else if (char.IsWhiteSpace(c))
            {
                if (inWord) { words.Add(current.ToString()); current.Clear(); inWord = false; }
            }
            else
            {
                inWord = true;
                if (c == '\\' && i + 1 < text.Length) c = text[++i];
                current.Append(c);
            }
        }
        if (inWord) words.Add(current.ToString());
        return words;
    }
}
