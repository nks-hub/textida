namespace NksHub.Textida.App.Startup;

/// <summary>A file named on the command line with an optional 1-based line; <paramref name="Hex"/> opens it in the hex view, at <paramref name="Offset"/> when given.</summary>
public readonly record struct OpenRequest(string Path, int? Line, bool Hex = false, long? Offset = null);

/// <summary>Result of parsing the command line. When <see cref="Error"/> is set nothing else is meaningful.</summary>
public sealed record CommandLineArgs(
    IReadOnlyList<OpenRequest> Files,
    bool NewWindow,
    bool Wait,
    bool Smoke,
    string? Error,
    IReadOnlyList<string>? Diff = null,
    string? Project = null);

/// <summary>
/// Parses <c>[--new-window] [--wait] [--line N] [--hex] [--offset N] [--smoke] [--diff left right] [--] path...</c>. <c>--line N</c> (or <c>--line=N</c>)
/// applies to the nearest path without a line: the previous one, otherwise the next one. <c>--hex</c> opens that path in the hex view and
/// <c>--offset N</c> (decimal or <c>0x</c> hex, also <c>--offset=N</c>) puts the caret on a byte; an offset implies <c>--hex</c>.
/// <c>--diff left right</c> opens the comparison of two files: the left one in a tab of its own, the right one read-only.
/// <c>--project folder</c> (or <c>--project=folder</c>) opens a window with the folder as its root: the file panel, Git, search in the folder. A path that is an existing folder is taken the same way.
/// </summary>
public static class CommandLine
{
    public static CommandLineArgs Parse(IReadOnlyList<string> args, string workingDirectory)
    {
        var files = new List<OpenRequest>();
        bool newWindow = false, wait = false, smoke = false, onlyPaths = false;
        int? pendingLine = null;
        bool pendingHex = false;
        long? pendingOffset = null;
        string[]? diff = null;
        string? project = null;

        CommandLineArgs Fail(string error) => new([], false, false, false, error);

        bool TryOffset(string text, out long offset)
        {
            bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            return long.TryParse(hex ? text[2..] : text, hex ? System.Globalization.NumberStyles.AllowHexSpecifier : System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out offset) && offset >= 0;
        }

        bool TryLine(string text, out int line) => int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out line) && line >= 1;

        for (int i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (onlyPaths || !a.StartsWith("--", StringComparison.Ordinal))
            {
                var full = Path.GetFullPath(a, workingDirectory);
                if (pendingLine is null && !pendingHex && pendingOffset is null && Directory.Exists(full))
                {
                    if (project is not null) return Fail("only one folder can be opened as the project");
                    project = full;
                    continue;
                }
                files.Add(new OpenRequest(full, pendingLine, pendingHex || pendingOffset is not null, pendingOffset));
                pendingLine = null;
                pendingHex = false;
                pendingOffset = null;
                continue;
            }

            switch (a)
            {
                case "--": onlyPaths = true; break;
                case "--new-window": newWindow = true; break;
                case "--wait": wait = true; break;
                case "--smoke": smoke = true; break;
                case "--diff":
                    if (diff is not null || i + 2 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal) || args[i + 2].StartsWith("--", StringComparison.Ordinal))
                        return Fail("--diff needs two paths, once");
                    diff = [Path.GetFullPath(args[i + 1], workingDirectory), Path.GetFullPath(args[i + 2], workingDirectory)];
                    i += 2;
                    break;
                case "--hex":
                    if (files.Count > 0 && !files[^1].Hex) files[^1] = files[^1] with { Hex = true };
                    else pendingHex = true;
                    break;
                default:
                    string? value = null;
                    if (a == "--project" || a.StartsWith("--project=", StringComparison.Ordinal))
                    {
                        value = a == "--project" ? (i + 1 < args.Count ? args[++i] : null) : a["--project=".Length..];
                        if (string.IsNullOrWhiteSpace(value)) return Fail("--project needs a folder");
                        if (project is not null) return Fail("only one folder can be opened as the project");
                        project = Path.GetFullPath(value, workingDirectory);
                        break;
                    }
                    if (a == "--offset" || a.StartsWith("--offset=", StringComparison.Ordinal))
                    {
                        value = a == "--offset" ? (i + 1 < args.Count ? args[++i] : null) : a["--offset=".Length..];
                        if (value is null || !TryOffset(value, out long offset)) return Fail($"--offset needs a byte offset, got: {value ?? "(nothing)"}");
                        if (files.Count > 0 && files[^1].Offset is null) files[^1] = files[^1] with { Hex = true, Offset = offset };
                        else pendingOffset = offset;
                        break;
                    }
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
        if (pendingHex || pendingOffset is not null) return Fail("--hex and --offset need a path to apply to");
        if (wait && files.Count == 0 && diff is null) return Fail("--wait needs at least one path");
        return new CommandLineArgs(files, newWindow, wait, smoke, null, diff, project);
    }
}
