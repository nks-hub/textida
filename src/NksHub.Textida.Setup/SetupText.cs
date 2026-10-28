using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class SetupStringsJson : JsonSerializerContext;

/// <summary>Keys of the texts of the setup, one embedded <c>Strings/{tag}.json</c> per language. Placeholders are named, <c>{name}</c>.</summary>
internal static class S
{
    public const string AppTitle = "app.title";
    public const string Language = "lang.label";
    public const string Next = "button.next";
    public const string Back = "button.back";
    public const string Cancel = "button.cancel";
    public const string Install = "button.install";
    public const string Update = "button.update";
    public const string Finish = "button.finish";
    public const string Close = "button.close";
    public const string Retry = "button.retry";
    public const string Browse = "button.browse";
    public const string Uninstall = "button.uninstall";
    public const string WelcomeTitle = "welcome.title";
    public const string WelcomeIntro = "welcome.intro";
    public const string WelcomeInstalled = "welcome.installed";
    public const string LicenceTitle = "licence.title";
    public const string LicenceAccept = "licence.accept";
    public const string OptionsTitle = "options.title";
    public const string OptionsDir = "options.dir";
    public const string OptionsStartMenu = "options.startMenu";
    public const string OptionsDesktop = "options.desktop";
    public const string OptionsMenu = "options.menu";
    public const string OptionsAssoc = "options.assoc";
    public const string OptionsAssocAll = "options.assocAll";
    public const string OptionsAssocNone = "options.assocNone";
    public const string OptionsLaunch = "options.launch";
    public const string OptionsDirNotEmpty = "options.dirNotEmpty";
    public const string ProgressInstall = "progress.install";
    public const string ProgressUpdate = "progress.update";
    public const string StagePreparing = "stage.preparing";
    public const string StageFiles = "stage.files";
    public const string StageSwitching = "stage.switching";
    public const string StageIntegration = "stage.integration";
    public const string StageFinishing = "stage.finishing";
    public const string DoneInstall = "done.install";
    public const string DoneUpdate = "done.update";
    public const string DoneInstalled = "done.installed";
    public const string DoneUpdated = "done.updated";
    public const string DoneSwitched = "done.switched";
    public const string DoneWarning = "done.warning";
    public const string DoneLaunch = "done.launch";
    public const string ErrorTitle = "error.title";
    public const string ErrorRunning = "error.running";
    public const string ErrorBadPackage = "error.badPackage";
    public const string ErrorUnknownDir = "error.unknownDir";
    public const string ErrorNotInstalled = "error.notInstalled";
    public const string ErrorNoPrevious = "error.noPrevious";
    public const string ErrorBadStaging = "error.badStaging";
    public const string ErrorInvalidDir = "error.invalidDir";
    public const string ErrorFailed = "error.failed";
    public const string ErrorNoPackage = "error.noPackage";
    public const string UninstallTitle = "uninstall.title";
    public const string UninstallQuestion = "uninstall.question";
    public const string UninstallKeep = "uninstall.keep";
    public const string UninstallDeleteSettings = "uninstall.deleteSettings";
    public const string UninstallProgress = "uninstall.progress";
    public const string UninstallDone = "uninstall.done";
    public const string UninstallDoneAll = "uninstall.doneAll";
    public const string MenuLabel = "menu.label";
}

/// <summary>The texts of the setup in one of the four languages. The language of the system is the first choice; the user can switch.</summary>
internal sealed class SetupText(string language)
{
    public static readonly IReadOnlyList<string> Languages = ["cs", "en", "sk", "de"];

    /// <summary>The names of the languages in themselves; they are the same whatever language is shown.</summary>
    public static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string> { ["cs"] = "Čeština", ["en"] = "English", ["sk"] = "Slovenčina", ["de"] = "Deutsch" };

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = LoadAll();

    public string Language { get; } = Pick(language);

    /// <summary>The language of the system when the setup has one, else English.</summary>
    public static string Detect() => Pick(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);

    public static string Pick(string? tag) => tag is not null && Catalogs.ContainsKey(tag.ToLowerInvariant()) ? tag.ToLowerInvariant() : "en";

    internal static IReadOnlyDictionary<string, string> Catalog(string tag) => Catalogs[tag];

    public string Get(string key, params (string Name, object Value)[] args)
    {
        var template = Catalogs[Language].TryGetValue(key, out var own) ? own : Catalogs["en"].GetValueOrDefault(key, key);
        foreach (var (name, value) in args) template = template.Replace("{" + name + "}", Convert.ToString(value, CultureInfo.InvariantCulture));
        return template;
    }

    /// <summary>The sentence for a refusal or failure of the setup logic.</summary>
    public string Describe(SetupException error) => Get(error.Error switch
    {
        SetupError.Running => S.ErrorRunning,
        SetupError.BadPackage => S.ErrorBadPackage,
        SetupError.UnknownDir => S.ErrorUnknownDir,
        SetupError.NotInstalled => S.ErrorNotInstalled,
        SetupError.NoPrevious => S.ErrorNoPrevious,
        SetupError.BadStaging => S.ErrorBadStaging,
        SetupError.InvalidDir => S.ErrorInvalidDir,
        _ => S.ErrorFailed,
    }, ("detail", error.Detail));

    public string Stage(SetupStage stage) => Get(stage switch
    {
        SetupStage.Preparing => S.StagePreparing,
        SetupStage.Files => S.StageFiles,
        SetupStage.Switching => S.StageSwitching,
        SetupStage.Integration => S.StageIntegration,
        _ => S.StageFinishing,
    });

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadAll()
    {
        var assembly = typeof(SetupText).Assembly;
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var tag in Languages)
        {
            var name = $"NksHub.Textida.Setup.Strings.{tag}.json";
            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException($"string catalogue {name} is not embedded");
            result[tag] = JsonSerializer.Deserialize(stream, SetupStringsJson.Default.DictionaryStringString) ?? throw new InvalidDataException($"empty string catalogue {name}");
        }
        return result;
    }
}
