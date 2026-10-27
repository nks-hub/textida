using System.Globalization;

namespace NksHub.Textida.Plugins.Json.Schema;

/// <summary>
/// The texts of the schema features in the languages of the editor (cs, en, sk, de; the editor passes the language with the command). A finding of the validator carries its English text and the
/// values it is made of; <see cref="Issue"/> writes it again in the language asked for, and English (or an unknown language, or a code without a text) keeps the English text.
/// </summary>
public sealed record SchemaTexts(
    string SyntaxAt, string Or, string Expected, IReadOnlyDictionary<string, string> TypeNames,
    IReadOnlyDictionary<string, string> Issues, string Type, string Values, string Default, string Deprecated)
{
    private static readonly string[] TypeKeys = ["object", "array", "string", "number", "integer", "boolean", "null"];

    /// <summary>The text of a syntax error: <paramref name="line"/> and <paramref name="column"/> count from 1.</summary>
    public string Syntax(int line, int column, string detail) =>
        SyntaxAt.Replace("{line}", line.ToString(CultureInfo.InvariantCulture)).Replace("{column}", column.ToString(CultureInfo.InvariantCulture)).Replace("{detail}", detail);

    /// <summary>The finding in this language, with its JSON pointer in front when it has one.</summary>
    public string Issue(SchemaIssue issue)
    {
        string text = issue.Detail;
        var args = issue.Args ?? [];
        string key = issue.Code == "oneOf" && args is ["many"] ? "oneOf.many" : issue.Code;
        if (issue.Code == "type" && args.Length > 0)
            text = Expected.Replace("{types}", string.Join(" " + Or + " ", args.Select(a => TypeNames.TryGetValue(a, out var name) ? name : a)));
        else if (Issues.TryGetValue(key, out var format))
        {
            text = format;
            for (int i = 0; i < args.Length; i++) text = text.Replace("{" + i + "}", args[i]);
        }
        return issue.Pointer.Length == 0 ? text : issue.Pointer + ": " + text;
    }

    private static IReadOnlyDictionary<string, string> Types(params string[] names) => TypeKeys.Zip(names).ToDictionary(p => p.First, p => p.Second);

    public static SchemaTexts For(string language) => language switch
    {
        "cs" => new("Chyba syntaxe na řádku {line}, sloupci {column}: {detail}", "nebo", "Očekáváno: {types}.", Types("objekt", "pole", "text", "číslo", "celé číslo", "ano/ne", "null"),
            new Dictionary<string, string>
            {
                ["false"] = "Žádná hodnota tu není povolena.",
                ["enum"] = "Hodnota není mezi povolenými hodnotami.",
                ["const"] = "Hodnota se liší od požadované konstanty.",
                ["anyOf"] = "Hodnota neodpovídá žádné z povolených variant.",
                ["oneOf"] = "Hodnota neodpovídá žádné z povolených variant.",
                ["oneOf.many"] = "Hodnota odpovídá více než jedné variantě.",
                ["not"] = "Hodnota odpovídá schématu, které není povoleno.",
                ["required"] = "Vlastnost „{0}“ je povinná.",
                ["minProperties"] = "Je potřeba alespoň {0} vlastností.",
                ["maxProperties"] = "Nejvýše {0} vlastností je povoleno.",
                ["additionalProperties"] = "Vlastnost „{0}“ tu není povolena.",
                ["propertyNames"] = "Název vlastnosti „{0}“ není povolen.",
                ["minItems"] = "Je potřeba alespoň {0} položek.",
                ["maxItems"] = "Nejvýše {0} položek je povoleno.",
                ["uniqueItems"] = "Položky musí být jedinečné.",
                ["contains"] = "Žádná položka neodpovídá požadovanému schématu.",
                ["minLength"] = "Je potřeba alespoň {0} znaků.",
                ["maxLength"] = "Nejvýše {0} znaků je povoleno.",
                ["pattern"] = "Text neodpovídá vzoru „{0}“.",
                ["minimum"] = "Hodnota musí být alespoň {0}.",
                ["maximum"] = "Hodnota smí být nejvýše {0}.",
                ["exclusiveMinimum"] = "Hodnota musí být větší než {0}.",
                ["exclusiveMaximum"] = "Hodnota musí být menší než {0}.",
                ["multipleOf"] = "Hodnota musí být násobkem {0}.",
                ["folder"] = "Název musí odpovídat názvu složky „{0}“.",
            }, "Typ", "Hodnoty", "Výchozí", "Zastaralé"),
        "sk" => new("Chyba syntaxe na riadku {line}, v stĺpci {column}: {detail}", "alebo", "Očakáva sa: {types}.", Types("objekt", "pole", "text", "číslo", "celé číslo", "áno/nie", "null"),
            new Dictionary<string, string>
            {
                ["false"] = "Žiadna hodnota tu nie je povolená.",
                ["enum"] = "Hodnota nie je medzi povolenými hodnotami.",
                ["const"] = "Hodnota sa líši od požadovanej konštanty.",
                ["anyOf"] = "Hodnota nezodpovedá žiadnemu z povolených variantov.",
                ["oneOf"] = "Hodnota nezodpovedá žiadnemu z povolených variantov.",
                ["oneOf.many"] = "Hodnota zodpovedá viac ako jednému variantu.",
                ["not"] = "Hodnota zodpovedá schéme, ktorá nie je povolená.",
                ["required"] = "Vlastnosť „{0}“ je povinná.",
                ["minProperties"] = "Vyžaduje sa aspoň {0} vlastností.",
                ["maxProperties"] = "Povolených je najviac {0} vlastností.",
                ["additionalProperties"] = "Vlastnosť „{0}“ tu nie je povolená.",
                ["propertyNames"] = "Názov vlastnosti „{0}“ nie je povolený.",
                ["minItems"] = "Vyžaduje sa aspoň {0} položiek.",
                ["maxItems"] = "Povolených je najviac {0} položiek.",
                ["uniqueItems"] = "Položky musia byť jedinečné.",
                ["contains"] = "Žiadna položka nezodpovedá požadovanej schéme.",
                ["minLength"] = "Vyžaduje sa aspoň {0} znakov.",
                ["maxLength"] = "Povolených je najviac {0} znakov.",
                ["pattern"] = "Text nezodpovedá vzoru „{0}“.",
                ["minimum"] = "Hodnota musí byť aspoň {0}.",
                ["maximum"] = "Hodnota môže byť najviac {0}.",
                ["exclusiveMinimum"] = "Hodnota musí byť väčšia ako {0}.",
                ["exclusiveMaximum"] = "Hodnota musí byť menšia ako {0}.",
                ["multipleOf"] = "Hodnota musí byť násobkom {0}.",
                ["folder"] = "Názov musí zodpovedať názvu priečinka „{0}“.",
            }, "Typ", "Hodnoty", "Predvolené", "Zastarané"),
        "de" => new("Syntaxfehler in Zeile {line}, Spalte {column}: {detail}", "oder", "Erwartet: {types}.", Types("Objekt", "Array", "Text", "Zahl", "ganze Zahl", "Wahrheitswert", "null"),
            new Dictionary<string, string>
            {
                ["false"] = "Hier ist kein Wert erlaubt.",
                ["enum"] = "Der Wert gehört nicht zu den erlaubten Werten.",
                ["const"] = "Der Wert weicht von der vorgeschriebenen Konstante ab.",
                ["anyOf"] = "Der Wert passt zu keiner der erlaubten Alternativen.",
                ["oneOf"] = "Der Wert passt zu keiner der erlaubten Alternativen.",
                ["oneOf.many"] = "Der Wert passt zu mehr als einer Alternative.",
                ["not"] = "Der Wert entspricht einem Schema, das nicht erlaubt ist.",
                ["required"] = "Die Eigenschaft „{0}“ ist erforderlich.",
                ["minProperties"] = "Mindestens {0} Eigenschaften sind erforderlich.",
                ["maxProperties"] = "Höchstens {0} Eigenschaften sind erlaubt.",
                ["additionalProperties"] = "Die Eigenschaft „{0}“ ist hier nicht erlaubt.",
                ["propertyNames"] = "Der Eigenschaftsname „{0}“ ist nicht erlaubt.",
                ["minItems"] = "Mindestens {0} Elemente sind erforderlich.",
                ["maxItems"] = "Höchstens {0} Elemente sind erlaubt.",
                ["uniqueItems"] = "Die Elemente müssen eindeutig sein.",
                ["contains"] = "Kein Element entspricht dem geforderten Schema.",
                ["minLength"] = "Mindestens {0} Zeichen sind erforderlich.",
                ["maxLength"] = "Höchstens {0} Zeichen sind erlaubt.",
                ["pattern"] = "Der Text passt nicht zum Muster „{0}“.",
                ["minimum"] = "Der Wert muss mindestens {0} sein.",
                ["maximum"] = "Der Wert darf höchstens {0} sein.",
                ["exclusiveMinimum"] = "Der Wert muss größer als {0} sein.",
                ["exclusiveMaximum"] = "Der Wert muss kleiner als {0} sein.",
                ["multipleOf"] = "Der Wert muss ein Vielfaches von {0} sein.",
                ["folder"] = "Der Name muss dem Ordnernamen „{0}“ entsprechen.",
            }, "Typ", "Werte", "Standard", "Veraltet"),
        _ => new("Syntax error at line {line}, column {column}: {detail}", "or", "Expected {types}.", Types("object", "array", "string", "number", "integer", "boolean", "null"),
            new Dictionary<string, string>(), "Type", "Values", "Default", "Deprecated"),
    };
}
