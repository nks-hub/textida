using System.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Accessibility.Host;

internal static class Program
{
    public const string SampleText = "Hello Textida\r\nsecond line, with words\r\nthird: ěščř \U0001F600 end\r\n";

    private static int Main(string[] args)
    {
        int exitAfter = 120;
        string text = SampleText;
        string? commands = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--exit-after") exitAfter = int.Parse(args[i + 1]);
            else if (args[i] == "--text-file") text = File.ReadAllText(args[i + 1]);
            else if (args[i] == "--commands") commands = args[i + 1];
        }
        var doc = Document.FromBytes(Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance);
        if (OperatingSystem.IsWindows()) return WinHost.Run(doc, exitAfter);
        if (OperatingSystem.IsMacOS()) return MacHost.Run(doc, exitAfter, commands);
        Console.Error.WriteLine("No native host for this operating system.");
        return 2;
    }

    /// <summary>ASCII-only escape so the final text can be compared across consoles and code pages.</summary>
    public static string Escape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
            sb.Append(c switch { '\r' => "\\r", '\n' => "\\n", '\t' => "\\t", '\\' => "\\\\", < ' ' or > '~' => $"\\u{(int)c:x4}", _ => c.ToString() });
        return sb.ToString();
    }
}
