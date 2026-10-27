using System.Net;
using System.Text;

namespace NksHub.Textida.Plugins.Formatters;

/// <summary>
/// HTML entities for a piece of text. <see cref="Encode"/> escapes the five characters that matter in markup (<c>&amp; &lt; &gt; &quot; '</c>) and nothing else, so
/// <c>Decode(Encode(x)) == x</c> for every text: each <c>&amp;</c> of the original becomes <c>&amp;amp;</c> and so cannot be read as the start of an entity.
/// <see cref="Decode"/> reads every entity the .NET decoder knows (named, decimal and hexadecimal).
/// </summary>
public static class HtmlEntities
{
    public static string Encode(string text)
    {
        var sb = new StringBuilder(text.Length + text.Length / 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#39;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    public static string Decode(string text) => WebUtility.HtmlDecode(text);
}
