using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Readers of the files a PSPad installation keeps (PZ.1.1). They only read: nothing here writes to the folder of PSPad, and every reader takes the text of the file, so what is read is
/// decided by the caller. The formats are the ones PSPad 5.5 writes (INI-like files with their own habits: the first of two equal keys wins, <c>;</c> starts a comment, a line without <c>=</c> is a word).
/// </summary>
internal static class PspadText
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Text of a file: a byte order mark decides, else valid UTF-8 is UTF-8, else Windows-1250 (the code page of the Central European Windows PSPad was made on).</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1250).GetString(bytes);
        }
    }

    public static string[] Lines(string text) => text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
}

/// <summary>One line of an INI file: <see cref="Key"/> and <see cref="Value"/> (null without a <c>=</c>), and the whole <see cref="Line"/>.</summary>
internal sealed record PspadIniEntry(string Key, string? Value, string Line);

internal sealed class PspadIni
{
    private readonly List<(string Name, List<PspadIniEntry> Entries)> _sections = [];

    public IReadOnlyList<(string Name, List<PspadIniEntry> Entries)> Sections => _sections;

    public static PspadIni Parse(string text)
    {
        var ini = new PspadIni();
        List<PspadIniEntry>? current = null;
        foreach (string raw in PspadText.Lines(text))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[' && line.EndsWith(']'))
            {
                current = [];
                ini._sections.Add((line[1..^1].Trim(), current));
                continue;
            }
            if (current is null || line[0] == ';') continue;
            int eq = line.IndexOf('=');
            current.Add(eq < 0 ? new PspadIniEntry(line, null, raw) : new PspadIniEntry(line[..eq].Trim(), line[(eq + 1)..].Trim(), raw));
        }
        return ini;
    }

    public IEnumerable<List<PspadIniEntry>> Named(string name) => _sections.Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).Select(s => s.Entries);

    /// <summary>The first value of the key in the section; names are compared without regard to case and the first of two equal keys wins, as in PSPad.</summary>
    public string? Get(string section, string key) =>
        Named(section).SelectMany(e => e).FirstOrDefault(e => e.Value is not null && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
}

/// <summary>A connection of <c>PSPadFTP.INI</c>. <see cref="Password"/> is the clear password, decoded only when it is asked for; null when the connection has none saved.</summary>
internal sealed record PspadConnection(string Name, string Folder, string Host, int Port, string User, bool Tls, bool Passive, string? Password)
{
    public bool HasPassword => Password is not null;
}

internal static class PspadFtp
{
    private static readonly byte[] Key = [0xE8, 0xF8, 0x9E, 0xFD, 0xE1, 0xE8, 0x9A, 0xED, 0xE9];

    /// <summary>
    /// The obfuscation of PSPad: three random hex digits, the bytes of the password XOR a 9-byte key (used again from the start) written as hex pairs, and one more random digit.
    /// This is not encryption, and it is read only to put the password into the credential store when the user asked for that.
    /// </summary>
    public static string? Decode(string stored)
    {
        if (stored.Length < 4 || (stored.Length - 4) % 2 != 0) return null;
        string pairs = stored[3..^1];
        var bytes = new byte[pairs.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(pairs.AsSpan(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte b)) return null;
            bytes[i] = (byte)(b ^ Key[i % Key.Length]);
        }
        return Encoding.Latin1.GetString(bytes);
    }

    /// <summary>The same obfuscation in the other direction, for tests that need a file as PSPad writes it.</summary>
    public static string Encode(string password, string prefix = "A1B", string suffix = "7")
    {
        var sb = new StringBuilder(prefix);
        byte[] bytes = Encoding.Latin1.GetBytes(password);
        for (int i = 0; i < bytes.Length; i++) sb.Append((bytes[i] ^ Key[i % Key.Length]).ToString("X2", CultureInfo.InvariantCulture));
        return sb.Append(suffix).ToString();
    }

    /// <summary>Every section is a connection. <paramref name="withPasswords"/> decodes the saved password; without it no password is read at all.</summary>
    public static List<PspadConnection> Read(string text, bool withPasswords)
    {
        var result = new List<PspadConnection>();
        foreach (var (name, entries) in PspadIni.Parse(text).Sections)
        {
            string? Get(string key) => entries.FirstOrDefault(e => e.Value is not null && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
            string server = Get("Server") ?? "";
            if (server.Length == 0) continue;
            int port = 0;
            int colon = server.LastIndexOf(':');
            if (colon > 0 && server.IndexOf(':') == colon && int.TryParse(server.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int p) && p is > 0 and <= 65535)
            {
                port = p;
                server = server[..colon];
            }
            string? password = null;
            if (withPasswords)
            {
                if (Get("Password") is { Length: > 0 } stored && Decode(stored) is { } clear) password = clear;
                else if (Get("PlainPassword") is { Length: > 0 } plain) password = plain;
            }
            result.Add(new PspadConnection(name.TrimEnd('*').Trim(), Get("Folder") ?? "", server.Trim(), port, Get("User") ?? "", Get("SSL") == "1", Get("PassiveMode") != "0", password));
        }
        return result;
    }

    /// <summary>True when the connection section holds a saved password, decoded or not.</summary>
    public static int WithSavedPassword(string text) =>
        PspadIni.Parse(text).Sections.Count(s => s.Entries.Any(e => e.Value is { Length: > 0 } && (string.Equals(e.Key, "Password", StringComparison.OrdinalIgnoreCase) || string.Equals(e.Key, "PlainPassword", StringComparison.OrdinalIgnoreCase))));
}

/// <summary>A project file (<c>.ppr</c>): its name, the files of its tree and the default folder.</summary>
internal sealed record PspadProject(string Name, string Folder, string? DefaultFolder, IReadOnlyList<string> Files);

internal static class PspadProjects
{
    /// <summary>
    /// Reads the project tree: the first line is the name of the project, the following lines are folders (<c>+</c> or <c>-</c> in front) and files, indented by tabs or spaces. A path that is not absolute
    /// is relative to the folder of the project file. The folders of the tree exist only inside the project, so only the files are taken.
    /// </summary>
    public static PspadProject? Read(string text, string projectFile)
    {
        var ini = PspadIni.Parse(text);
        string folder = Path.GetDirectoryName(Path.GetFullPath(projectFile))!;
        var tree = ini.Named("Project tree").FirstOrDefault();
        if (tree is null) return null;
        var lines = tree.Select(e => e.Line.TrimEnd()).Where(l => l.Trim().Length > 0).ToList();
        string name = lines.Count > 0 ? lines[0].Trim().TrimStart('+', '-') : Path.GetFileNameWithoutExtension(projectFile);
        var files = new List<string>();
        foreach (string line in lines.Skip(1))
        {
            string item = OperatingSystem.IsWindows() ? line.Trim() : line.Trim().Replace('\\', '/'); // PSPad writes Windows paths
            if (item.Length == 0 || item[0] is '+' or '-' || item == "***") continue;
            if (item.Contains('=') && !item.Contains(Path.DirectorySeparatorChar) && !item.Contains('/') && !item.Contains('\\')) continue;
            try { files.Add(Path.GetFullPath(item, folder)); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { /* not a path */ }
        }
        string? defaultDir = ini.Get("Config", "DefaultDir") is { Length: > 0 } d ? d : null;
        return new PspadProject(name.Length > 0 ? name : Path.GetFileNameWithoutExtension(projectFile), folder, defaultDir, files);
    }
}

/// <summary>A clip of a <c>Context\*.DEF</c> file: its name, its description and the text with the cursor and the selection marks as the file has them.</summary>
internal sealed record PspadClip(string Name, string Description, string Text, bool UsesMacro);

internal static class PspadClips
{
    private static readonly Regex Header = new(@"^\[(?<body>.*)\](?:\*(?<hotkey>.*))?\s*$", RegexOptions.Compiled);
    private static readonly Regex Reference = new(@"%(?<name>[^%\s]+)%", RegexOptions.Compiled);

    /// <summary>The cursor mark and the selection mark of a file (the defaults are <c>|</c> and <c>§</c>; <c>[Clip Settings]</c> may name others).</summary>
    public static (char Cursor, char Selection) Marks(string text)
    {
        char cursor = '|', selection = '§';
        foreach (string raw in PspadText.Lines(text))
        {
            string line = raw.Trim();
            if (line.StartsWith("CursorChar=", StringComparison.OrdinalIgnoreCase) && line.Length > 11) cursor = line[11];
            else if (line.StartsWith("SelectionChar=", StringComparison.OrdinalIgnoreCase) && line.Length > 14) selection = line[14];
        }
        return (cursor, selection);
    }

    /// <summary>The clips in file order, duplicates kept. A clip that names a macro of the file (<c>%name%</c> defined in <c>[Macro definition]</c>) is marked, because the editor has no such dialog.</summary>
    public static List<PspadClip> Read(string text)
    {
        var clips = new List<PspadClip>();
        var macros = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string mode = "clip";
        string? name = null, description = "";
        var body = new List<string>();

        void Finish()
        {
            if (name is null) return;
            while (body.Count > 0 && body[^1].Length == 0) body.RemoveAt(body.Count - 1);
            clips.Add(new PspadClip(name, description ?? "", string.Join("\n", body), false));
            name = null;
            body.Clear();
        }

        foreach (string raw in PspadText.Lines(text))
        {
            string trimmed = raw.TrimEnd();
            if (trimmed.StartsWith('['))
            {
                string upper = trimmed.ToUpperInvariant();
                if (upper.EndsWith("KEYWORDS]", StringComparison.Ordinal)) { Finish(); mode = "keywords"; continue; }
                if (upper.EndsWith("TREE CONTENT]", StringComparison.Ordinal)) { Finish(); mode = "tree"; continue; }
                if (upper.EndsWith("CLIP SETTINGS]", StringComparison.Ordinal)) { Finish(); mode = "settings"; continue; }
                if (upper.EndsWith("MACRO DEFINITION]", StringComparison.Ordinal)) { Finish(); mode = "macro"; continue; }
                if (mode is "clip" or "keywords" or "tree" or "settings" or "macro")
                {
                    Finish();
                    mode = "clip";
                    var m = Header.Match(trimmed);
                    string headerBody = m.Success ? m.Groups["body"].Value : trimmed[1..];
                    int bar = headerBody.IndexOf('|');
                    name = (bar < 0 ? headerBody : headerBody[..bar]).Trim();
                    description = bar < 0 ? "" : headerBody[(bar + 1)..].Trim();
                    continue;
                }
            }
            switch (mode)
            {
                case "macro":
                    if (trimmed.StartsWith('%') && trimmed.IndexOf('=') is > 1 and var eq) macros.Add(trimmed[1..eq].Trim('%', ' '));
                    break;
                case "clip" when name is not null:
                    if (trimmed.StartsWith(";;", StringComparison.Ordinal)) body.Add(";" + trimmed[2..]);
                    else if (!trimmed.StartsWith(';')) body.Add(raw.TrimEnd('\r', '\n'));
                    break;
            }
        }
        Finish();
        return [.. clips.Select(c => c with { UsesMacro = Reference.Matches(c.Text).Any(r => macros.Contains(r.Groups["name"].Value)) })];
    }

    /// <summary>
    /// The text of the clip in the template language of the editor: the first cursor mark becomes the caret <c>$|</c>, the selection mark the selected text, a dollar sign is doubled, and the other cursor marks stay text.
    /// </summary>
    public static string ToTemplate(string text, char cursor, char selection)
    {
        var sb = new StringBuilder();
        bool placed = false;
        foreach (char c in text)
        {
            if (c == cursor && !placed) { sb.Append("$|"); placed = true; }
            else if (c == selection) sb.Append("${TM_SELECTED_TEXT}");
            else if (c == '$') sb.Append("$$");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>A macro of a <c>Macro\*.PME</c> file as steps the editor can play, or why it cannot be played.</summary>
internal sealed record PspadMacro(string Name, IReadOnlyList<(string? Command, string? Text)> Steps, string? Unsupported);

internal static class PspadMacros
{
    // the editor commands of PSPad (ec...) that have an equal in the editor, taken from the command list of the scripting help of PSPad
    private static readonly Dictionary<string, string> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ecLeft"] = "nks.edit.cursorLeft", ["ecRight"] = "nks.edit.cursorRight", ["ecUp"] = "nks.edit.cursorUp", ["ecDown"] = "nks.edit.cursorDown",
        ["ecWordLeft"] = "nks.edit.cursorWordLeft", ["ecWordRight"] = "nks.edit.cursorWordRight", ["ecLineStart"] = "nks.edit.cursorLineStart", ["ecLineEnd"] = "nks.edit.cursorLineEnd",
        ["ecPageUp"] = "nks.edit.cursorPageUp", ["ecPageDown"] = "nks.edit.cursorPageDown", ["ecEditorTop"] = "nks.edit.cursorDocumentStart", ["ecEditorBottom"] = "nks.edit.cursorDocumentEnd",
        ["ecSelLeft"] = "nks.edit.cursorLeftSelect", ["ecSelRight"] = "nks.edit.cursorRightSelect", ["ecSelUp"] = "nks.edit.cursorUpSelect", ["ecSelDown"] = "nks.edit.cursorDownSelect",
        ["ecSelWordLeft"] = "nks.edit.cursorWordLeftSelect", ["ecSelWordRight"] = "nks.edit.cursorWordRightSelect", ["ecSelLineStart"] = "nks.edit.cursorLineStartSelect",
        ["ecSelLineEnd"] = "nks.edit.cursorLineEndSelect", ["ecSelPageUp"] = "nks.edit.cursorPageUpSelect", ["ecSelPageDown"] = "nks.edit.cursorPageDownSelect",
        ["ecSelEditorTop"] = "nks.edit.cursorDocumentStartSelect", ["ecSelEditorBottom"] = "nks.edit.cursorDocumentEndSelect",
        ["ecSelectAll"] = "nks.edit.selectAll", ["ecCopy"] = "nks.edit.copy",
        ["ecDeleteLastChar"] = "nks.edit.deleteLeft", ["ecDeleteChar"] = "nks.edit.deleteRight", ["ecDeleteWord"] = "nks.edit.deleteWordRight", ["ecDeleteLastWord"] = "nks.edit.deleteWordLeft",
        ["ecDeleteLine"] = "nks.edit.deleteLine", ["ecLineBreak"] = "nks.edit.newline",
    };

    public static bool Knows(string ecName) => Commands.ContainsKey(ecName);

    public static string? CommandOf(string ecName) => Commands.GetValueOrDefault(ecName);

    /// <summary>One command a line; <c>ecChar{text}</c> types the text. A command the editor has no equal for makes the whole macro unsupported, so a macro never plays half.</summary>
    public static PspadMacro Read(string name, string text)
    {
        var steps = new List<(string?, string?)>();
        string? unsupported = null;
        foreach (string raw in PspadText.Lines(text))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("ecChar{", StringComparison.OrdinalIgnoreCase) && line.EndsWith('}'))
            {
                steps.Add((null, line[7..^1]));
                continue;
            }
            string command = line.Split('{', ' ', '\t')[0];
            if (Commands.TryGetValue(command, out string? id) && line == command) steps.Add((id, null));
            else unsupported ??= command;
        }
        return new PspadMacro(name, steps, unsupported ?? (steps.Count == 0 ? "empty" : null));
    }
}

/// <summary>A user highlighter (<c>Syntax\*.INI</c>) turned into a grammar of the editor, with the settings it could not carry over.</summary>
internal sealed record PspadHighlighter(string Name, string Id, string GrammarJson, IReadOnlyList<string> NotConverted);

internal static class PspadHighlighters
{
    // the comment styles of PSPad that have a fixed form: a line comment mark or a pair of block marks
    private static readonly (string Key, string? Line, string? Open, string? Close)[] CommentStyles =
    [
        ("SlashComment", "//", null, null), ("CComment", null, "/*", "*/"), ("BasComment", "'", null, null), ("SinglQComment", "'", null, null), ("SharpComment", "#", null, null),
        ("SQLComment", "--", null, null), ("PasComment", null, "{", "}"), ("ANSIComment", null, "(*", "*)"), ("HTMLComment", null, "<!--", "-->"), ("ExclComment", "!", null, null),
        ("PerCentComment", "%", null, null), ("HaskellComment", null, "{-", "-}"), ("PwShellComment", null, "<#", "#>"), ("INIComment", ";", null, null),
        ("DblQComment", "\"", null, null), ("DComment", null, "/+", "+/"), ("LuaComment", null, "--[[", "]]"),
    ];

    private static readonly string[] Handled = ["Name", "FileType", "CaseSensitive", "CommentString", "KeyWordChars", "DoubleQuote", "SingleQuote", "ApostropheQuote", "EscString", "EscChar", "Preprocessors",
        "IndentChar", "UnIndentChar", "TabWidth", "HTMLGroup", "CodeExplorer", "CodeFolding", "DocComment", "Label", "Vectors", "PHPVariable", "RubySymbols", "KW1StartWith", "KW2StartWith", "KW3StartWith", "KW4StartWith"];

    private static string Bool(string? v) => v == "1" ? "1" : "0";

    private static string Json(string text) => "\"" + System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(text) + "\"";

    /// <summary>Null when the file is not a highlighter (it has no <c>[Settings]</c> with a name).</summary>
    public static PspadHighlighter? Read(string text, string fileName)
    {
        var ini = PspadIni.Parse(text);
        string? name = ini.Get("Settings", "Name");
        if (string.IsNullOrWhiteSpace(name)) return null;
        bool Flag(string key) => Bool(ini.Get("Settings", key)) == "1";
        string? Setting(string key) => ini.Get("Settings", key);
        var notConverted = new List<string>();

        string id = "pspad-" + ClipLibrary.Slug(name);
        var extensions = new List<string>();
        foreach (string mask in (Setting("FileType") ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string ext = mask.StartsWith("*.") ? mask[1..] : mask.StartsWith('.') ? mask : mask.Contains('*') || mask.Contains('?') || mask.Contains('.') ? "" : "." + mask;
            if (ext.Length > 1 && ext.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') && !extensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) extensions.Add(ext.ToLowerInvariant());
        }

        var sb = new StringBuilder();
        sb.Append("{\"formatVersion\":1,\"id\":").Append(Json(id)).Append(",\"name\":").Append(Json(name!.Trim())).Append(",\"version\":\"1.0.0\"");
        sb.Append(",\"caseSensitive\":").Append(Flag("CaseSensitive") ? "true" : "false");
        sb.Append(",\"extensions\":[").Append(string.Join(',', extensions.Select(Json))).Append(']');
        string wordChars = Setting("KeyWordChars") ?? "";
        string extra = new([.. wordChars.Where(c => !char.IsLetterOrDigit(c) && c != '_' && !char.IsWhiteSpace(c))]);
        if (extra.Length > 0) sb.Append(",\"wordPattern\":").Append(Json(@"[\w" + Regex.Escape(extra).Replace("]", @"\]").Replace("-", @"\-") + "]+"));

        string? lineComment = Setting("CommentString") is { Length: > 0 } cs ? cs : null;
        if (lineComment is not null) sb.Append(",\"comments\":{\"line\":").Append(Json(lineComment)).Append('}');

        // keyword groups: KeyWords, ReservedWords, KeyWords2, KeyWords3 become keyword, constant, type and function
        string[][] sections = [["KeyWords", "keyword"], ["ReservedWords", "constant"], ["KeyWords2", "type"], ["KeyWords3", "function"]];
        var groups = new List<string>();
        foreach (var section in sections)
        {
            var words = new List<string>();
            foreach (var (sectionName, entries) in ini.Sections.Where(s => string.Equals(s.Name, section[0], StringComparison.OrdinalIgnoreCase) || s.Name.StartsWith(section[0] + "-", StringComparison.OrdinalIgnoreCase)))
                foreach (var entry in entries)
                {
                    string line = entry.Line.Trim();
                    string word = line.EndsWith('=') ? line[..^1].Trim() : (entry.Value is null ? line : entry.Key);
                    if (word.Length > 0 && word.Length <= 64 && !word.Any(char.IsWhiteSpace)) words.Add(word);
                }
            if (words.Count > 0) groups.Add("{\"name\":" + Json(section[0]) + ",\"token\":" + Json(section[1]) + ",\"words\":[" + string.Join(',', words.Distinct(StringComparer.Ordinal).Select(Json)) + "]}");
        }
        if (groups.Count > 0) sb.Append(",\"keywords\":[").Append(string.Join(',', groups)).Append(']');

        var rules = new List<string>();
        foreach (var style in CommentStyles)
        {
            if (!Flag(style.Key)) continue;
            if (style.Line is not null) rules.Add("{\"match\":" + Json(Regex.Escape(style.Line) + @"[^\r\n]*") + ",\"token\":\"comment\"}");
            else rules.Add("{\"begin\":" + Json(Regex.Escape(style.Open!)) + ",\"end\":" + Json(Regex.Escape(style.Close!)) + ",\"multiline\":true,\"token\":\"comment\"}");
        }
        string escape = Flag("EscString") ? (Setting("EscChar") is { Length: > 0 } ec ? Regex.Escape(ec[..1]) : @"\\") : "";
        string Quoted(char quote)
        {
            string q = Regex.Escape(quote.ToString());
            return escape.Length > 0 ? $"{q}(?:[^{q}{escape}\\r\\n]|{escape}.)*{q}" : $"{q}[^{q}\\r\\n]*{q}";
        }
        if (Flag("DoubleQuote")) rules.Add("{\"match\":" + Json(Quoted('"')) + ",\"token\":\"string\"}");
        if (Flag("SingleQuote") && !Flag("BasComment") && !Flag("SinglQComment")) rules.Add("{\"match\":" + Json(Quoted('\'')) + ",\"token\":\"string\"}");
        else if (Flag("SingleQuote")) notConverted.Add("SingleQuote (the apostrophe is a comment in this highlighter)");
        if (Flag("ApostropheQuote")) rules.Add("{\"match\":" + Json(Quoted('`')) + ",\"token\":\"string\"}");
        if (Flag("Preprocessors") && !Flag("SharpComment")) rules.Add("{\"match\":" + Json(@"(?<=^[ \t]*)#[ \t]*\w+") + ",\"token\":\"preprocessor\"}");
        if (Flag("PHPVariable")) rules.Add("{\"match\":" + Json(@"\$\w+") + ",\"token\":\"variable\"}");
        rules.Add("{\"match\":" + Json(@"(?<![\w.])\d+(?:\.\d+)?(?![\w.])") + ",\"token\":\"number\"}");
        if (groups.Count > 0) rules.Add("{\"words\":true}");
        sb.Append(",\"rules\":[").Append(string.Join(',', rules)).Append("]}");

        foreach (var (key, value) in ini.Named("Settings").SelectMany(e => e).Where(e => e.Value is { Length: > 0 } v && v != "0").Select(e => (e.Key, e.Value!)))
        {
            if (Handled.Contains(key, StringComparer.OrdinalIgnoreCase) || CommentStyles.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))) continue;
            if (key.EndsWith("Comment", StringComparison.OrdinalIgnoreCase) || key.Contains("Comm", StringComparison.OrdinalIgnoreCase)) notConverted.Add(key);
        }
        foreach (string key in new[] { "KW1StartWith", "KW2StartWith", "KW3StartWith", "KW4StartWith" })
            if (Flag(key)) notConverted.Add(key + " (words that match only their beginning)");
        if (Setting("IndentChar") is { Length: > 0 }) notConverted.Add("IndentChar");
        return new PspadHighlighter(name.Trim(), id, sb.ToString(), [.. notConverted.Distinct()]);
    }
}

/// <summary>A shortcut of <c>KeyMap.ini</c> turned into a binding of the editor.</summary>
internal sealed record PspadKey(string Name, string Key, string Command);

internal static class PspadKeys
{
    /// <summary>
    /// Reads <c>name=shortcut</c> pairs. A shortcut is text (<c>Ctrl+Shift+F5</c>) or the number a Delphi program stores (the key code, plus 0x2000 for Shift, 0x4000 for Ctrl and 0x8000 for Alt).
    /// Only the editor commands the macro reader knows are carried over; <paramref name="skipped"/> lists the names that have no equal.
    /// </summary>
    public static List<PspadKey> Read(string text, List<string> skipped)
    {
        var keys = new List<PspadKey>();
        foreach (var entry in PspadIni.Parse(text).Sections.SelectMany(s => s.Entries))
        {
            if (entry.Value is not { Length: > 0 } shortcut) continue;
            string? command = PspadMacros.CommandOf(entry.Key);
            string? key = Chord(shortcut);
            if (command is null || key is null) { skipped.Add(entry.Key); continue; }
            keys.Add(new PspadKey(entry.Key, key, command));
        }
        return keys;
    }

    /// <summary>The chord in the notation of the keymap (<c>ctrl+shift+f5</c>), or null when the text is neither a shortcut text nor a number.</summary>
    public static string? Chord(string shortcut)
    {
        var parts = new List<string>();
        string keyName;
        if (int.TryParse(shortcut, NumberStyles.None, CultureInfo.InvariantCulture, out int code))
        {
            if ((code & 0x4000) != 0) parts.Add("ctrl");
            if ((code & 0x8000) != 0) parts.Add("alt");
            if ((code & 0x2000) != 0) parts.Add("shift");
            keyName = KeyOfVirtualCode(code & 0xFFF) ?? "";
        }
        else
        {
            var tokens = shortcut.Split('+', StringSplitOptions.TrimEntries);
            if (tokens.Length == 0 || tokens[^1].Length == 0) return null;
            foreach (string modifier in tokens[..^1])
            {
                string m = modifier.ToLowerInvariant();
                if (m is "ctrl" or "alt" or "shift") parts.Add(m);
                else return null;
            }
            keyName = tokens[^1].ToLowerInvariant();
            if (keyName.Length > 1 && !(keyName[0] == 'f' && int.TryParse(keyName.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 24) &&
                keyName is not ("space" or "enter" or "tab" or "esc" or "home" or "end" or "left" or "right" or "up" or "down" or "pgup" or "pgdn" or "ins" or "del" or "backspace")) return null;
        }
        if (keyName.Length == 0) return null;
        return string.Join('+', [.. parts.OrderBy(p => Array.IndexOf(new[] { "ctrl", "alt", "shift" }, p)), keyName]);
    }

    private static string? KeyOfVirtualCode(int code) => code switch
    {
        >= 0x30 and <= 0x39 => ((char)code).ToString(),
        >= 0x41 and <= 0x5A => char.ToLowerInvariant((char)code).ToString(),
        >= 0x70 and <= 0x87 => "f" + (code - 0x6F).ToString(CultureInfo.InvariantCulture),
        0x20 => "space", 0x0D => "enter", 0x09 => "tab", 0x1B => "esc", 0x08 => "backspace", 0x2E => "del", 0x2D => "ins",
        0x24 => "home", 0x23 => "end", 0x25 => "left", 0x27 => "right", 0x26 => "up", 0x28 => "down", 0x21 => "pgup", 0x22 => "pgdn",
        _ => null,
    };
}
