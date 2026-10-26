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
    string FallbackFontFamily = "")
{
    /// <summary>The configured family followed by the fallback families, so a character the main font lacks is drawn with the next one that has it.</summary>
    public string FullFontFamily => FallbackFontFamily.Length == 0 ? FontFamily : FontFamily + ", " + FallbackFontFamily;

    /// <summary>The smart keys for web files that are on for the language of the document.</summary>
    public SmartKeyOptions Smart => SmartKeys ?? new SmartKeyOptions();

    /// <summary>The indentation found in the file replaces the configured one for that document.</summary>
    public EditorOptions WithIndent(IndentSettings indent) => this with { InsertSpaces = indent.InsertSpaces, IndentSize = indent.IndentSize, TabSize = indent.TabSize };

    public IndentSettings Indent => new(InsertSpaces, IndentSize, TabSize);

    public static EditorOptions From(SettingsStack s, string? language = null)
    {
        string lang = language ?? "";
        string? l = lang.Length == 0 || lang == LanguageModes.PlainId ? null : lang;
        return new EditorOptions(
            TabSize: (int)s.GetInt("editor.tabSize", l),
            IndentSize: (int)s.GetInt("editor.indentSize", l),
            InsertSpaces: s.GetBool("editor.insertSpaces", l),
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
            FallbackFontFamily: s.GetString("editor.fallbackFontFamily"));
    }
}
