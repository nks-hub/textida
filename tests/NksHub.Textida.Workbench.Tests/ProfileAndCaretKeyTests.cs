using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.Workbench.Tests;

/// <summary>P2.2.5 keys for several carets on Windows and macOS, and P2.2.6 the PSPad profile as a settings layer and a keymap layer.</summary>
public class ProfileAndCaretKeyTests
{
    private static readonly WorkbenchHost Host = WorkbenchHost.CreateBuiltin();

    private static IWhenContext Ctx(bool editor = true, bool carets = false) =>
        new WhenContext().Set(ContextKeys.EditorFocus, editor).Set(ContextKeys.MultipleCarets, carets).Set(ContextKeys.ReadOnly, false);

    private static string? Command(Keymap keymap, string keys, IWhenContext? ctx = null)
    {
        Assert.True(KeySequence.TryParse(keys, keymap.Platform, out var sequence, out var error), error);
        var pressed = new List<KeyChord>();
        KeyResolution last = default;
        foreach (var chord in sequence.Chords)
        {
            pressed.Add(chord);
            last = keymap.Resolve(pressed, ctx ?? Ctx());
        }
        return last.Kind == KeyResolutionKind.Command ? last.Binding!.CommandId : null;
    }

    private static Keymap Map(Platform platform, string? profile = null) =>
        profile is null ? Host.BuildKeymap(platform) : Host.BuildKeymap(platform, BehaviourProfiles.KeymapLayerOf(profile)!);

    // ---- keys for several carets: the primary modifier is Ctrl on Windows and Linux, Command on macOS ----

    [Theory]
    [InlineData(Platform.Windows, "ctrl+d", "nks.edit.addNextOccurrence")]
    [InlineData(Platform.MacOS, "meta+d", "nks.edit.addNextOccurrence")]
    [InlineData(Platform.Linux, "ctrl+d", "nks.edit.addNextOccurrence")]
    [InlineData(Platform.Windows, "ctrl+k ctrl+d", "nks.edit.skipOccurrence")]
    [InlineData(Platform.MacOS, "meta+k meta+d", "nks.edit.skipOccurrence")]
    [InlineData(Platform.Windows, "ctrl+shift+l", "nks.edit.selectAllOccurrences")]
    [InlineData(Platform.MacOS, "meta+shift+l", "nks.edit.selectAllOccurrences")]
    [InlineData(Platform.Windows, "ctrl+alt+up", "nks.edit.addCaretAbove")]
    [InlineData(Platform.MacOS, "meta+alt+up", "nks.edit.addCaretAbove")]
    [InlineData(Platform.Windows, "ctrl+alt+down", "nks.edit.addCaretBelow")]
    [InlineData(Platform.MacOS, "meta+alt+down", "nks.edit.addCaretBelow")]
    public void Keys_for_several_carets_resolve_on_every_platform(Platform platform, string keys, string command) =>
        Assert.Equal(command, Command(Map(platform), keys));

    [Fact]
    public void Escape_removes_the_extra_carets_only_while_there_are_some()
    {
        var map = Map(Platform.Windows);
        Assert.Equal("nks.edit.clearCarets", Command(map, "escape", Ctx(carets: true)));
        Assert.Null(Command(map, "escape", Ctx(carets: false)));
        Assert.Null(Command(map, "ctrl+d", Ctx(editor: false)));
    }

    [Fact]
    public void The_new_commands_are_in_the_edit_menu_and_have_texts()
    {
        var strings = StringTable.LoadBuiltin("en");
        var menu = Host.Commands.GetMenu("edit", Ctx()).Select(i => i.CommandId).ToList();
        foreach (var id in new[] { "nks.edit.addNextOccurrence", "nks.edit.skipOccurrence", "nks.edit.selectAllOccurrences", "nks.edit.addCaretAbove", "nks.edit.addCaretBelow" })
        {
            Assert.Contains(id, menu);
            Assert.True(Host.Commands.TryGet(id, out var command));
            Assert.NotEqual(command.TitleKey, strings.Get(command.TitleKey));
        }
    }

    // ---- profile ----

    [Fact]
    public void The_pspad_profile_is_valid_data_over_the_defaults()
    {
        var profile = BehaviourProfiles.Load(BehaviourProfiles.Pspad)!;
        Assert.Equal("pspad", profile.Id);
        foreach (var (key, value) in profile.Settings)
        {
            Assert.True(Host.Settings.TryGet(key, out var definition), key);
            Assert.Null(SettingRules.Validate(definition, value));
        }
        foreach (var platform in Enum.GetValues<Platform>())
        {
            var map = Map(platform, BehaviourProfiles.Pspad);
            Assert.Empty(Host.Validate(map));
            Assert.DoesNotContain(map.Conflicts, c => c.IsAmbiguous);
        }
        Assert.Null(BehaviourProfiles.Load(BehaviourProfiles.Default));
        Assert.Null(BehaviourProfiles.Load("nonsense"));
    }

    [Theory]
    // the default profile first, then PSPad: the cases of 08-editor-behavior 0.2 whose command exists here
    [InlineData("ctrl+y", "nks.edit.redo", "nks.edit.deleteLine")]
    [InlineData("ctrl+w", "nks.file.close", "nks.view.toggleWordWrap")]
    [InlineData("f8", "nks.edit.toggleBlockSelect", "nks.edit.selectToMark")]
    [InlineData("alt+up", "nks.edit.moveLineUp", null)]
    [InlineData("alt+down", "nks.edit.moveLineDown", null)]
    [InlineData("ctrl+shift+i", null, "nks.edit.indent")]
    [InlineData("ctrl+shift+u", null, "nks.edit.outdent")]
    [InlineData("alt+backspace", null, "nks.edit.undo")]
    [InlineData("alt+shift+backspace", null, "nks.edit.redo")]
    [InlineData("alt+shift+down", null, "nks.edit.duplicateLine")]
    [InlineData("ctrl+shift+c", null, "nks.edit.toggleBlockSelect")]
    [InlineData("ctrl+shift+z", "nks.edit.redo", "nks.edit.redo")]  // PSPad keeps Ctrl+Shift+Z for redo
    [InlineData("ctrl+z", "nks.edit.undo", "nks.edit.undo")]
    public void Colliding_keys_of_the_original_follow_the_profile_and_the_default_stays_without_it(string keys, string? defaults, string? pspad)
    {
        Assert.Equal(defaults, Command(Map(Platform.Windows), keys));
        Assert.Equal(pspad, Command(Map(Platform.Windows, BehaviourProfiles.Pspad), keys));
    }

    [Fact]
    public void The_profile_uses_command_on_macos_where_the_original_used_control()
    {
        var map = Map(Platform.MacOS, BehaviourProfiles.Pspad);
        Assert.Equal("nks.edit.deleteLine", Command(map, "meta+y"));
        Assert.Equal("nks.view.toggleWordWrap", Command(map, "meta+w"));
        Assert.Equal("nks.edit.indent", Command(map, "meta+shift+i"));
        Assert.Equal("nks.edit.selectToMark", Command(map, "f8"));
    }

    [Fact]
    public void A_user_binding_still_wins_over_the_profile()
    {
        var user = new KeymapLayer(KeymapLayerKind.User, "user", [new() { Key = "f8", Command = "nks.edit.toggleBlockSelect", When = "editorFocus" }]);
        var map = Host.BuildKeymap(Platform.Windows, BehaviourProfiles.KeymapLayerOf(BehaviourProfiles.Pspad)!, user);
        Assert.Equal("nks.edit.toggleBlockSelect", Command(map, "f8"));
    }

    [Fact]
    public void The_overridden_defaults_are_listed_as_intended_overrides_not_as_conflicts_to_resolve()
    {
        var map = Map(Platform.Windows, BehaviourProfiles.Pspad);
        Assert.Contains(map.Conflicts, c => c.Kind == KeyConflictKind.Overridden && c.Winner.CommandId == "nks.edit.deleteLine" && c.Loser.CommandId == "nks.edit.redo");
        Assert.Contains(map.Conflicts, c => c.Kind == KeyConflictKind.Overridden && c.Winner.CommandId == "nks.edit.selectToMark" && c.Loser.CommandId == "nks.edit.toggleBlockSelect");
    }

    [Fact]
    public void Profile_settings_sit_between_the_defaults_and_the_user_values()
    {
        var stack = new SettingsStack(Host.Settings);
        Assert.False(stack.GetBool("editor.enhancedHomeEnd"));
        Assert.Equal("carets", stack.GetString("editor.altDragMode"));

        BehaviourProfiles.ApplySettings(stack, BehaviourProfiles.Pspad);
        Assert.True(stack.GetBool("editor.enhancedHomeEnd"));
        Assert.True(stack.GetBool("editor.backspaceUnindent"));
        Assert.Equal("block", stack.GetString("editor.altDragMode"));
        Assert.Equal("pspad", stack.GetString("editor.pairStyle", "html"));
        Assert.Equal(SettingSource.Profile, stack.Resolve("editor.enhancedHomeEnd")!.Source);

        stack.SetLayer(SettingSource.User, new() { ["editor.enhancedHomeEnd"] = false });
        Assert.False(stack.GetBool("editor.enhancedHomeEnd"));
        Assert.True(stack.GetBool("editor.backspaceUnindent"));

        BehaviourProfiles.ApplySettings(stack, BehaviourProfiles.Default);
        Assert.False(stack.GetBool("editor.backspaceUnindent"));
        Assert.Equal("carets", stack.GetString("editor.altDragMode"));
    }

    [Fact]
    public void Every_smart_key_is_a_setting_that_is_on_by_default_and_can_be_switched_off()
    {
        var stack = new SettingsStack(Host.Settings);
        foreach (var key in new[] { "renameTag", "closeTag", "escapeInString", "jsonComma", "phpArrow", "selectVariable", "sqlEnd" })
        {
            string full = "editor.smartKeys." + key;
            Assert.True(Host.Settings.TryGet(full, out var definition), full);
            Assert.Equal("boolean", definition.Type);
            Assert.True(stack.GetBool(full, "html"));
            stack.SetLayer(SettingSource.UserLanguage, new() { [full] = false }, "html");
            Assert.False(stack.GetBool(full, "html"));
            Assert.True(stack.GetBool(full, "php"));
        }
    }
}
