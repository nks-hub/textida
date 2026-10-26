using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Keys;

namespace NksHub.Textida.Workbench.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("mod+s", Platform.Windows, "ctrl+s", "Ctrl+S")]
    [InlineData("mod+s", Platform.MacOS, "meta+s", "⌘S")]
    [InlineData("mod+shift+s", Platform.Linux, "ctrl+shift+s", "Ctrl+Shift+S")]
    [InlineData("Shift+Alt+Up", Platform.Windows, "alt+shift+up", "Alt+Shift+Up")]
    [InlineData("CTRL+ALT+delete", Platform.Windows, "ctrl+alt+delete", "Ctrl+Alt+Delete")]
    [InlineData("alt+backspace", Platform.MacOS, "alt+backspace", "⌥⌫")]
    [InlineData("ctrl+plus", Platform.Windows, "ctrl+plus", "Ctrl++")]
    [InlineData("cmd+ctrl+f12", Platform.MacOS, "ctrl+meta+f12", "⌃⌘F12")]
    [InlineData("tab", Platform.Windows, "tab", "Tab")]
    [InlineData("meta+e", Platform.Windows, "meta+e", "Win+E")]
    public void Parse_canonical_and_display(string text, Platform platform, string canonical, string display)
    {
        Assert.True(KeyChord.TryParse(text, platform, out var chord, out _));
        Assert.Equal(canonical, chord.ToString());
        Assert.Equal(display, chord.Display(platform));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ctrl+")]
    [InlineData("ctrl+ctrl+s")]
    [InlineData("hyper+s")]
    [InlineData("ctrl+f25")]
    [InlineData("ctrl+ab")]
    [InlineData("ctrl+S+")]
    public void Invalid_chords_are_rejected(string text)
    {
        Assert.False(KeyChord.TryParse(text, Platform.Windows, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Sequences_support_chords_and_compare_by_canonical_text()
    {
        Assert.True(KeySequence.TryParse("mod+k  mod+u", Platform.Windows, out var a, out _));
        Assert.True(KeySequence.TryParse("ctrl+k ctrl+u", Platform.Windows, out var b, out _));
        Assert.Equal(a, b);
        Assert.Equal("Ctrl+K Ctrl+U", a.Display(Platform.Windows));
        Assert.True(KeySequence.TryParse("ctrl+k", Platform.Windows, out var prefix, out _));
        Assert.True(prefix.IsStrictPrefixOf(a));
        Assert.False(a.IsStrictPrefixOf(prefix));
        Assert.False(KeySequence.TryParse("a b c d e", Platform.Windows, out _, out _));
        Assert.False(KeySequence.TryParse("  ", Platform.Windows, out _, out _));
    }
}

public class KeymapTests
{
    private static KeybindingContribution Kb(string key, string command, string? when = null, string? mac = null) =>
        new() { Key = key, Command = command, When = when, Mac = mac };

    private static KeymapLayer Layer(KeymapLayerKind kind, string source, params KeybindingContribution[] bindings) => new(kind, source, bindings);

    private static IWhenContext Ctx(params string[] on)
    {
        var c = new WhenContext();
        foreach (var k in on) c.Set(k, true);
        return c;
    }

    private static KeyChord Chord(string text) => KeyChord.TryParse(text, Platform.Windows, out var c, out _) ? c : throw new FormatException(text);

    [Fact]
    public void Builtin_keymap_has_no_ambiguous_conflicts_on_any_platform()
    {
        var host = WorkbenchHost.CreateBuiltin();
        foreach (var platform in Enum.GetValues<Platform>())
        {
            var keymap = host.BuildKeymap(platform);
            Assert.Empty(keymap.Errors);
            Assert.DoesNotContain(keymap.Conflicts, c => c.IsAmbiguous);
        }
    }

    [Fact]
    public void Ctrl_and_command_variants_come_from_the_same_data()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var win = host.BuildKeymap(Platform.Windows);
        var mac = host.BuildKeymap(Platform.MacOS);
        Assert.Equal(["Ctrl+S"], win.ShortcutsFor("nks.file.save"));
        Assert.Equal(["⌘S"], mac.ShortcutsFor("nks.file.save"));
        Assert.Equal(["Ctrl+Y", "Ctrl+Shift+Z"], win.ShortcutsFor("nks.edit.redo"));
        Assert.Equal(["⇧⌘Z"], mac.ShortcutsFor("nks.edit.redo"));
        Assert.Equal(["Ctrl+H"], win.ShortcutsFor("nks.search.replace"));
        Assert.Equal(["⌥⌘F"], mac.ShortcutsFor("nks.search.replace"));
        Assert.Equal(["Ctrl+K Ctrl+U"], win.ShortcutsFor("nks.edit.upperCase"));
        Assert.Equal(["⌘K ⌘U"], mac.ShortcutsFor("nks.edit.upperCase"));
    }

    [Fact]
    public void Higher_layers_win_and_plugins_never_override()
    {
        var layers = new[]
        {
            Layer(KeymapLayerKind.Plugin, "acme.x", Kb("ctrl+s", "acme.x.save"), Kb("ctrl+9", "acme.x.nine")),
            Layer(KeymapLayerKind.Default, "nks.core", Kb("ctrl+s", "nks.file.save")),
            Layer(KeymapLayerKind.Profile, "profile:alt", Kb("ctrl+s", "nks.file.saveAll")),
        };
        var keymap = new Keymap(Platform.Windows, layers);
        var ctx = Ctx();

        Assert.Equal("nks.file.saveAll", keymap.Resolve([Chord("ctrl+s")], ctx).Binding!.CommandId);
        Assert.Equal("acme.x.nine", keymap.Resolve([Chord("ctrl+9")], ctx).Binding!.CommandId);

        var withUser = new Keymap(Platform.Windows, layers.Append(Layer(KeymapLayerKind.User, "user", Kb("ctrl+s", "nks.file.saveAs"))));
        Assert.Equal("nks.file.saveAs", withUser.Resolve([Chord("ctrl+s")], ctx).Binding!.CommandId);

        var pluginOnly = new Keymap(Platform.Windows, layers.Take(2));
        Assert.Equal("nks.file.save", pluginOnly.Resolve([Chord("ctrl+s")], ctx).Binding!.CommandId);
    }

    [Fact]
    public void Layer_order_of_input_does_not_matter()
    {
        var a = Layer(KeymapLayerKind.Default, "d", Kb("ctrl+q", "nks.app.quit"));
        var b = Layer(KeymapLayerKind.User, "u", Kb("ctrl+q", "nks.file.close"));
        Assert.Equal("nks.file.close", new Keymap(Platform.Windows, [b, a]).Resolve([Chord("ctrl+q")], Ctx()).Binding!.CommandId);
    }

    [Fact]
    public void Unbind_removes_lower_bindings_of_the_command_only()
    {
        var keymap = new Keymap(Platform.Windows,
        [
            Layer(KeymapLayerKind.Default, "d", Kb("ctrl+s", "nks.file.save"), Kb("ctrl+d", "nks.edit.duplicateLine")),
            Layer(KeymapLayerKind.User, "u", Kb("ctrl+s", "-nks.file.save"), Kb("ctrl+s", "nks.file.saveAs")),
        ]);
        Assert.Equal(["nks.edit.duplicateLine", "nks.file.saveAs"], keymap.Bindings.Select(b => b.CommandId).Order());
        Assert.Empty(keymap.Conflicts);
    }

    [Fact]
    public void When_context_selects_between_bindings_on_the_same_keys()
    {
        var keymap = new Keymap(Platform.Windows,
        [
            Layer(KeymapLayerKind.Default, "d", Kb("tab", "a.b.indent", "hasSelection"), Kb("tab", "a.b.tab", "!hasSelection")),
        ]);
        Assert.Equal("a.b.indent", keymap.Resolve([Chord("tab")], Ctx("hasSelection")).Binding!.CommandId);
        Assert.Equal("a.b.tab", keymap.Resolve([Chord("tab")], Ctx()).Binding!.CommandId);
        Assert.Empty(keymap.Conflicts);
    }

    [Fact]
    public void Same_layer_conflict_in_one_context_is_reported_with_both_sources()
    {
        var keymap = new Keymap(Platform.Windows,
        [
            Layer(KeymapLayerKind.Default, "nks.core", Kb("ctrl+d", "nks.edit.duplicateLine", "editorFocus")),
            Layer(KeymapLayerKind.Default, "nks.extra", Kb("ctrl+d", "nks.edit.deleteLine", "editorFocus && !readOnly")),
        ]);
        var conflict = Assert.Single(keymap.Conflicts);
        Assert.Equal(KeyConflictKind.SameLayer, conflict.Kind);
        Assert.True(conflict.IsAmbiguous);
        Assert.Equal("nks.extra", conflict.Winner.Source);
        Assert.Equal("nks.core", conflict.Loser.Source);
        Assert.Equal("editorFocus && !readOnly", conflict.Winner.WhenText);
    }

    [Fact]
    public void Disjoint_contexts_are_not_a_conflict()
    {
        var keymap = new Keymap(Platform.Windows,
        [
            Layer(KeymapLayerKind.Default, "d", Kb("ctrl+d", "a.b.one", "language == 'json'"), Kb("ctrl+d", "a.b.two", "language == 'xml'"), Kb("ctrl+d", "a.b.three", "readOnly"), Kb("ctrl+d", "a.b.four", "!readOnly")),
        ]);
        var overlaps = keymap.Conflicts.Select(c => (c.Winner.CommandId, c.Loser.CommandId)).ToList();
        Assert.DoesNotContain(("a.b.two", "a.b.one"), overlaps);
        Assert.DoesNotContain(("a.b.four", "a.b.three"), overlaps);
        Assert.Contains(("a.b.three", "a.b.one"), overlaps);
    }

    [Fact]
    public void Override_across_layers_is_informational_and_prefix_is_ambiguous()
    {
        var keymap = new Keymap(Platform.Windows,
        [
            Layer(KeymapLayerKind.Default, "nks.core", Kb("ctrl+s", "nks.file.save"), Kb("ctrl+k ctrl+u", "nks.edit.upperCase")),
            Layer(KeymapLayerKind.User, "user", Kb("ctrl+s", "nks.file.saveAll"), Kb("ctrl+k", "nks.edit.tab")),
        ]);
        var overridden = Assert.Single(keymap.Conflicts, c => c.Kind == KeyConflictKind.Overridden);
        Assert.False(overridden.IsAmbiguous);
        Assert.Equal(("user", "nks.core"), (overridden.Winner.Source, overridden.Loser.Source));

        var prefix = Assert.Single(keymap.Conflicts, c => c.Kind == KeyConflictKind.Prefix);
        Assert.True(prefix.IsAmbiguous);
        Assert.Equal("nks.edit.tab", prefix.Winner.CommandId);
        Assert.Equal("nks.edit.upperCase", prefix.Loser.CommandId);
        Assert.Equal(KeyResolutionKind.Command, keymap.Resolve([Chord("ctrl+k")], Ctx()).Kind);
    }

    [Fact]
    public void Same_command_twice_on_the_same_keys_is_not_a_conflict()
    {
        var keymap = new Keymap(Platform.MacOS,
        [
            Layer(KeymapLayerKind.Default, "d", Kb("ctrl+y", "x.y.redo", mac: "mod+shift+z"), Kb("mod+shift+z", "x.y.redo")),
        ]);
        Assert.Empty(keymap.Conflicts);
        Assert.Equal(["⇧⌘Z"], keymap.ShortcutsFor("x.y.redo"));
    }

    [Fact]
    public void Chords_resolve_step_by_step()
    {
        var keymap = new Keymap(Platform.Windows, [Layer(KeymapLayerKind.Default, "d", Kb("ctrl+k ctrl+u", "nks.edit.upperCase"), Kb("ctrl+k ctrl+l", "nks.edit.lowerCase"))]);
        Assert.Equal(KeyResolutionKind.Pending, keymap.Resolve([Chord("ctrl+k")], Ctx()).Kind);
        Assert.Equal("nks.edit.lowerCase", keymap.Resolve([Chord("ctrl+k"), Chord("ctrl+l")], Ctx()).Binding!.CommandId);
        Assert.Equal(KeyResolutionKind.NoMatch, keymap.Resolve([Chord("ctrl+k"), Chord("ctrl+x")], Ctx()).Kind);
        Assert.Equal(KeyResolutionKind.NoMatch, keymap.Resolve([Chord("ctrl+j")], Ctx()).Kind);
    }

    [Fact]
    public void Invalid_entries_are_skipped_and_listed_with_their_source()
    {
        var keymap = new Keymap(Platform.Windows, [Layer(KeymapLayerKind.User, "user", Kb("ctrl+nonsense", "a.b.c"), Kb("ctrl+a", "a.b.c", "a &&"), Kb("ctrl+b", "a.b.c"))]);
        Assert.Equal(2, keymap.Errors.Count);
        Assert.All(keymap.Errors, e => Assert.StartsWith("user:", e));
        Assert.Single(keymap.Bindings);
    }

    [Fact]
    public void Model_can_express_a_pspad_style_profile()
    {
        var profile = Layer(KeymapLayerKind.Profile, "profile:pspad",
            Kb("alt+backspace", "nks.edit.undo", "editorFocus"),
            Kb("ctrl+insert", "nks.edit.copy", "editorFocus"),
            Kb("shift+delete", "nks.edit.cut", "editorFocus"),
            Kb("shift+insert", "nks.edit.paste", "editorFocus"),
            Kb("ctrl+shift+0", "pspad.marker.set0", "editorFocus"),
            Kb("ctrl+0", "pspad.marker.goto0", "editorFocus"),
            Kb("ctrl+k ctrl+l", "nks.edit.lowerCase", "editorFocus"),
            Kb("ctrl+shift+minus", "pspad.fold.all", "editorFocus"),
            Kb("ctrl+slash", "pspad.fold.nearest", "editorFocus"),
            Kb("ctrl+y", "nks.edit.deleteLine", "editorFocus"));
        var host = WorkbenchHost.CreateBuiltin();
        var keymap = host.BuildKeymap(Platform.Windows, profile);
        Assert.Empty(keymap.Errors);
        Assert.Equal("nks.edit.deleteLine", keymap.Resolve([Chord("ctrl+y")], Ctx("editorFocus")).Binding!.CommandId);
        Assert.Equal("nks.edit.undo", keymap.Resolve([Chord("alt+backspace")], Ctx("editorFocus")).Binding!.CommandId);
        Assert.Contains(keymap.Conflicts, c => c.Kind == KeyConflictKind.Overridden && c.Winner.Source == "profile:pspad");
    }

    [Fact]
    public void Keymap_file_is_data_with_a_schema_version()
    {
        var file = System.Text.Json.JsonSerializer.Deserialize("""
            { "schemaVersion": 1, "bindings": [ { "key": "mod+shift+d", "command": "nks.edit.deleteLine" } ] }
            """, ContributionJson.Default.KeymapFile)!;
        var layer = WorkbenchHost.LayerFromFile(file, KeymapLayerKind.User, "user");
        var keymap = WorkbenchHost.CreateBuiltin().BuildKeymap(Platform.Windows, layer);
        Assert.Equal("nks.edit.deleteLine", keymap.Resolve([Chord("ctrl+shift+d")], Ctx("editorFocus")).Binding!.CommandId);
    }

    [Fact]
    public async Task Unhandled_tab_falls_back_to_plain_indentation()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var keymap = host.BuildKeymap(Platform.Windows);
        var log = new List<string>();
        host.Commands.SetHandler("nks.edit.tab", (_, _) => { log.Add("tab"); return ValueTask.FromResult(CommandOutcome.NotHandled); });
        host.Commands.SetHandler("nks.edit.insertIndent", (_, _) => { log.Add("insertIndent"); return ValueTask.FromResult(CommandOutcome.Handled); });
        var dispatcher = new KeyDispatcher(keymap, host.Commands);
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, false);

        var result = await dispatcher.HandleAsync(Chord("tab"), ctx);
        Assert.Equal(new KeyDispatchResult(KeyDispatchKind.Executed, "nks.edit.tab"), result);
        Assert.Equal(["tab", "insertIndent"], log);
    }

    [Fact]
    public async Task Dispatcher_reports_unbound_pending_cancelled_not_handled_and_disabled()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var dispatcher = new KeyDispatcher(host.BuildKeymap(Platform.Windows), host.Commands);
        var ctx = new WhenContext().Set(ContextKeys.EditorFocus, true).Set(ContextKeys.ReadOnly, false).Set(ContextKeys.HasSelection, false);

        Assert.Equal(KeyDispatchKind.Unbound, (await dispatcher.HandleAsync(Chord("ctrl+j"), ctx)).Kind);
        Assert.Equal(KeyDispatchKind.Pending, (await dispatcher.HandleAsync(Chord("ctrl+k"), ctx)).Kind);
        Assert.Equal(KeyDispatchKind.Cancelled, (await dispatcher.HandleAsync(Chord("ctrl+x"), ctx)).Kind);
        Assert.Equal(KeyDispatchKind.Unbound, (await dispatcher.HandleAsync(Chord("ctrl+j"), ctx)).Kind);

        Assert.Equal(KeyDispatchKind.NotHandled, (await dispatcher.HandleAsync(Chord("tab"), ctx)).Kind);

        await dispatcher.HandleAsync(Chord("ctrl+k"), ctx);
        Assert.Equal(KeyDispatchKind.Disabled, (await dispatcher.HandleAsync(Chord("ctrl+u"), ctx)).Kind);

        host.Commands.SetHandler("nks.edit.upperCase", (_, _) => ValueTask.FromResult(CommandOutcome.Handled));
        ctx.Set(ContextKeys.HasSelection, true);
        await dispatcher.HandleAsync(Chord("ctrl+k"), ctx);
        Assert.Equal(new KeyDispatchResult(KeyDispatchKind.Executed, "nks.edit.upperCase"), await dispatcher.HandleAsync(Chord("ctrl+u"), ctx));
    }
}
