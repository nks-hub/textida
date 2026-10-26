using System.Text.Json;
using System.Text.Json.Nodes;
using NksHub.Textida.Workbench;
using NksHub.Textida.Workbench.Settings;

namespace NksHub.Textida.Workbench.Tests;

/// <summary>In-memory file system: directory ('/' separators, no trailing slash) to the text of its .editorconfig.</summary>
internal sealed class MemoryEditorConfigFiles(Dictionary<string, string> files) : IEditorConfigFiles
{
    public string? Read(string directory) => files.GetValueOrDefault(directory == "/" ? "" : directory);
}

public class EditorConfigOfficialCasesTests
{
    private sealed record Case(string Name, string Config, string Path, Dictionary<string, string> Expected);

    private static readonly Lazy<(Dictionary<string, string> Configs, List<Case> Cases)> Data = new(() =>
    {
        using var stream = typeof(EditorConfigOfficialCasesTests).Assembly.GetManifestResourceStream("official-cases.json")!;
        var root = JsonNode.Parse(stream)!.AsObject();
        var configs = root["configs"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>());
        var cases = root["cases"]!.AsArray().Select(c => new Case(
            c!["name"]!.GetValue<string>(), c["config"]!.GetValue<string>(), c["path"]!.GetValue<string>(),
            c["expected"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>()))).ToList();
        return (configs, cases);
    });

    public static IEnumerable<object[]> CaseNames() => Data.Value.Cases.Select(c => new object[] { c.Name });

    /// <summary>
    /// The cases of the EditorConfig project's core test suite (glob, parser and properties; BSD-2-Clause, see official-cases.json for the notice).
    /// Each one has a configuration file, a file path and the properties a conforming implementation reports for it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Core_test_suite_case(string name)
    {
        var c = Data.Value.Cases.Single(x => x.Name == name);
        var files = new MemoryEditorConfigFiles(new() { ["/t"] = Data.Value.Configs[c.Config] });
        var result = EditorConfigResolver.Resolve("/t/" + c.Path, files);
        Assert.Equal(c.Expected.OrderBy(p => p.Key), result.Properties.OrderBy(p => p.Key));
    }

    [Fact]
    public void The_suite_is_present_and_its_notice_is_kept()
    {
        Assert.True(Data.Value.Cases.Count >= 170);
        using var stream = typeof(EditorConfigOfficialCasesTests).Assembly.GetManifestResourceStream("official-cases.json")!;
        Assert.Contains("Redistributions of source code must retain the above copyright notice", JsonNode.Parse(stream)!["license"]!.GetValue<string>());
    }
}

public class EditorConfigTests
{
    private static EditorConfigResult Resolve(string file, params (string Dir, string Text)[] configs) =>
        EditorConfigResolver.Resolve(file, new MemoryEditorConfigFiles(configs.ToDictionary(c => c.Dir, c => c.Text)));

    [Fact]
    public void Nearer_files_override_farther_ones_and_root_stops_the_search()
    {
        var r = Resolve("/w/src/a.cs",
            ("/w", "[*]\nindent_style = tab\nindent_size = 8\n"),
            ("/w/src", "[*.cs]\nindent_size = 2\n"));
        Assert.Equal("tab", r.Get("indent_style"));
        Assert.Equal("2", r.Get("indent_size"));
        Assert.Equal(["/w/src/.editorconfig", "/w/.editorconfig"], r.Files);
        Assert.Equal("/w/src/.editorconfig", r.Origins["indent_size"]);
        Assert.Equal("/w/.editorconfig", r.Origins["indent_style"]);

        var rooted = Resolve("/w/src/a.cs",
            ("/w", "[*]\nindent_style = tab\n"),
            ("/w/src", "root = true\n[*.cs]\nindent_size = 2\n"));
        Assert.Null(rooted.Get("indent_style"));
        Assert.Equal(["/w/src/.editorconfig"], rooted.Files);
    }

    [Fact]
    public void Unset_removes_a_value_inherited_from_a_farther_file()
    {
        var r = Resolve("/w/a.txt", ("", "[*]\ncharset = utf-8\ntrim_trailing_whitespace = true\n"), ("/w", "[*.txt]\ncharset = unset\n"));
        Assert.Null(r.Get("charset"));
        Assert.Equal("true", r.Get("trim_trailing_whitespace"));
    }

    [Fact]
    public void Section_globs_are_relative_to_the_file_that_holds_them()
    {
        var files = ("/w", "[lib/*.js]\nk = lib\n[*.js]\nany = yes\n[/top.js]\ntop = yes\n");
        Assert.Equal("lib", Resolve("/w/lib/a.js", files).Get("k"));
        Assert.Null(Resolve("/w/x/lib/a.js", files).Get("k"));
        Assert.Equal("yes", Resolve("/w/x/y/a.js", files).Get("any"));
        Assert.Equal("yes", Resolve("/w/top.js", files).Get("top"));
        Assert.Null(Resolve("/w/x/top.js", files).Get("top"));
        Assert.Empty(Resolve("/elsewhere/a.js", files).Properties);
    }

    [Theory]
    [InlineData("{1..3}", "2", true)]
    [InlineData("{1..3}", "4", false)]
    [InlineData("{-2..2}", "-1", true)]
    [InlineData("f{10..12}.x", "f11.x", true)]
    [InlineData("f{10..12}.x", "f13.x", false)]
    [InlineData("a{b,{c,d}}e", "ade", true)]
    public void Brace_ranges_and_alternatives(string glob, string file, bool expected) =>
        Assert.Equal(expected, EditorConfigGlob.IsMatch(glob, "/d", "/d/" + file));

    [Fact]
    public void Properties_reach_the_settings_stack_in_the_editorconfig_slot()
    {
        var stack = new SettingsStack(WorkbenchHost.CreateBuiltin().Settings);
        stack.SetLayer(SettingSource.User, new JsonObject { ["editor.tabSize"] = 8, ["files.eol"] = "lf" });
        var result = Resolve("/w/a.cs", ("/w", "[*.cs]\nindent_style = space\nindent_size = 3\ntab_width = 5\ntrim_trailing_whitespace = true\ninsert_final_newline = true\nend_of_line = crlf\ncharset = utf-8-bom\n"));
        EditorConfigSettings.Apply(stack, result);

        var tab = stack.Resolve("editor.tabSize")!;
        Assert.Equal((5L, SettingSource.EditorConfig, "/w/.editorconfig"), ((long)SettingRules.ToNumber(tab.Value)!, tab.Source, tab.LayerName));
        Assert.Equal(3, stack.GetInt("editor.indentSize"));
        Assert.True(stack.GetBool("editor.insertSpaces"));
        Assert.True(stack.GetBool("files.trimTrailingWhitespace"));
        Assert.True(stack.GetBool("files.insertFinalNewline"));
        Assert.Equal("crlf", stack.GetString("files.eol"));
        Assert.Equal("utf-8-bom", stack.GetString("files.encoding"));
        Assert.Equal(SettingSource.EditorConfig, stack.Resolve("files.eol")!.Source);

        // a document-level choice still wins over the file, and the user's value wins over nothing
        stack.SetLayer(SettingSource.Document, new JsonObject { ["editor.tabSize"] = 2 });
        Assert.Equal(SettingSource.Document, stack.Resolve("editor.tabSize")!.Source);

        // another document without a configuration clears the slot
        EditorConfigSettings.Apply(stack, EditorConfigResult.Empty);
        stack.SetLayer(SettingSource.Document, null);
        var back = stack.Resolve("editor.tabSize")!;
        Assert.Equal((8L, SettingSource.User), ((long)SettingRules.ToNumber(back.Value)!, back.Source));
    }

    [Fact]
    public void Indent_size_tab_uses_the_tab_width_and_values_the_setting_forbids_are_skipped()
    {
        var layer = EditorConfigSettings.ToLayer(Resolve("/w/a.go", ("/w", "[*.go]\nindent_style = tab\ntab_width = 6\n")));
        Assert.Equal(6, layer["editor.indentSize"]!.GetValue<long>());
        Assert.False(layer["editor.insertSpaces"]!.GetValue<bool>());

        var noWidth = EditorConfigSettings.ToLayer(Resolve("/w/a.go", ("/w", "[*.go]\nindent_style = tab\n")));
        Assert.False(noWidth.ContainsKey("editor.indentSize"));

        var stack = new SettingsStack(WorkbenchHost.CreateBuiltin().Settings);
        EditorConfigSettings.Apply(stack, Resolve("/w/a.go", ("/w", "[*.go]\nindent_size = 99\n")));
        var r = stack.Resolve("editor.indentSize")!;
        Assert.Equal(SettingSource.Default, r.Source);
        Assert.Single(r.Skipped);
    }

    [Fact]
    public void End_of_line_and_charset_are_for_new_files_and_explicit_actions_only()
    {
        var result = Resolve("/w/a.txt", ("/w", "[*.txt]\nend_of_line = lf\ncharset = latin1\n"));
        Assert.Equal(("lf", "windows-1252"), EditorConfigSettings.NewFileDefaults(result));
        Assert.Equal((null, null), EditorConfigSettings.NewFileDefaults(Resolve("/w/a.cs", ("/w", "[*.txt]\nend_of_line = lf\n"))));
    }

    [Fact]
    public void Real_files_are_found_from_the_directory_of_the_edited_file()
    {
        string dir = Path.Combine(Path.GetTempPath(), "textida-ec-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "a", "b"));
            File.WriteAllText(Path.Combine(dir, ".editorconfig"), "root = true\n[*.cs]\nindent_style = tab\n");
            File.WriteAllText(Path.Combine(dir, "a", ".editorconfig"), "[b/*.cs]\nindent_style = space\nindent_size = 4\n");
            var r = EditorConfigResolver.Resolve(Path.Combine(dir, "a", "b", "x.cs"), new DiskEditorConfigFiles());
            Assert.Equal("space", r.Get("indent_style"));
            Assert.Equal("4", r.Get("tab_width"));
            Assert.Equal(2, r.Files.Count);
            Assert.Equal("tab", EditorConfigResolver.Resolve(Path.Combine(dir, "y.cs"), new DiskEditorConfigFiles()).Get("indent_style"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
