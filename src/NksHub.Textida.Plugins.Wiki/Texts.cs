namespace NksHub.Textida.Plugins.Wiki;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language on the command line. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["askBaseTitle"] = ["Wiki NKS IS", "NKS IS wiki", "Wiki NKS IS", "NKS-IS-Wiki"],
        ["askBaseLabel"] = ["Adresa wiki (začíná http:// nebo https://):", "Address of the wiki (starts with http:// or https://):", "Adresa wiki (začína http:// alebo https://):", "Adresse des Wikis (beginnt mit http:// oder https://):"],
        ["askTokenTitle"] = ["Wiki NKS IS: přístupový token", "NKS IS wiki: access token", "Wiki NKS IS: prístupový token", "NKS-IS-Wiki: Zugriffstoken"],
        ["askTokenLabel"] = ["Přístupový token (uloží se do úložiště přihlašovacích údajů systému):", "Access token (kept in the credential store of the system):", "Prístupový token (uloží sa do úložiska prihlasovacích údajov systému):", "Zugriffstoken (wird im Anmeldeinformationsspeicher des Systems abgelegt):"],
        ["noBaseUrl"] = ["Adresa wiki nebyla zadána, nic se neodeslalo.", "No wiki address was entered, nothing was sent.", "Adresa wiki nebola zadaná, nič sa neodoslalo.", "Es wurde keine Wiki-Adresse eingegeben, nichts wurde gesendet."],
        ["badBaseUrl"] = ["Adresa wiki musí být úplná adresa http:// nebo https://.", "The wiki address must be a full http:// or https:// address.", "Adresa wiki musí byť úplná adresa http:// alebo https://.", "Die Wiki-Adresse muss eine vollständige http://- oder https://-Adresse sein."],
        ["noToken"] = ["Přístupový token nebyl zadán, nic se neodeslalo.", "No access token was entered, nothing was sent.", "Prístupový token nebol zadaný, nič sa neodoslalo.", "Es wurde kein Zugriffstoken eingegeben, nichts wurde gesendet."],
        ["rejected"] = ["Wiki token nepřijala. Uložený token jsem zapomněl, příště se zeptám znovu.", "The wiki did not accept the token. The stored token was dropped; you will be asked again.", "Wiki token neprijala. Uložený token som zabudol, nabudúce sa spýtam znova.", "Das Wiki hat das Token nicht akzeptiert. Das gespeicherte Token wurde verworfen, Sie werden erneut gefragt."],
        ["unreachable"] = ["Wiki je nedostupná: {reason}", "The wiki is not reachable: {reason}", "Wiki je nedostupná: {reason}", "Das Wiki ist nicht erreichbar: {reason}"],
        ["failed"] = ["Wiki odpověděla neočekávaně (HTTP {status}).", "The wiki answered unexpectedly (HTTP {status}).", "Wiki odpovedala neočakávane (HTTP {status}).", "Das Wiki hat unerwartet geantwortet (HTTP {status})."],
        ["notFound"] = ["Stránka {slug} neexistuje.", "The page {slug} does not exist.", "Stránka {slug} neexistuje.", "Die Seite {slug} existiert nicht."],
        ["conflict"] = ["Stránka má jinou revizi, než ze které se upravovalo.", "The page is at another revision than the one that was edited.", "Stránka má inú revíziu, než z ktorej sa upravovalo.", "Die Seite hat eine andere Revision als die bearbeitete."],
        ["badAddress"] = ["Adresa {uri} neodpovídá tvaru nksis://wiki/stránka.", "The address {uri} does not match nksis://wiki/page.", "Adresa {uri} nezodpovedá tvaru nksis://wiki/stránka.", "Die Adresse {uri} entspricht nicht nksis://wiki/Seite."],
        ["tooLarge"] = ["Stránka má víc než {max} B.", "The page is larger than {max} bytes.", "Stránka má viac než {max} B.", "Die Seite ist größer als {max} Bytes."],
        ["notText"] = ["Obsah stránky není platný UTF-8.", "The content is not valid UTF-8.", "Obsah stránky nie je platný UTF-8.", "Der Inhalt ist kein gültiges UTF-8."],
    };

    private static int Column(string language) => language switch { "cs" => 0, "sk" => 2, "de" => 3, _ => 1 };

    public static string Get(string language, string key, params (string Name, string Value)[] arguments)
    {
        var text = Table[key][Column(language)];
        foreach (var (name, value) in arguments) text = text.Replace("{" + name + "}", value);
        return text;
    }
}
