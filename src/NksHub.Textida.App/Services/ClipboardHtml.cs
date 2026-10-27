using System.Text;
using Avalonia.Input;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The HTML form of the clipboard. Windows takes the registered format "HTML Format", whose text starts with a header of byte offsets (CF_HTML);
/// the other systems take the HTML as UTF-8 under their own name.
/// </summary>
internal static class ClipboardHtml
{
    /// <summary>The platform format HTML is put on the clipboard under.</summary>
    public static DataFormat<byte[]> Format { get; } = DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsWindows() ? "HTML Format" : OperatingSystem.IsMacOS() ? "public.html" : "text/html");

    /// <summary>The bytes of a fragment in the form the platform expects.</summary>
    public static byte[] Encode(string fragment) => OperatingSystem.IsWindows() ? CfHtml(fragment) : Encoding.UTF8.GetBytes(fragment);

    /// <summary>CF_HTML version 0.9: UTF-8 text with a header whose offsets (ten digits, in bytes) mark the document and the fragment inside it.</summary>
    internal static byte[] CfHtml(string fragment)
    {
        const string Header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n";
        const string Before = "<html><body>\r\n<!--StartFragment-->";
        const string After = "<!--EndFragment-->\r\n</body></html>";
        int headerLength = Encoding.UTF8.GetByteCount(string.Format(Header, 0, 0, 0, 0));
        int fragmentLength = Encoding.UTF8.GetByteCount(fragment);
        int startFragment = headerLength + Before.Length;
        int endFragment = startFragment + fragmentLength;
        int end = endFragment + After.Length;
        return Encoding.UTF8.GetBytes(string.Format(Header, headerLength, end, startFragment, endFragment) + Before + fragment + After);
    }

    /// <summary>
    /// The HTML in the bytes another program put on the clipboard: with a CF_HTML header the fragment it marks (else the HTML document it marks),
    /// without one the whole UTF-8 text. Trailing NUL bytes some programs add are dropped.
    /// </summary>
    public static string Decode(byte[] raw)
    {
        int length = raw.Length;
        while (length > 0 && raw[length - 1] == 0) length--;
        string text = Encoding.UTF8.GetString(raw, 0, length);
        if (!text.StartsWith("Version:", StringComparison.Ordinal)) return text;
        int tag = text.IndexOf('<');
        string header = tag < 0 ? text : text[..tag];
        var range = Marked(header, "StartFragment", "EndFragment", length) ?? Marked(header, "StartHTML", "EndHTML", length);
        return range is { } r ? Encoding.UTF8.GetString(raw, r.Start, r.End - r.Start) : text;
    }

    private static (int Start, int End)? Marked(string header, string startName, string endName, int length)
    {
        int start = HeaderNumber(header, startName), end = HeaderNumber(header, endName);
        return start >= 0 && end >= start && end <= length ? (start, end) : null;
    }

    /// <summary>The byte offset after <c>name:</c> in the header; -1 when it is missing or negative (CF_HTML writes -1 for a part it leaves out).</summary>
    private static int HeaderNumber(string header, string name)
    {
        int at = header.IndexOf(name + ":", StringComparison.Ordinal);
        if (at < 0) return -1;
        var rest = header.AsSpan(at + name.Length + 1);
        int digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
        return digits > 0 && int.TryParse(rest[..digits], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : -1;
    }
}

/// <summary>The RTF form of the clipboard: "Rich Text Format" on Windows, <c>public.rtf</c> on macOS, <c>text/rtf</c> elsewhere. RTF is ASCII, so the bytes are the text.</summary>
internal static class ClipboardRtf
{
    public static DataFormat<byte[]> Format { get; } = DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsWindows() ? "Rich Text Format" : OperatingSystem.IsMacOS() ? "public.rtf" : "text/rtf");

    public static byte[] Encode(string rtf) => Encoding.ASCII.GetBytes(rtf);
}
