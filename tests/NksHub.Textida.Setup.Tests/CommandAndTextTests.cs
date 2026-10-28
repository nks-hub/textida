using System.Text.Json;
using System.Text.RegularExpressions;
using NksHub.Textida.Install;
using NksHub.Textida.Setup;

namespace NksHub.Textida.Setup.Tests;

/// <summary>The command line of the setup, its four languages, and the tokens of the D5 look it shares with the editor.</summary>
public class CommandAndTextTests
{
    // ---- command line ----

    [Fact]
    public void A_silent_install_names_the_folder_and_the_choices()
    {
        var c = SetupCommand.Parse(["--silent", "--dir", @"D:\Apps\Textida", "--no-menu", "--no-start-menu", "--desktop", "--assoc", ".log,.md", "--launch", "--lang", "de"]);

        Assert.Null(c.Error);
        Assert.Equal(SetupMode.Install, c.Mode);
        Assert.True(c.Silent);
        Assert.True(c.NeedsNoWindow);
        Assert.Equal(@"D:\Apps\Textida", c.Dir);
        Assert.False(c.ContextMenu);
        Assert.False(c.StartMenu);
        Assert.True(c.Desktop);
        Assert.Equal([".log", ".md"], c.Extensions);
        Assert.True(c.Launch);
        Assert.Equal("de", c.Language);
    }

    [Fact]
    public void A_choice_the_line_does_not_name_stays_open()
    {
        var c = SetupCommand.Parse(["--silent"]);
        Assert.Null(c.Dir);
        Assert.Null(c.ContextMenu);
        Assert.Null(c.StartMenu);
        Assert.Null(c.Desktop);
        Assert.Null(c.Launch);
        Assert.Null(c.Extensions);
        Assert.False(SetupCommand.Parse([]).NeedsNoWindow);
    }

    [Fact]
    public void Values_can_follow_an_equals_sign_and_the_options_are_not_case_sensitive()
    {
        var c = SetupCommand.Parse(["--SILENT", "--dir=C:\\a b\\c", "--assoc=all", "--lang=CS"]);
        Assert.Null(c.Error);
        Assert.Equal(@"C:\a b\c", c.Dir);
        Assert.Equal(ShellIntegration.Extensions, c.Extensions);
        Assert.Equal("CS", c.Language);
    }

    [Theory]
    [InlineData("--assoc", "none")]
    [InlineData("--no-assoc", null)]
    public void No_associations_can_be_asked_for_two_ways(string option, string? value)
    {
        var c = SetupCommand.Parse(value is null ? [option] : [option, value]);
        Assert.Empty(c.Extensions!);
    }

    [Fact]
    public void An_extension_is_taken_with_or_without_the_dot_and_listed_once()
    {
        Assert.Equal([".txt", ".md"], SetupCommand.Parse(["--assoc", "TXT,.md,txt"]).Extensions);
    }

    [Theory]
    [InlineData("--frobnicate")]
    [InlineData("--dir")]
    [InlineData("--assoc", ".exe")]
    [InlineData("--lang", "fr")]
    [InlineData("--wait-pid", "abc")]
    [InlineData("--apply-update")]
    public void A_wrong_line_is_reported_and_not_acted_on(params string[] args)
    {
        Assert.NotNull(SetupCommand.Parse(args).Error);
    }

    [Fact]
    public void The_other_modes_are_recognised_and_need_no_window()
    {
        var uninstall = SetupCommand.Parse(["--uninstall", "--delete-settings"]);
        Assert.Equal(SetupMode.Uninstall, uninstall.Mode);
        Assert.True(uninstall.DeleteSettings);
        Assert.False(uninstall.NeedsNoWindow); // the question is asked in a window unless --silent is given
        Assert.True(SetupCommand.Parse(["--uninstall", "--silent"]).NeedsNoWindow);

        var update = SetupCommand.Parse(["--apply-update", @"C:\x\Textida.new", "--dir", @"C:\x\Textida", "--wait-pid", "1234", "--restart", "--temp-copy", "--uninstall-key", @"HKCU:\Software\Test\Uninstall"]);
        Assert.Equal(SetupMode.ApplyUpdate, update.Mode);
        Assert.Equal(@"C:\x\Textida.new", update.Staged);
        Assert.Equal(1234, update.WaitPid);
        Assert.True(update.Restart);
        Assert.True(update.TempCopy);
        Assert.Equal(@"Software\Test\Uninstall", update.UninstallKey);
        Assert.True(update.NeedsNoWindow);

        Assert.Equal(SetupMode.Rollback, SetupCommand.Parse(["--rollback"]).Mode);
        Assert.Equal(SetupMode.Check, SetupCommand.Parse(["--check"]).Mode);
        Assert.Equal(SetupMode.Help, SetupCommand.Parse(["-h"]).Mode);
        Assert.Equal(SetupMode.Help, SetupCommand.Parse(["/?"]).Mode);
    }

    // ---- texts ----

    private static readonly string[] Tags = ["cs", "en", "sk", "de"];

    public static IEnumerable<object[]> Languages() => Tags.Select(t => new object[] { t });

    private static IEnumerable<string> KeyConstants() =>
        typeof(S).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void The_four_languages_ship_and_the_names_are_written_in_themselves()
    {
        Assert.Equal(Tags, SetupText.Languages);
        Assert.Equal(["Čeština", "English", "Slovenčina", "Deutsch"], Tags.Select(t => SetupText.Names[t]));
    }

    [Theory, MemberData(nameof(Languages))]
    public void Every_language_has_exactly_the_keys_of_the_code_and_none_is_empty(string tag)
    {
        var catalog = SetupText.Catalog(tag);
        Assert.Equal(KeyConstants().Order(), catalog.Keys.Order());
        Assert.All(catalog, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), $"{tag}:{kv.Key}"));
    }

    [Theory, MemberData(nameof(Languages))]
    public void The_placeholders_are_the_same_in_every_language(string tag)
    {
        static HashSet<string> Names(string text) => Regex.Matches(text, @"\{(\w+)\}").Select(m => m.Groups[1].Value).ToHashSet();
        var english = SetupText.Catalog("en");
        foreach (var (key, text) in SetupText.Catalog(tag)) Assert.True(Names(english[key]).SetEquals(Names(text)), $"{tag}:{key}: {text}");
    }

    [Fact]
    public void A_text_fills_its_placeholders_and_an_unknown_language_is_english()
    {
        Assert.Equal("Textida 1.2.3 is installed in C:\\x.", new SetupText("en").Get(S.DoneInstalled, ("version", "1.2.3"), ("dir", "C:\\x")));
        Assert.Equal("Textida 1.2.3 je nainstalovaná v C:\\x.", new SetupText("cs").Get(S.DoneInstalled, ("version", "1.2.3"), ("dir", "C:\\x")));
        Assert.Equal("en", new SetupText("fr").Language);
        Assert.Equal("de", SetupText.Pick("DE"));
        Assert.Equal("en", SetupText.Pick(null));
        Assert.Equal("no.such.key", new SetupText("cs").Get("no.such.key"));
    }

    [Theory, MemberData(nameof(Languages))]
    public void Every_error_and_stage_has_a_sentence_in_every_language(string tag)
    {
        var text = new SetupText(tag);
        foreach (var error in Enum.GetValues<SetupError>())
        {
            var sentence = text.Describe(new SetupException(error, "<detail>"));
            Assert.DoesNotContain("error.", sentence);
            if (error != SetupError.Running && error != SetupError.BadPackage) Assert.Contains("<detail>", sentence);
        }
        foreach (var stage in Enum.GetValues<SetupStage>()) Assert.DoesNotContain("stage.", text.Stage(stage));
    }

    [Theory, MemberData(nameof(Languages))]
    public void The_menu_entry_reads_the_same_as_in_the_editor(string tag)
    {
        var app = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(Repo.Root, "src", "NksHub.Textida.App", "Strings", tag + ".json")))!;
        Assert.Equal(app["shell.menu.label"], new SetupText(tag).Get(S.MenuLabel));
    }

    // ---- the D5 look ----

    [Fact]
    public void The_tokens_of_the_setup_have_the_values_of_the_editor()
    {
        static Dictionary<string, string> Tokens(string axaml, string theme)
        {
            var start = axaml.IndexOf($"x:Key=\"{theme}\"", StringComparison.Ordinal);
            var end = axaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
            return Regex.Matches(axaml[start..end], @"x:Key=""(\w+)"">(#[0-9A-Fa-f]+)<").ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        }
        var editor = File.ReadAllText(Path.Combine(Repo.Root, "src", "NksHub.Textida.App", "App.axaml"));
        var setup = File.ReadAllText(Path.Combine(Repo.Root, "src", "NksHub.Textida.Setup", "SetupApp.axaml"));
        foreach (var theme in new[] { "Light", "Dark" })
        {
            var mine = Tokens(setup, theme);
            var theirs = Tokens(editor, theme);
            Assert.True(mine.Count >= 10, theme);
            foreach (var (name, value) in mine) Assert.True(theirs.TryGetValue(name, out var expected) && expected == value, $"{theme} {name}: {value} / {(theirs.TryGetValue(name, out var e) ? e : "missing")}");
        }
    }

    [Fact]
    public void The_setup_has_the_application_icon_of_the_editor()
    {
        var setup = File.ReadAllText(Path.Combine(Repo.Root, "src", "NksHub.Textida.Setup", "NksHub.Textida.Setup.csproj"));
        var editor = File.ReadAllText(Path.Combine(Repo.Root, "src", "NksHub.Textida.App", "NksHub.Textida.App.csproj"));
        Assert.Contains(@"<ApplicationIcon>..\NksHub.Textida.App\Assets\textida.ico</ApplicationIcon>", setup);
        Assert.Contains(@"<ApplicationIcon>Assets\textida.ico</ApplicationIcon>", editor);
        Assert.Contains("textida.ico", File.ReadAllText(Path.Combine(Repo.Root, "src", "NksHub.Textida.Setup", "SetupApp.axaml")));
    }
}
