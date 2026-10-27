using System.Text;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// Builds the command line that starts a batch file through <c>cmd.exe</c>. cmd.exe parses the line itself (metacharacters, <c>%</c> expansion) and the batch file then splits it again,
/// so an argument is only safe when both passes see it as one literal. This follows the fix for CVE-2024-24576 ("BatBadBut") in the Rust standard library
/// (<c>library/std/src/sys/pal/windows/args.rs</c>, <c>make_bat_command_line</c> / <c>append_bat_arg</c>) and the .NET handling of batch files: every argument is quoted, a quote is doubled
/// (after doubling the backslashes in front of it), a <c>%</c> becomes <c>%%cd:~,%</c> (the second <c>%</c> starts a variable that expands to nothing, which leaves a literal percent sign), and
/// <c>\r</c> / <c>\n</c> cannot be passed at all. Inside the quotes <c>&amp; | &lt; &gt; ^ ( )</c> are literal for cmd.exe, so they need no escape of their own.
/// </summary>
internal static class BatchCommandLine
{
    public static bool IsBatchFile(string path) =>
        Path.GetExtension(path) is { } ext && (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) || ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase));

    /// <summary>The path of cmd.exe in the system folder (not the COMSPEC variable, which a project could change).</summary>
    public static string Interpreter => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    /// <summary>
    /// The text after <c>cmd.exe</c>: <c>/d /v:off /s /c ""script" "arg" ..."</c>. <c>/d</c> skips the AutoRun commands, <c>/v:off</c> keeps delayed expansion off whatever the registry says,
    /// <c>/s</c> makes cmd.exe strip exactly the outer pair of quotes. Throws <see cref="ToolException"/> for an argument that cannot be passed safely.
    /// </summary>
    public static string Build(string script, IReadOnlyList<string> args)
    {
        bool delayed = EnablesDelayedExpansion(script);
        var sb = new StringBuilder("/d /v:off /s /c \"");
        Append(sb, script, "path of the batch file", delayed);
        foreach (var arg in args) Append(sb.Append(' '), arg, $"argument '{arg}'", delayed);
        return sb.Append('"').ToString();
    }

    private static void Append(StringBuilder sb, string text, string what, bool delayed)
    {
        foreach (char c in text)
        {
            if (c < ' ' && c != '\t') throw new ToolException($"The {what} holds a control character (code {(int)c}); cmd.exe cannot pass it safely to a batch file.");
            if (c == '!' && delayed) throw new ToolException($"The {what} holds '!' and the batch file turns delayed expansion on, which would change it; cmd.exe cannot pass it safely.");
        }
        sb.Append('"');
        int backslashes = 0;
        foreach (char c in text)
        {
            if (c == '\\') { backslashes++; continue; }
            sb.Append('\\', backslashes);
            if (c == '"') sb.Append('\\', backslashes).Append('"');
            backslashes = 0;
            if (c == '%') sb.Append("%%cd:~,%");
            else sb.Append(c);
        }
        sb.Append('\\', backslashes * 2).Append('"');
    }

    /// <summary>A batch file that writes <c>enabledelayedexpansion</c> or <c>/v:on</c> makes cmd.exe read <c>!</c> in the arguments as a variable; the text is searched, an unreadable file counts as yes.</summary>
    private static bool EnablesDelayedExpansion(string script)
    {
        try
        {
            using var reader = new StreamReader(script, Encoding.Latin1);
            var buffer = new char[1 << 20];
            string text = new(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
            return text.Contains("enabledelayedexpansion", StringComparison.OrdinalIgnoreCase) || text.Contains("/v:on", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }
}
