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
}

/// <summary>The RTF form of the clipboard: "Rich Text Format" on Windows, <c>public.rtf</c> on macOS, <c>text/rtf</c> elsewhere. RTF is ASCII, so the bytes are the text.</summary>
internal static class ClipboardRtf
{
    public static DataFormat<byte[]> Format { get; } = DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsWindows() ? "Rich Text Format" : OperatingSystem.IsMacOS() ? "public.rtf" : "text/rtf");

    public static byte[] Encode(string rtf) => Encoding.ASCII.GetBytes(rtf);
}
