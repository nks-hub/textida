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
    TextCodec NewDocumentCodec)
{
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
            });
    }
}
