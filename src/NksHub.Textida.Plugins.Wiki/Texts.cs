namespace NksHub.Textida.Plugins.Wiki;

/// <summary>Texts the plugin shows, in the languages of the editor; the editor passes the language on the command line. Placeholders are written <c>{name}</c>.</summary>
public static class Texts
{
    // columns: cs, en, sk, de
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["askBaseTitle"] = ["Wiki NKS IS", "NKS IS wiki", "Wiki NKS IS", "NKS-IS-Wiki"],
        ["askBaseLabel"] = ["Adresa wiki (začíná http:// nebo https://):", "Address of the wiki (starts with http:// or https://):", "Adresa wiki (začína http:// alebo https://):", "Adresse des Wikis (beginnt mit http:// oder https://):"],
        ["askTokenTitle"] = ["Wiki NKS IS: klíč API", "NKS IS wiki: API key", "Wiki NKS IS: kľúč API", "NKS-IS-Wiki: API-Schlüssel"],
        ["askTokenLabel"] = ["Klíč API (uloží se do úložiště přihlašovacích údajů systému):", "API key (kept in the credential store of the system):", "Kľúč API (uloží sa do úložiska prihlasovacích údajov systému):", "API-Schlüssel (wird im Anmeldeinformationsspeicher des Systems abgelegt):"],
        ["noBaseUrl"] = ["Adresa wiki nebyla zadána, nic se neodeslalo.", "No wiki address was entered, nothing was sent.", "Adresa wiki nebola zadaná, nič sa neodoslalo.", "Es wurde keine Wiki-Adresse eingegeben, nichts wurde gesendet."],
        ["badBaseUrl"] = ["Adresa wiki musí být úplná adresa http:// nebo https://.", "The wiki address must be a full http:// or https:// address.", "Adresa wiki musí byť úplná adresa http:// alebo https://.", "Die Wiki-Adresse muss eine vollständige http://- oder https://-Adresse sein."],
        ["noToken"] = ["Klíč API nebyl zadán, nic se neodeslalo.", "No API key was entered, nothing was sent.", "Kľúč API nebol zadaný, nič sa neodoslalo.", "Es wurde kein API-Schlüssel eingegeben, nichts wurde gesendet."],
        ["rejected"] = ["Klíč API chybí nebo je neplatný. Uložený klíč jsem zapomněl, příště se zeptám znovu.", "The API key is missing or invalid. The stored key was dropped; you will be asked again.", "Kľúč API chýba alebo je neplatný. Uložený kľúč som zabudol, nabudúce sa spýtam znova.", "Der API-Schlüssel fehlt oder ist ungültig. Der gespeicherte Schlüssel wurde verworfen, Sie werden erneut gefragt."],
        ["rateLimit"] = ["Wiki přijímá nejvýš 60 požadavků za minutu na klíč. Zkuste to za chvíli.", "The wiki accepts at most 60 requests per minute per key. Try again in a moment.", "Wiki prijíma najviac 60 požiadaviek za minútu na kľúč. Skúste to o chvíľu.", "Das Wiki nimmt höchstens 60 Anfragen pro Minute und Schlüssel an. Versuchen Sie es gleich noch einmal."],
        ["unreachable"] = ["Wiki je nedostupná: {reason}", "The wiki is not reachable: {reason}", "Wiki je nedostupná: {reason}", "Das Wiki ist nicht erreichbar: {reason}"],
        ["failed"] = ["Wiki odpověděla neočekávaně (HTTP {status}).", "The wiki answered unexpectedly (HTTP {status}).", "Wiki odpovedala neočakávane (HTTP {status}).", "Das Wiki hat unerwartet geantwortet (HTTP {status})."],
        ["notFound"] = ["Stránka {slug} neexistuje.", "The page {slug} does not exist.", "Stránka {slug} neexistuje.", "Die Seite {slug} existiert nicht."],
        ["conflict"] = ["Stránka má jinou revizi, než ze které se upravovalo.", "The page is at another revision than the one that was edited.", "Stránka má inú revíziu, než z ktorej sa upravovalo.", "Die Seite hat eine andere Revision als die bearbeitete."],
        ["emptyQuery"] = ["Zadejte slova k hledání.", "Enter the words to search for.", "Zadajte slová na hľadanie.", "Geben Sie die Suchbegriffe ein."],
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
