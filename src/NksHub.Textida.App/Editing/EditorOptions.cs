using Avalonia.Media;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.App.Editing;

/// <summary>The settings the editor surface and the file commands read, resolved for one language.</summary>
internal sealed record EditorOptions(
    int TabSize,
    int IndentSize,
    bool InsertSpaces,
    bool WordWrap,
    bool ShowWhitespace,
    string FontFamily,
    double FontSize,
    bool AutoClosePairs,
    bool ContinueComments,
    EolStyle? ConfiguredEol,
    TextCodec NewDocumentCodec,
    bool TrimTrailingWhitespace = false,
    bool InsertFinalNewline = false,
    bool EnhancedHomeEnd = false,
    bool BackspaceUnindent = false,
    bool AltDragBlock = false,
    bool PspadPairs = false,
    SmartKeyOptions? SmartKeys = null,
    bool ShowLineEndings = false,
    bool LineNumbers = true,
    bool CurrentLine = true,
    bool Links = true,
    bool Minimap = true,
    int RightMargin = 0,
    string FallbackFontFamily = "",
    bool TabsOnly = false,
    bool ScrollPastEnd = false,
    bool CutCopyWholeLine = false,
    bool HideMouseWhileTyping = true,
    bool LinkNeedsCtrl = true,
    int WrapIndent = 0,
    bool MatchBrackets = true,
    bool Emmet = true)
{
    /// <summary>The configured family followed by the fallback families, so a character the main font lacks is drawn with the next one that has it.</summary>
    public string FullFontFamily => FallbackFontFamily.Length == 0 ? FontFamily : FontFamily + ", " + FallbackFontFamily;

    /// <summary>The smart keys for web files that are on for the language of the document.</summary>
    public SmartKeyOptions Smart => SmartKeys ?? new SmartKeyOptions();

    /// <summary>The indentation found in the file replaces the configured one for that document; a language that needs tabs keeps them.</summary>
    public EditorOptions WithIndent(IndentSettings indent) => this with { InsertSpaces = indent.InsertSpaces && !TabsOnly, IndentSize = indent.IndentSize, TabSize = indent.TabSize };

    public IndentSettings Indent => new(InsertSpaces, IndentSize, TabSize);

    public static EditorOptions From(SettingsStack s, string? language = null)
    {
        string lang = language ?? "";
        string? l = lang.Length == 0 || lang == LanguageModes.PlainId ? null : lang;
        bool tabsOnly = lang == LanguageModes.MakefileId; // recipe lines of a makefile must start with a tab, whatever the indentation setting says
        return new EditorOptions(
            TabSize: (int)s.GetInt("editor.tabSize", l),
            IndentSize: (int)s.GetInt("editor.indentSize", l),
            InsertSpaces: !tabsOnly && s.GetBool("editor.insertSpaces", l),
            WordWrap: s.GetString("editor.wordWrap", l) == "on",
            ShowWhitespace: s.GetString("editor.renderWhitespace", l) == "all",
            FontFamily: s.GetString("editor.fontFamily"),
            FontSize: s.GetNumber("editor.fontSize"),
            AutoClosePairs: s.GetBool("editor.autoClosePairs", l),
            ContinueComments: s.GetBool("editor.continueComments", l),
            ConfiguredEol: s.GetString("files.eol", l) switch { "lf" => EolStyle.Lf, "crlf" => EolStyle.CrLf, _ => null },
            NewDocumentCodec: s.GetString("files.encoding", l) switch
            {
                "utf-8-bom" => TextCodec.Utf8Bom,
                "utf-16le" => TextCodec.Utf16LEBom,
                "utf-16be" => TextCodec.Utf16BEBom,
                "windows-1250" => TextCodec.Cp1250,
                "iso-8859-2" => TextCodec.Latin2,
                _ => TextCodec.Utf8,
            },
            TrimTrailingWhitespace: s.GetBool("files.trimTrailingWhitespace", l),
            InsertFinalNewline: s.GetBool("files.insertFinalNewline", l),
            EnhancedHomeEnd: s.GetBool("editor.enhancedHomeEnd", l),
            BackspaceUnindent: s.GetBool("editor.backspaceUnindent", l),
            AltDragBlock: s.GetString("editor.altDragMode") == "block",
            PspadPairs: s.GetString("editor.pairStyle", l) == "pspad",
            SmartKeys: new SmartKeyOptions(
                RenameTag: s.GetBool("editor.smartKeys.renameTag", l),
                CloseTag: s.GetBool("editor.smartKeys.closeTag", l),
                EscapeInString: s.GetBool("editor.smartKeys.escapeInString", l),
                JsonComma: s.GetBool("editor.smartKeys.jsonComma", l),
                PhpArrow: s.GetBool("editor.smartKeys.phpArrow", l),
                SelectVariable: s.GetBool("editor.smartKeys.selectVariable", l),
                SqlEnd: s.GetBool("editor.smartKeys.sqlEnd", l)),
            ShowLineEndings: s.GetBool("editor.renderLineEndings", l),
            LineNumbers: s.GetBool("editor.lineNumbers"),
            CurrentLine: s.GetBool("editor.highlightCurrentLine"),
            Links: s.GetBool("editor.links"),
            Minimap: s.GetBool("editor.minimap"),
            RightMargin: (int)s.GetInt("editor.rightMargin", l),
            FallbackFontFamily: s.GetString("editor.fallbackFontFamily"),
            TabsOnly: tabsOnly,
            ScrollPastEnd: s.GetBool("advanced.scrollPastEnd"),
            CutCopyWholeLine: s.GetBool("advanced.cutCopyWholeLine"),
            HideMouseWhileTyping: s.GetBool("advanced.hideMouseWhileTyping"),
            LinkNeedsCtrl: s.GetBool("advanced.linkNeedsCtrl"),
            WrapIndent: (int)s.GetInt("advanced.wrapIndent"),
            MatchBrackets: s.GetBool("editor.matchBrackets"),
            Emmet: s.GetBool("editor.emmet", l));
    }
}

/// <summary>
/// The fonts Avalonia tries, in order, for a character the first font of a family lacks. Avalonia asks the system for such a character
/// and ignores the other names of the family, so <see cref="Options"/> hands this list to the font manager and the surfaces fill it from <see cref="EditorOptions.FullFontFamily"/>.
/// </summary>
internal sealed class EditorFontFallbacks : IReadOnlyList<FontFallback>
{
    public static readonly EditorFontFallbacks Shared = new();

    /// <summary>The font manager options of the app, read once when the font manager is created.</summary>
    public static FontManagerOptions Options => new() { FontFallbacks = Shared };

    private string _family = "";
    private FontFallback[] _fonts = [];

    /// <summary>Takes the fonts of a comma-separated family. A name that is not installed is left out, as Avalonia would draw it with its default font.</summary>
    public void Use(string family)
    {
        if (family == _family) return;
        _family = family;
        _fonts = [.. family.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(name => FontManager.Current.TryGetGlyphTypeface(new Typeface(name), out var face) && string.Equals(face.FamilyName, name, StringComparison.OrdinalIgnoreCase))
            .Select(name => new FontFallback { FontFamily = new FontFamily(name) })];
    }

    public FontFallback this[int index] => _fonts[index];
    public int Count => _fonts.Length;
    public IEnumerator<FontFallback> GetEnumerator() => ((IEnumerable<FontFallback>)_fonts).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
