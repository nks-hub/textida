using System.Text;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Core.AiContext;

public enum AiContextFormat { Markdown, Xml }

public enum AiOmitReason
{
    /// <summary>A <c>.env</c> file: its values are secrets, so the file is never copied.</summary>
    SecretFile,
    IgnoreRules,
    Hidden,
    Binary,
    TooLarge,
    Unreadable,
    /// <summary>A file shown in paged mode: only a selection of it is copied, never the whole file.</summary>
    Paged,
    /// <summary>The text does not fit in what is left of the size limit of the whole context.</summary>
    TotalLimit,
}

/// <summary>One part of a file. Lines are 1-based and inclusive; they are null when the line index does not reach the part yet (a paged file).</summary>
public sealed record AiRange(string Text, long? FirstLine = null, long? LastLine = null)
{
    public static AiRange Whole(string text) => new(text, 1, AiContextBuilder.CountLines(text));
}

/// <param name="Name">The path as the context shows it, or the title of a file that has no path yet.</param>
/// <param name="Path">The full path, the name of a virtual document, null for a file never saved. A secrets file is refused by it.</param>
/// <param name="Revision">Revision of the open document the text was taken from; null for a file read from disk.</param>
/// <param name="Unsaved">The text holds changes that are not in the file on disk.</param>
public sealed record AiSource(string Name, string? Path, string Language, string Encoding, long? Revision, bool Unsaved, IReadOnlyList<AiRange> Ranges);

/// <param name="Name">The file left out; null when <paramref name="Count"/> files of one reason are counted together.</param>
public sealed record AiOmission(AiOmitReason Reason, string? Name = null, long Count = 1);

public sealed record AiContextResult(string Text, int Files, IReadOnlyList<AiOmission> Omissions)
{
    public long Bytes => Encoding.UTF8.GetByteCount(Text);
}

/// <summary>
/// Writes files, selections and tabs as one block of Markdown or XML with path, line range, language, encoding and revision for an AI assistant.
/// This is the only gate: a secrets file (<see cref="SecretValues.IsSecretFile"/>) is left out here whatever the caller passes, so its values never
/// reach the text, and what was left out is named in the block with the reason. Nothing is sent anywhere; the caller puts the text on the clipboard.
/// </summary>
public static class AiContextBuilder
{
    public static string ReasonId(AiOmitReason reason) => reason switch
    {
        AiOmitReason.SecretFile => "secrets",
        AiOmitReason.IgnoreRules => "ignore-rules",
        AiOmitReason.Hidden => "hidden",
        AiOmitReason.Binary => "binary",
        AiOmitReason.TooLarge => "too-large",
        AiOmitReason.Unreadable => "unreadable",
        AiOmitReason.TotalLimit => "size-limit",
        _ => "paged",
    };

    /// <param name="maxTextBytes">
    /// Size limit of the whole context: the UTF-8 bytes of the copied text, without the headers, so Markdown and XML hold the same files. A source that does not
    /// fit in what is left is named as left out; a later, smaller one may still fit.
    /// </param>
    public static AiContextResult Build(IEnumerable<AiSource> sources, IEnumerable<AiOmission> omitted, AiContextFormat format, long maxTextBytes = long.MaxValue)
    {
        var kept = new List<AiSource>();
        var left = new List<AiOmission>(omitted);
        long room = maxTextBytes;
        foreach (var source in sources)
        {
            if (SecretValues.IsSecretFile(source.Path)) { left.Add(new AiOmission(AiOmitReason.SecretFile, source.Name)); continue; }
            long size = source.Ranges.Sum(r => (long)Encoding.UTF8.GetByteCount(r.Text));
            if (size > room) { left.Add(new AiOmission(AiOmitReason.TotalLimit, source.Name)); continue; }
            room -= size;
            kept.Add(source);
        }
        var sb = new StringBuilder();
        if (format == AiContextFormat.Markdown) WriteMarkdown(sb, kept, left);
        else WriteXml(sb, kept, left);
        return new AiContextResult(sb.ToString(), kept.Count, left);
    }

    /// <summary>
    /// True when text stored in <paramref name="storedBytes"/> bytes cannot fit in <paramref name="room"/> UTF-8 bytes, so it need not be read: every character
    /// takes at least one UTF-8 byte and at most four stored bytes (UTF-32), after a byte order mark of at most four.
    /// </summary>
    public static bool CannotFit(long storedBytes, long room) => (storedBytes - 4) / 4 > room;

    /// <summary>Number of lines of a text: a line break ends a line, so a final break starts no new one; an empty text is one line.</summary>
    public static long CountLines(string text)
    {
        long lines = 1;
        foreach (var c in text) if (c == '\n') lines++;
        return text.EndsWith('\n') && lines > 1 ? lines - 1 : lines;
    }

    private static string Describe(AiOmission o) => (o.Name ?? o.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " files") + ": " + ReasonId(o.Reason);

    private static string Lines(AiRange r) => r.FirstLine is { } a && r.LastLine is { } b ? (a == b ? $"{a}" : $"{a}-{b}") : "?";

    private static void WriteMarkdown(StringBuilder sb, List<AiSource> sources, List<AiOmission> omitted)
    {
        sb.Append("# Context\n");
        foreach (var s in sources)
        {
            sb.Append("\n## ").Append(s.Name).Append('\n');
            sb.Append("- language: ").Append(s.Language).Append('\n');
            sb.Append("- encoding: ").Append(s.Encoding).Append('\n');
            if (s.Revision is { } rev) sb.Append("- revision: ").Append(rev).Append('\n');
            if (s.Unsaved) sb.Append("- unsaved changes: yes\n");
            foreach (var r in s.Ranges)
            {
                sb.Append("\n### Lines ").Append(Lines(r)).Append('\n');
                string fence = new('`', Math.Max(3, LongestRun(r.Text, '`') + 1));
                sb.Append('\n').Append(fence).Append(s.Language == "plaintext" ? "" : s.Language).Append('\n').Append(r.Text);
                if (!r.Text.EndsWith('\n')) sb.Append('\n');
                sb.Append(fence).Append('\n');
            }
        }
        if (omitted.Count == 0) return;
        sb.Append("\n## Omitted\n\n");
        foreach (var o in omitted) sb.Append("- ").Append(Describe(o)).Append('\n');
    }

    private static void WriteXml(StringBuilder sb, List<AiSource> sources, List<AiOmission> omitted)
    {
        sb.Append("<context>\n");
        foreach (var s in sources)
        {
            sb.Append("<file path=\"").Append(Attr(s.Name)).Append("\" language=\"").Append(Attr(s.Language)).Append("\" encoding=\"").Append(Attr(s.Encoding)).Append('"');
            if (s.Revision is { } rev) sb.Append(" revision=\"").Append(rev).Append('"');
            if (s.Unsaved) sb.Append(" unsaved=\"true\"");
            sb.Append(">\n");
            foreach (var r in s.Ranges)
            {
                sb.Append("<lines range=\"").Append(Lines(r)).Append("\"><![CDATA[").Append(r.Text.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal)).Append("]]></lines>\n");
            }
            sb.Append("</file>\n");
        }
        foreach (var o in omitted)
        {
            sb.Append("<omitted");
            if (o.Name is { } name) sb.Append(" path=\"").Append(Attr(name)).Append('"'); else sb.Append(" count=\"").Append(o.Count).Append('"');
            sb.Append(" reason=\"").Append(ReasonId(o.Reason)).Append("\"/>\n");
        }
        sb.Append("</context>\n");
    }

    private static string Attr(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal).Replace("\n", "&#10;", StringComparison.Ordinal).Replace("\r", "&#13;", StringComparison.Ordinal);

    private static int LongestRun(string text, char c)
    {
        int best = 0, run = 0;
        foreach (var x in text)
        {
            run = x == c ? run + 1 : 0;
            if (run > best) best = run;
        }
        return best;
    }
}
