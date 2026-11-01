using System.Text;
using WeCantSpell.Hunspell;

namespace NksHub.Textida.App.Services;

/// <summary>A dictionary LibreOffice publishes: its code, its name and where its two files are in the repository of the dictionaries.</summary>
internal sealed record DownloadableDictionary(string Code, string Name, string Folder, string BaseName)
{
    public const string Host = "raw.githubusercontent.com";

    public Uri Uri(string extension) => new($"https://{Host}/LibreOffice/dictionaries/master/{Folder}/{BaseName}.{extension}");
}

/// <summary>
/// The Hunspell dictionary the user chose and their own words (PZ.3.1). The editor ships no dictionary: they are files of the user, or files downloaded from the dictionaries of LibreOffice after the user agreed,
/// each under its own licence. A dictionary is a <c>.dic</c> file with the <c>.aff</c> file of the same name beside it.
/// </summary>
internal sealed class SpellChecker
{
    private readonly WordList _list;
    private readonly HashSet<string> _personal;

    private SpellChecker(WordList list, HashSet<string> personal, string path)
    {
        _list = list;
        _personal = personal;
        Path = path;
    }

    public string Path { get; }

    /// <summary>The checker for the dictionary at <paramref name="dicPath"/>; null with the reason in <paramref name="problem"/> when the files are missing or are not a dictionary.</summary>
    public static SpellChecker? Load(string dicPath, IEnumerable<string> personal, out string? problem)
    {
        problem = null;
        try
        {
            if (!File.Exists(dicPath)) { problem = "The dictionary file does not exist."; return null; }
            if (!File.Exists(System.IO.Path.ChangeExtension(dicPath, ".aff"))) { problem = "The affix file (.aff) is not beside the dictionary."; return null; }
            return new SpellChecker(WordList.CreateFromFiles(dicPath), new HashSet<string>(personal.Select(w => w.ToLowerInvariant()), StringComparer.Ordinal), dicPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException)
        {
            problem = e.Message;
            return null;
        }
    }

    public bool Check(string word) => _list.Check(word) || _personal.Contains(word.ToLowerInvariant());

    public IReadOnlyList<string> Suggest(string word) => [.. _list.Suggest(word)];

    public void AddPersonal(string word) => _personal.Add(word.ToLowerInvariant());
}

/// <summary>Where the spelling dictionaries and the personal words live in the profile, and the dictionaries that can be downloaded.</summary>
internal sealed class SpellFiles(string profile)
{
    public static readonly IReadOnlyList<DownloadableDictionary> Downloadable =
    [
        new("cs_CZ", "Čeština (cs_CZ)", "cs_CZ", "cs_CZ"),
        new("sk_SK", "Slovenčina (sk_SK)", "sk_SK", "sk_SK"),
        new("en_US", "English (en_US)", "en", "en_US"),
        new("en_GB", "English (en_GB)", "en", "en_GB"),
        new("de_DE", "Deutsch (de_DE)", "de", "de_DE_frami"),
        new("pl_PL", "Polski (pl_PL)", "pl_PL", "pl_PL"),
    ];

    public const long MaxDictionaryBytes = 24L << 20;

    public string DictionaryFolder => System.IO.Path.Combine(profile, "dictionaries");

    public string PersonalPath => System.IO.Path.Combine(profile, "spell-words.txt");

    public string DictionaryPath(DownloadableDictionary d) => System.IO.Path.Combine(DictionaryFolder, d.Code + ".dic");

    public IReadOnlyList<string> PersonalWords()
    {
        try { return File.Exists(PersonalPath) ? File.ReadAllLines(PersonalPath, Encoding.UTF8).Where(l => l.Trim().Length > 0).ToList() : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    public void AddPersonal(string word)
    {
        Directory.CreateDirectory(profile);
        File.AppendAllText(PersonalPath, word.Trim() + "\n", new UTF8Encoding(false));
    }

    /// <summary>Writes the two files of a downloaded dictionary into the profile (each through a temporary file) and returns the path of the <c>.dic</c> file.</summary>
    public string Save(DownloadableDictionary dictionary, byte[] dic, byte[] aff)
    {
        Directory.CreateDirectory(DictionaryFolder);
        string path = DictionaryPath(dictionary);
        foreach (var (extension, bytes) in new[] { ("aff", aff), ("dic", dic) })
        {
            string target = System.IO.Path.ChangeExtension(path, extension), temp = target + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, target, overwrite: true);
        }
        return path;
    }
}
