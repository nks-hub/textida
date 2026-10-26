using System.Text.Json.Serialization;

namespace NksHub.Textida.Plugins.Json;

[JsonSerializable(typeof(string))]
internal sealed partial class PluginJsonStrings : JsonSerializerContext;

/// <summary>Texts the plugin shows through notifications, in the languages of the editor (cs, en, sk, de); the editor passes the language with the command.</summary>
internal sealed record Messages(string Title, string Stale, string Unchanged, string Overlap, string DoneOne, string DoneMany, string TooLargeText, string InvalidBytesText, string SyntaxText, string RangeText)
{
    public string Done(int count) => count == 1 ? DoneOne : DoneMany.Replace("{count}", count.ToString());

    public string TooLarge(long size, long max) => TooLargeText.Replace("{size}", size.ToString()).Replace("{max}", max.ToString());

    public string InvalidBytes(int range, int ranges) => Prefix(range, ranges) + InvalidBytesText;

    public string Syntax(int range, int ranges, JsonFormatter.SyntaxError e) =>
        Prefix(range, ranges) + SyntaxText.Replace("{line}", e.Line.ToString()).Replace("{column}", e.Column.ToString()).Replace("{detail}", e.Detail);

    private string Prefix(int range, int ranges) => ranges > 1 ? RangeText.Replace("{n}", range.ToString()).Replace("{count}", ranges.ToString()) + " " : "";

    public static Messages For(string language) => language switch
    {
        "cs" => new("Formátovat JSON", "Dokument se mezitím změnil. Spusťte příkaz znovu.", "JSON už je naformátovaný.", "Označené části se překrývají.",
            "JSON byl naformátován.", "Naformátováno částí: {count}.", "Text k formátování má {size} B, limit je {max} B. Označte menší část.",
            "Text obsahuje bajty, které nejsou v kódování souboru platné. Nic se nezměnilo.", "Chyba syntaxe JSON na řádku {line}, sloupci {column}: {detail}", "Část {n} z {count}:"),
        "sk" => new("Formátovať JSON", "Dokument sa medzitým zmenil. Spustite príkaz znova.", "JSON je už naformátovaný.", "Označené časti sa prekrývajú.",
            "JSON bol naformátovaný.", "Naformátovaných častí: {count}.", "Text na formátovanie má {size} B, limit je {max} B. Označte menšiu časť.",
            "Text obsahuje bajty, ktoré nie sú v kódovaní súboru platné. Nič sa nezmenilo.", "Chyba syntaxe JSON na riadku {line}, stĺpci {column}: {detail}", "Časť {n} z {count}:"),
        "de" => new("JSON formatieren", "Das Dokument hat sich inzwischen geändert. Führen Sie den Befehl erneut aus.", "Das JSON ist bereits formatiert.", "Die markierten Bereiche überlappen sich.",
            "JSON wurde formatiert.", "{count} Bereiche formatiert.", "Der zu formatierende Text hat {size} Bytes, das Limit ist {max} Bytes. Markieren Sie einen kleineren Teil.",
            "Der Text enthält Bytes, die in der Dateikodierung nicht gültig sind. Nichts wurde geändert.", "JSON-Syntaxfehler in Zeile {line}, Spalte {column}: {detail}", "Bereich {n} von {count}:"),
        _ => new("Format JSON", "The document changed in the meantime. Run the command again.", "The JSON is already formatted.", "The selected ranges overlap.",
            "JSON formatted.", "{count} ranges formatted.", "The text to format is {size} bytes; the limit is {max} bytes. Select a smaller part.",
            "The text contains bytes that are not valid in the encoding of the file. Nothing was changed.", "JSON syntax error at line {line}, column {column}: {detail}", "Range {n} of {count}:"),
    };
}
