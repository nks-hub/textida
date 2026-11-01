using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Http;

/// <summary>One request of a <c>.http</c> or <c>.rest</c> file, as written (variables not yet replaced).</summary>
/// <param name="Name">The name from a <c>@name</c> comment, or null.</param>
/// <param name="FirstLine">The zero-based line of the request line, <paramref name="LastLine"/> the last line of its block (the one before the next <c>###</c>).</param>
public sealed record HttpRequest(string? Name, string Method, string Url, IReadOnlyList<(string Name, string Value)> Headers, string Body, int FirstLine, int LastLine, int BlockFirstLine);

/// <summary>
/// The requests of a <c>.http</c> file: blocks separated by lines that start with <c>###</c>; in a block the request line (<c>GET https://host/path HTTP/1.1</c>, the method may be left out for GET), the headers up to a blank line and the body
/// after it. A line that starts with <c>#</c> or <c>//</c> before the request line is a comment, except <c>@name</c> (the name of the request); <c>@variable = value</c> lines define variables for the file. A line that starts with
/// <c>?</c> or <c>&amp;</c> right under the request line continues its URL. No scripts: a <c>&gt;</c> line (a handler of the response) is ignored.
/// </summary>
public sealed partial class HttpRequestFile
{
    private static readonly Regex RequestLine = RequestLineRegex();
    private static readonly string[] Methods = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT"];

    [GeneratedRegex(@"^\s*(?:(?<method>GET|POST|PUT|PATCH|DELETE|HEAD|OPTIONS|TRACE|CONNECT)\s+)?(?<url>(?:https?://|\{\{)\S*)(?:\s+HTTP/\d(?:\.\d)?)?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RequestLineRegex();

    [GeneratedRegex(@"^\s*@(?<name>[A-Za-z_][\w.\-]*)\s*=\s*(?<value>.*?)\s*$")]
    private static partial Regex VariableLineRegex();

    [GeneratedRegex(@"^\s*(?:#|//)\s*@name\s+(?<name>\S+)\s*$")]
    private static partial Regex NameLineRegex();

    private HttpRequestFile(List<HttpRequest> requests, Dictionary<string, string> variables)
    {
        Requests = requests;
        Variables = variables;
    }

    public IReadOnlyList<HttpRequest> Requests { get; }

    /// <summary>The variables the file defines with <c>@name = value</c> lines, the last definition of a name winning; the values may name other variables.</summary>
    public IReadOnlyDictionary<string, string> Variables { get; }

    /// <summary>The request whose block holds the zero-based <paramref name="line"/>; the last request before it when the line is in a part without one; null when there is none.</summary>
    public HttpRequest? At(int line)
    {
        HttpRequest? best = null;
        foreach (var request in Requests)
        {
            if (request.BlockFirstLine > line) break;
            best = request;
            if (line <= request.LastLine) return request;
        }
        return best;
    }

    public static HttpRequestFile Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var requests = new List<HttpRequest>();
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        int start = 0;
        for (int i = 0; i <= lines.Length; i++)
        {
            if (i < lines.Length && !lines[i].StartsWith("###", StringComparison.Ordinal)) continue;
            Block(lines, start, i - 1, requests, variables);
            start = i + 1;
        }
        return new HttpRequestFile(requests, variables);
    }

    private static void Block(string[] lines, int first, int last, List<HttpRequest> requests, Dictionary<string, string> variables)
    {
        string? name = null;
        int i = first;
        for (; i <= last; i++)
        {
            string line = lines[i];
            if (line.Trim().Length == 0) continue;
            if (NameLineRegex().Match(line) is { Success: true } named) { name = named.Groups["name"].Value; continue; }
            if (VariableLineRegex().Match(line) is { Success: true } variable) { variables[variable.Groups["name"].Value] = variable.Groups["value"].Value; continue; }
            if (line.TrimStart().StartsWith('#') || line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
            break;
        }
        if (i > last || RequestLine.Match(lines[i]) is not { Success: true } m) return;
        string method = m.Groups["method"].Success ? m.Groups["method"].Value.ToUpperInvariant() : "GET";
        var url = new StringBuilder(m.Groups["url"].Value);
        int requestLine = i++;
        for (; i <= last && lines[i].TrimStart() is { Length: > 0 } t && (t[0] == '?' || t[0] == '&'); i++) url.Append(t);
        var headers = new List<(string, string)>();
        for (; i <= last && lines[i].Trim().Length > 0; i++)
        {
            string line = lines[i];
            int colon = line.IndexOf(':');
            if (colon > 0 && !line.TrimStart().StartsWith('>')) headers.Add((line[..colon].Trim(), line[(colon + 1)..].Trim()));
        }
        var body = new StringBuilder();
        for (i++; i <= last; i++)
        {
            if (IsHandlerLine(lines[i])) break; // the handler of a response is not run
            body.Append(lines[i]).Append('\n');
        }
        string address = Regex.Replace(url.ToString(), @"\s+HTTP/\d(?:\.\d)?\s*$", ""); // the version may come after the continued lines
        requests.Add(new HttpRequest(name, method, address, headers, body.ToString().TrimEnd('\n', ' ') , requestLine, last, first));
    }

    private static bool IsHandlerLine(string line) => line.TrimStart().StartsWith("> ", StringComparison.Ordinal) || line.TrimStart().StartsWith("> {%", StringComparison.Ordinal);

    public static bool IsMethod(string word) => Methods.Contains(word, StringComparer.OrdinalIgnoreCase);
}
