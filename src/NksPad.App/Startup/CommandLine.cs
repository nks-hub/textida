namespace NksPad.App.Startup;

/// <summary>A file named on the command line with an optional 1-based line.</summary>
public readonly record struct OpenRequest(string Path, int? Line);

/// <summary>Result of parsing the command line. When <see cref="Error"/> is set nothing else is meaningful.</summary>
public sealed record CommandLineArgs(
    IReadOnlyList<OpenRequest> Files,
    bool NewWindow,
    bool Wait,
    bool Smoke,
    string? Error);

/// <summary>
/// Parses <c>[--new-window] [--wait] [--line N] [--smoke] [--] path...</c>. <c>--line N</c> (or <c>--line=N</c>)
/// applies to the nearest path without a line: the previous one, otherwise the next one.
/// </summary>
public static class CommandLine
{
    public static CommandLineArgs Parse(IReadOnlyList<string> args, string workingDirectory)
    {
        var files = new List<OpenRequest>();
        bool newWindow = false, wait = false, smoke = false, onlyPaths = false;
        int? pendingLine = null;

        CommandLineArgs Fail(string error) => new([], false, false, false, error);

        bool TryLine(string text, out int line) => int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out line) && line >= 1;

        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (onlyPaths || !a.StartsWith("--", StringComparison.Ordinal))
            {
                files.Add(new OpenRequest(Path.GetFullPath(a, workingDirectory), pendingLine));
                pendingLine = null;
                continue;
            }

            switch (a)
            {
                case "--": onlyPaths = true; break;
                case "--new-window": newWindow = true; break;
                case "--wait": wait = true; break;
                case "--smoke": smoke = true; break;
                default:
                    string? value = null;
                    if (a == "--line") value = i + 1 < args.Count ? args[++i] : null;
                    else if (a.StartsWith("--line=", StringComparison.Ordinal)) value = a["--line=".Length..];
                    else return Fail($"unknown option: {a}");

                    if (value is null || !TryLine(value, out int line)) return Fail($"--line needs a positive number, got: {value ?? "(nothing)"}");
                    if (files.Count > 0 && files[^1].Line is null) files[^1] = files[^1] with { Line = line };
                    else pendingLine = line;
                    break;
            }
        }

        if (pendingLine is not null) return Fail("--line has no path to apply to");
        if (wait && files.Count == 0) return Fail("--wait needs at least one path");
        return new CommandLineArgs(files, newWindow, wait, smoke, null);
    }
}
