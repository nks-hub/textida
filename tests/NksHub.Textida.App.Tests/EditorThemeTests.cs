using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Workbench.Settings;
using static NksHub.Textida.App.Tests.HighlightExportTests;

namespace NksHub.Textida.App.Tests;

/// <summary>P3.2.5 (owner request of 2. 11. 2026): colour themes of the editor with custom token colours, saved, exported and imported from TextMate and VS Code.</summary>
[Collection("LocState")]
public class EditorThemeTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private App _app;

    public EditorThemeTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        _app.RequestedThemeVariant = ThemeVariant.Default;
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private static readonly Color Red = Color.Parse("#FF0000");

    private static void Settle()
    {
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
    }

    private static HashSet<Color> Painted(DocTab tab, int line)
    {
        tab.Editor!.TextArea.TextView.EnsureVisualLines();
        return tab.Editor.TextArea.TextView.GetVisualLine(line)!.Elements.Select(e => e.TextRunProperties.ForegroundBrush).OfType<ISolidColorBrush>().Select(b => b.Color).ToHashSet();
    }

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private SettingsWindow OpenSettings()
    {
        _app.OpenSettings();
        Dispatcher.UIThread.RunJobs();
        var view = _app.SettingsView!;
        view.SelectCategory(SettingCategories.Appearance);
        return view;
    }

    [AvaloniaFact]
    public void Every_built_in_theme_is_offered_loads_and_reads_like_the_built_in_ui_themes()
    {
        Assert.Equal(["light", "dark", "monokai", "solarized-light", "solarized-dark", "dracula", "one-dark", "github-light", "github-dark", "nord"], EditorColorTheme.BuiltIn.Select(t => t.Id));
        foreach (var theme in EditorColorTheme.BuiltIn)
        {
            Assert.All(EditorColorTheme.Keys, key => Assert.True(theme.Colors.ContainsKey(key), $"{theme.Id} has no {key}"));
            var back = theme[EditorColorTheme.Background];
            var line = EditorColorTheme.Over(theme[EditorColorTheme.CurrentLine], back);
            void Reads(string text, Color fill, string where)
            {
                double ratio = ContrastColors.Ratio(EditorColorTheme.Over(theme[text], back), fill);
                Assert.True(ratio >= 4.5, $"{theme.Id}: {text} on {where} is {ratio:0.00} : 1");
            }
            foreach (var text in EditorColorTheme.Tokens.Where(k => k != "log.highlight").Append(EditorColorTheme.Foreground).Append(EditorColorTheme.LineNumbers))
            {
                Reads(text, back, "the background");
                Reads(text, line, "the current line");
            }
            Reads(EditorColorTheme.Foreground, EditorColorTheme.Over(theme[EditorColorTheme.Selection], back), "the selection");
            Reads(EditorColorTheme.Foreground, EditorColorTheme.Over(theme["log.highlight"], back), "the highlight of a log");
            foreach (var kind in EditorColorTheme.Tokens) Assert.Equal(theme[kind], BrushColor(theme.Palette.GetBrush(kind)));

            // the setting chooses it and the editors get it
            _app.Workbench.Set(EditorThemes.ThemeSetting, theme.Id);
            Assert.Equal(theme.Id, _app.EditorThemes.Active(ThemeVariant.Light).Id);
            Assert.Equal(back, _app.EditorThemes.Active(ThemeVariant.Dark)[EditorColorTheme.Background]);
        }
    }

    [AvaloniaFact]
    public void Automatic_is_the_light_or_the_dark_theme_and_those_are_the_brushes_of_the_window_themes()
    {
        foreach (var (variant, own) in new[] { (ThemeVariant.Light, EditorColorTheme.Light), (ThemeVariant.Dark, EditorColorTheme.Dark) })
        {
            var auto = _app.EditorThemes.Active(variant);
            Assert.Equal(EditorColorTheme.AutoId, auto.Id);
            foreach (var key in EditorColorTheme.Keys) Assert.True(own[key] == auto[key], $"{variant}: {key} {own[key]} != {auto[key]}");
            Assert.Same(variant == ThemeVariant.Dark ? SyntaxPalette.Dark : SyntaxPalette.Light, auto.Palette);
        }
    }

    [AvaloniaFact]
    public void Choosing_a_theme_repaints_the_open_editors_of_every_window_at_once()
    {
        const string code = "using System;\nclass A { int n = 42; }\n";
        var tabs = new List<DocTab>();
        foreach (var name in new[] { "a.cs", "b.cs" })
        {
            var w = _app.CreateWindow(null);
            w.Width = 900;
            w.Height = 600;
            tabs.Add(w.OpenFile(_temp.File(name, code), null));
            w.UpdateLayout();
        }
        Settle();
        var monokai = EditorColorTheme.BuiltIn.Single(t => t.Id == "monokai");
        var light = EditorColorTheme.Light;
        Assert.All(tabs, tab => Assert.Contains(light["keyword"], Painted(tab, 1)));

        _app.Workbench.Set(EditorThemes.ThemeSetting, "monokai");
        Settle();
        foreach (var tab in tabs)
        {
            Assert.Equal(monokai[EditorColorTheme.Background], BrushColor(tab.Editor!.Background));
            Assert.Equal(monokai[EditorColorTheme.Foreground], BrushColor(tab.Editor.Foreground));
            Assert.Equal(monokai[EditorColorTheme.CurrentLine], BrushColor(tab.Editor.TextArea.TextView.CurrentLineBackground));
            var colors = Painted(tab, 1);
            Assert.Contains(monokai["keyword"], colors);
            Assert.DoesNotContain(light["keyword"], colors);
        }

        _app.Workbench.Set(EditorThemes.ThemeSetting, (System.Text.Json.Nodes.JsonNode?)null);
        Settle();
        Assert.All(tabs, tab => Assert.Contains(light["keyword"], Painted(tab, 1)));
    }

    [AvaloniaFact]
    public async Task A_custom_token_colour_shows_in_the_editor_the_preview_and_the_highlighted_export()
    {
        var w = _app.CreateWindow(null);
        w.Width = 1000;
        w.Height = 700;
        var tab = w.OpenFile(_temp.File("a.cs", CSharp), null);
        w.UpdateLayout();
        Settle();
        var view = OpenSettings();
        Assert.NotEqual(Red, view.Preview!.ForegroundOf("function"));

        view.SetThemeColor("keyword", Red);
        Settle();
        Assert.Equal("keyword=#FF0000", _app.Workbench.Settings.GetString(EditorThemes.ColorsSetting));
        Assert.Contains(Red, Painted(tab, 2)); // "using System;"
        Assert.Equal(Red, view.Preview!.ForegroundOf("function"));

        string path = Path.Combine(_temp.Path, "out.html");
        _dialogs.SaveFiles.Enqueue(path);
        w.Activate();
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, w.ExportHighlighted(MainWindow.ExportForm.Html));
        ToolTestKit.WaitFor(() => File.Exists(path) && w.StatusMessageText == Loc.Get(K.ExportDone, ("name", "out.html")), "the export did not finish", 6000);
        var palette = EditorColorTheme.Light.With(new Dictionary<string, Color> { ["keyword"] = Red }).Palette;
        Assert.Equal(EditorColors(CSharp, "csharp", palette), HtmlColors(await File.ReadAllTextAsync(path)));

        view.SetThemeColor("keyword", null);
        Assert.Equal("", _app.Workbench.Settings.GetString(EditorThemes.ColorsSetting));
        Assert.Equal(EditorColorTheme.Light["keyword"], view.Preview!.ForegroundOf("function"));
        Assert.DoesNotContain(Red, Painted(tab, 2));
    }

    [AvaloniaFact]
    public async Task A_custom_theme_survives_a_restart_and_comes_back_unchanged_through_export_and_import()
    {
        _app.Workbench.Set(EditorThemes.ThemeSetting, "dracula");
        var view = OpenSettings();
        view.SetThemeColor("comment", Color.Parse("#123456"));
        _dialogs.Prompts.Enqueue("Můj motiv");
        await view.SaveThemeAsync();
        Assert.True(File.Exists(Path.Combine(_app.EditorThemes.Folder, "Můj motiv.json")));
        Assert.Equal("user:Můj motiv", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
        Assert.Equal("", _app.Workbench.Settings.GetString(EditorThemes.ColorsSetting)); // the custom colour is part of the theme now
        var saved = _app.EditorThemes.Active(ThemeVariant.Light);
        Assert.Equal("Můj motiv", saved.Name);
        Assert.Equal(Color.Parse("#123456"), saved["comment"]);
        Assert.Equal(EditorColorTheme.BuiltIn.Single(t => t.Id == "dracula")["keyword"], saved["keyword"]);

        string exported = Path.Combine(_temp.Path, "export", "theme.json");
        Directory.CreateDirectory(Path.GetDirectoryName(exported)!);
        _dialogs.SaveFiles.Enqueue(exported);
        await view.ExportThemeAsync();
        Assert.Contains("\"tokenColors\"", File.ReadAllText(exported));
        _dialogs.Picked.Add(exported);
        await view.ImportThemeAsync();
        Assert.Equal("user:Můj motiv (2)", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
        var imported = _app.EditorThemes.Active(ThemeVariant.Light);
        foreach (var key in EditorColorTheme.Keys) Assert.True(saved[key] == imported[key], $"{key}: {saved[key]} != {imported[key]}");
        Assert.Equal(saved.IsDark, imported.IsDark);

        view.Close();
        _app = TestApp.Restart(_temp, _app, _dialogs);
        Assert.Equal("user:Můj motiv (2)", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
        var again = _app.EditorThemes.Active(ThemeVariant.Light);
        Assert.Equal("Můj motiv (2)", again.Name);
        foreach (var key in EditorColorTheme.Keys) Assert.Equal(saved[key], again[key]);
        Assert.Equal(["Můj motiv", "Můj motiv (2)"], _app.EditorThemes.Custom.Select(t => t.Name));

        // a custom theme is deleted with its file; an id that is not one of the folder deletes nothing
        string outside = _temp.File("outside.json", "{}");
        Assert.False(_app.EditorThemes.Delete("user:../../outside"));
        Assert.True(File.Exists(outside));
        Assert.True(_app.EditorThemes.Delete("user:Můj motiv"));
        Assert.False(File.Exists(Path.Combine(_app.EditorThemes.Folder, "Můj motiv.json")));
        Assert.Equal(["Můj motiv (2)"], _app.EditorThemes.Custom.Select(t => t.Name));
    }

    private const string TmTheme = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>name</key><string>Sample Night</string>
          <key>settings</key>
          <array>
            <dict>
              <key>settings</key>
              <dict>
                <key>background</key><string>#101820</string>
                <key>foreground</key><string>#E0E0E0</string>
                <key>lineHighlight</key><string>#1C2630</string>
                <key>selection</key><string>#33445580</string>
                <key>gutterForeground</key><string>#8899AA</string>
              </dict>
            </dict>
            <dict><key>scope</key><string>comment</string><key>settings</key><dict><key>foreground</key><string>#7F8C8D</string><key>fontStyle</key><string>italic</string></dict></dict>
            <dict><key>scope</key><string>string, string.quoted</string><key>settings</key><dict><key>foreground</key><string>#A3E635</string></dict></dict>
            <dict><key>scope</key><string>meta.tag string</string><key>settings</key><dict><key>foreground</key><string>#FF00FF</string></dict></dict>
            <dict><key>scope</key><string>string.regexp</string><key>settings</key><dict><key>foreground</key><string>#F97316</string></dict></dict>
            <dict><key>scope</key><string>constant.numeric</string><key>settings</key><dict><key>foreground</key><string>#C084FC</string></dict></dict>
            <dict><key>scope</key><string>keyword</string><key>settings</key><dict><key>foreground</key><string>#F472B6</string></dict></dict>
            <dict><key>scope</key><string>storage.type</string><key>settings</key><dict><key>foreground</key><string>#38BDF8</string></dict></dict>
            <dict><key>scope</key><string>entity.name.function</string><key>settings</key><dict><key>foreground</key><string>#FACC15</string></dict></dict>
            <dict><key>scope</key><string>entity.name.tag</string><key>settings</key><dict><key>foreground</key><string>#FB7185</string></dict></dict>
            <dict><key>scope</key><string>entity.other.attribute-name</string><key>settings</key><dict><key>foreground</key><string>#34D399</string></dict></dict>
          </array>
        </dict>
        </plist>
        """;

    private const string VsCodeTheme = """
        // a theme as VS Code writes it: comments and trailing commas are allowed
        {
          "name": "Sample Day",
          "type": "light",
          "colors": {
            "editor.background": "#FAFAFA",
            "editor.foreground": "#202020",
            "editor.lineHighlightBackground": "#0000000D",
            "editor.selectionBackground": "#ADD6FF",
            "editorLineNumber.foreground": "#6E6E6E",
            "minimap.background": "#F0F0F0",
          },
          "tokenColors": [
            { "scope": "keyword", "settings": { "foreground": "#AF00DB" } },
            { "scope": "keyword.control", "settings": { "foreground": "#0000FF" } },
            { "scope": ["comment", "punctuation.definition.comment"], "settings": { "foreground": "#008000" } },
            { "scope": "string", "settings": { "foreground": "#A31515" } },
            { "scope": "variable", "settings": { "foreground": "#001080" } },
            { "scope": "variable.other.property", "settings": { "foreground": "#0070C1" } },
            { "scope": "entity.name.type, support.type", "settings": { "foreground": "#267F99" } },
            { "scope": "markup.heading", "settings": { "foreground": "#80000080" } },
            { "settings": { "fontStyle": "italic" } },
          ],
        }
        """;

    [AvaloniaFact]
    public async Task TextMate_and_VS_Code_themes_give_their_colours_to_the_token_kinds()
    {
        var view = OpenSettings();
        _dialogs.Picked.Add(_temp.File("night.tmTheme", TmTheme));
        await view.ImportThemeAsync();
        Assert.Equal("user:Sample Night", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
        var night = _app.EditorThemes.Active(ThemeVariant.Light);
        Assert.True(night.IsDark);
        Color C(string hex) => Color.Parse(hex);
        Assert.Equal(C("#101820"), night[EditorColorTheme.Background]);
        Assert.Equal(C("#E0E0E0"), night[EditorColorTheme.Foreground]);
        Assert.Equal(C("#1C2630"), night[EditorColorTheme.CurrentLine]);
        Assert.Equal(Color.FromArgb(0x80, 0x33, 0x44, 0x55), night[EditorColorTheme.Selection]);
        Assert.Equal(C("#8899AA"), night[EditorColorTheme.LineNumbers]);
        Assert.Equal(C("#101820"), night[EditorColorTheme.Minimap]);
        Assert.Equal(C("#7F8C8D"), night["comment"]);
        Assert.Equal(C("#A3E635"), night["string"]); // not the colour of strings inside tags only
        Assert.Equal(C("#F97316"), night["regex"]);
        Assert.Equal(C("#C084FC"), night["number"]);
        Assert.Equal(C("#F472B6"), night["keyword"]);
        Assert.Equal(C("#38BDF8"), night["type"]); // storage.type stands in for entity.name.type
        Assert.Equal(C("#FACC15"), night["function"]);
        Assert.Equal(C("#FB7185"), night["tag"]);
        Assert.Equal(C("#34D399"), night["attribute"]);
        Assert.Equal(C("#F472B6"), night["preprocessor"]); // keyword.control.directive falls back to keyword
        Assert.Equal(C("#E0E0E0"), night["heading"]); // nothing for it: the text colour
        Assert.Equal(EditorColorTheme.Dark["log.error"], night["log.error"]);

        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(_temp.File("day.json", VsCodeTheme));
        await view.ImportThemeAsync();
        Assert.Equal("user:Sample Day", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
        var day = _app.EditorThemes.Active(ThemeVariant.Dark);
        Assert.False(day.IsDark);
        Assert.Equal(C("#FAFAFA"), day[EditorColorTheme.Background]);
        Assert.Equal(C("#F0F0F0"), day[EditorColorTheme.Minimap]);
        Assert.Equal(Color.FromArgb(0x0D, 0, 0, 0), day[EditorColorTheme.CurrentLine]);
        Assert.Equal(C("#0000FF"), day["keyword"]); // keyword.control is more telling than keyword
        Assert.Equal(C("#0000FF"), day["preprocessor"]); // keyword.control.directive takes the rule of its longest prefix, keyword.control
        Assert.Equal(C("#008000"), day["comment"]);
        Assert.Equal(C("#A31515"), day["string"]);
        Assert.Equal(C("#001080"), day["variable"]);
        Assert.Equal(C("#0070C1"), day["property"]);
        Assert.Equal(C("#267F99"), day["type"]);
        Assert.Equal(EditorColorTheme.Over(Color.FromArgb(0x80, 0x80, 0, 0), C("#FAFAFA")), day["heading"]); // a token with transparency as it shows on the background
        Assert.Equal(EditorColorTheme.Light["log.warning"], day["log.warning"]);

        // a file that is no theme is refused with the reason and nothing is added
        int before = _app.EditorThemes.Custom.Count;
        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(_temp.File("broken.json", "{ \"name\": \"x\", \"tokenColors\": \"./other.tmTheme\" }"));
        await view.ImportThemeAsync();
        Assert.Equal(before, _app.EditorThemes.Custom.Count);
        Assert.Equal("user:Sample Day", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
    }

    /// <summary>The window drawn as it is on screen; when <c>TEXTIDA_THEME_RENDER_DIR</c> names a folder the picture is kept there as a PNG for a look by eye.</summary>
    private static Avalonia.Media.Imaging.Bitmap Frame(Avalonia.Controls.Window window, string name)
    {
        window.UpdateLayout();
        for (int i = 0; i < 10; i++)
        {
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame(); // the frame is drawn on the render thread; every capture draws what changed since the last one
            Thread.Sleep(5);
        }
        var frame = window.CaptureRenderedFrame()!;
        if (Environment.GetEnvironmentVariable("TEXTIDA_THEME_RENDER_DIR") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            frame.Save(Path.Combine(folder, name + ".png"));
        }
        return frame;
    }

    /// <summary>The colour most pixels of a rectangle of the frame have.</summary>
    private static Color MostOf(Avalonia.Media.Imaging.Bitmap bitmap, Avalonia.Rect area)
    {
        var frame = Assert.IsAssignableFrom<Avalonia.Media.Imaging.WriteableBitmap>(bitmap);
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
        double scale = buffer.Size.Width / frame.Size.Width;
        var counts = new Dictionary<Color, int>();
        for (int y = (int)(area.Top * scale); y < Math.Min(buffer.Size.Height, (int)(area.Bottom * scale)); y += 2)
            for (int x = (int)(area.Left * scale); x < Math.Min(buffer.Size.Width, (int)(area.Right * scale)); x += 2)
            {
                int at = y * buffer.RowBytes + x * 4;
                var c = frame.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? Color.FromRgb(data[at + 2], data[at + 1], data[at]) : Color.FromRgb(data[at], data[at + 1], data[at + 2]);
                counts[c] = counts.GetValueOrDefault(c) + 1;
            }
        return counts.MaxBy(p => p.Value).Key;
    }

    [AvaloniaFact]
    public void Every_theme_draws_the_editor_and_the_preview_of_the_settings_in_its_colours()
    {
        var w = _app.CreateWindow(null);
        w.Width = 1000;
        w.Height = 640;
        var tab = w.OpenFile(_temp.File("a.cs", CSharp), null);
        w.UpdateLayout();
        Settle();
        var view = OpenSettings();
        foreach (var theme in EditorColorTheme.BuiltIn)
        {
            _app.Workbench.Set(EditorThemes.ThemeSetting, theme.Id);
            Settle();
            var editor = tab.Editor!;
            var origin = editor.TranslatePoint(new Avalonia.Point(0, 0), w)!.Value;
            Assert.Equal(theme[EditorColorTheme.Background], MostOf(Frame(w, "editor-" + theme.Id), new Avalonia.Rect(origin, editor.Bounds.Size)));
            var preview = view.Preview!;
            var at = preview.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value;
            Assert.Equal(theme[EditorColorTheme.Background], MostOf(Frame(view, "settings-" + theme.Id), new Avalonia.Rect(at, preview.Bounds.Size).Deflate(4)));
        }
        view.ShowKey(EditorThemes.ColorsSetting);
        Settle();
        Frame(view, "settings-colors");
    }

    [AvaloniaFact]
    public void The_theme_list_and_the_colour_pickers_are_on_the_appearance_page_and_high_contrast_takes_over()
    {
        var view = OpenSettings();
        Assert.Contains(EditorThemes.ThemeSetting, view.ShownIds);
        Assert.Contains(EditorThemes.ColorsSetting, view.ShownIds);
        Assert.Equal(SettingControl.ColorTheme, view.ControlOf(EditorThemes.ThemeSetting));
        Assert.Equal(SettingControl.ThemeColors, view.ControlOf(EditorThemes.ColorsSetting));

        _app.Workbench.Set(EditorThemes.ThemeSetting, "nord");
        _app.Workbench.Set(EditorThemes.ColorsSetting, "keyword=#FF0000");
        _app.Contrast.Apply(ContrastColors.Black);
        try
        {
            var look = _app.EditorThemes.Active(ThemeVariant.Dark);
            Assert.Equal(ContrastColors.Black.Window, look[EditorColorTheme.Background]);
            Assert.NotEqual(Red, look["keyword"]);
        }
        finally { _app.Contrast.Restore(); }
        Assert.Equal(Red, _app.EditorThemes.Active(ThemeVariant.Dark)["keyword"]);
    }

    // ---- hex, CSV table and comparison ----

    /// <summary>The number of pixels of a rectangle of the frame that have exactly <paramref name="color"/>.</summary>
    private static int CountOf(Avalonia.Media.Imaging.Bitmap bitmap, Avalonia.Rect area, Color color)
    {
        var frame = Assert.IsAssignableFrom<Avalonia.Media.Imaging.WriteableBitmap>(bitmap);
        using var buffer = frame.Lock();
        var data = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
        double scale = buffer.Size.Width / frame.Size.Width;
        int count = 0;
        for (int y = Math.Max(0, (int)(area.Top * scale)); y < Math.Min(buffer.Size.Height, (int)(area.Bottom * scale)); y++)
            for (int x = Math.Max(0, (int)(area.Left * scale)); x < Math.Min(buffer.Size.Width, (int)(area.Right * scale)); x++)
            {
                int at = y * buffer.RowBytes + x * 4;
                var c = frame.Format == Avalonia.Platform.PixelFormat.Bgra8888 ? Color.FromRgb(data[at + 2], data[at + 1], data[at]) : Color.FromRgb(data[at], data[at + 1], data[at + 2]);
                if (c == color) count++;
            }
        return count;
    }

    private static Avalonia.Rect AreaOf(Avalonia.Controls.Control control, Avalonia.Visual window) =>
        new(control.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value, control.Bounds.Size);

    [AvaloniaFact]
    public void Every_theme_gives_the_hex_table_and_comparison_colours_that_read()
    {
        foreach (var theme in EditorColorTheme.BuiltIn.Append(EditorColorTheme.FromVsCode("""{ "type": "dark", "colors": { "editor.background": "#202020", "editor.foreground": "#909090" } }""", "dim", "Dim")))
        {
            var view = theme.ViewColors;
            double Ratio(string text, string fill) => ContrastColors.Ratio(view[text], view[fill]);
            Assert.Equal(theme[EditorColorTheme.Background], view["BgEditor"]);
            foreach (var fill in new[] { "BgEditor", "CurrentLine", "SelectionBg", "AccentSoft", "Highlight", "WarnBg", "DiffRemoved", "DiffAdded" })
                Assert.True(Ratio("Fg", fill) >= 4.5, $"{theme.Id}: the text on {fill} is {Ratio("Fg", fill):0.00} : 1");
            foreach (var text in new[] { "FgMuted", "LineNumbers", "Accent", "Danger" })
                foreach (var fill in new[] { "BgEditor", "CurrentLine" })
                    Assert.True(Ratio(text, fill) >= 4.5, $"{theme.Id}: {text} on {fill} is {Ratio(text, fill):0.00} : 1");
            // a difference can be told from the background and from the other kinds of difference
            foreach (var tint in new[] { "DiffRemoved", "DiffAdded", "WarnBg" })
                Assert.True(Ratio(tint, "BgEditor") > 1.05, $"{theme.Id}: {tint} is {Ratio(tint, "BgEditor"):0.000} : 1 to the background");
            Assert.NotEqual(view["DiffRemoved"], view["DiffAdded"]);
            Assert.NotEqual(view["DiffAdded"], view["WarnBg"]);
        }
    }

    [AvaloniaFact]
    public async Task The_hex_view_the_table_and_the_comparison_draw_in_the_chosen_theme_and_high_contrast_keeps_the_system_colours()
    {
        var w = _app.CreateWindow(null);
        w.Width = 1000;
        w.Height = 640;
        var bytes = new byte[4096];
        new Random(3).NextBytes(bytes);
        w.OpenFile(_temp.Bytes("data.bin", bytes), null);
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, _app.Workbench.Commands.ExecuteAsync("nks.view.hex", new ShellContext(w, forMenu: true)).AsTask().GetAwaiter().GetResult());
        w.UpdateLayout();
        Settle();
        var hex = w.ActiveHex!;
        w.OpenFile(_temp.File("data.csv", "id,name,value\n1,alpha,10\n2,beta,20\n3,gamma,30\n"), null);
        w.UpdateLayout();
        Settle();
        Assert.Equal(Workbench.Commands.CommandOutcome.Handled, _app.Workbench.Commands.ExecuteAsync("nks.view.table", new ShellContext(w, forMenu: true)).AsTask().GetAwaiter().GetResult());
        var table = w.ActiveTable!;
        while (!table.RowsComplete) table.Tick();
        var source = w.NewDocument();
        source.Editor!.Document.Text = "same\nleft only\nsame\nchanged here\nsame\n";
        await w.Clipboard!.SetTextAsync("same\nsame\nchanged there\nsame\nright only\n");
        var diff = (Editing.DiffView)(await w.CompareWithClipboardAsync())!.Custom!;
        await diff.Session.RefreshAsync();
        w.UpdateLayout();
        Settle();

        foreach (var id in new[] { "monokai", "solarized-light", "nord" })
        {
            _app.Workbench.Set(EditorThemes.ThemeSetting, id);
            Settle();
            var theme = _app.EditorThemes.Active(ThemeVariant.Light);
            var colors = theme.ViewColors;
            Assert.Equal(colors["DiffAdded"], BrushColor(EditorLook.Brush(diff, "DiffAdded", Brushes.Transparent)));

            var frame = Frame(w, "compare-" + id);
            var area = AreaOf(diff, w);
            Assert.Equal(theme[EditorColorTheme.Background], MostOf(frame, area));
            Assert.True(CountOf(frame, area, colors["DiffAdded"]) > 50, $"{id}: no added rows in the colour of the theme");
            Assert.True(CountOf(frame, area, colors["DiffRemoved"]) > 50, $"{id}: no removed rows in the colour of the theme");

            w.ActiveGroup.Select(w.Tabs.First(t => t.Hex == hex));
            frame = Frame(w, "hex-" + id);
            Assert.Equal(theme[EditorColorTheme.Background], MostOf(frame, AreaOf(hex, w)));
            Assert.True(CountOf(frame, AreaOf(hex, w), colors["CurrentLine"]) > 50, $"{id}: no current row in the colour of the theme");

            w.ActiveGroup.Select(w.Tabs.First(t => t.Table == table));
            frame = Frame(w, "table-" + id);
            Assert.Equal(theme[EditorColorTheme.Background], MostOf(frame, AreaOf(table, w)));
            Assert.True(CountOf(frame, AreaOf(table, w), colors["AccentSoft"]) > 50, $"{id}: no header in the colour of the theme");
            w.ActiveGroup.Select(w.Tabs.First(t => t.Custom == diff));
        }

        // automatic keeps the brushes of the window theme, high contrast the colours of the system
        _app.Workbench.Set(EditorThemes.ThemeSetting, (System.Text.Json.Nodes.JsonNode?)null);
        Settle();
        Assert.True(diff.TryFindResource("DiffAdded", diff.ActualThemeVariant, out var resource));
        Assert.Same(resource, EditorLook.Brush(diff, "DiffAdded", Brushes.Transparent));
        _app.Workbench.Set(EditorThemes.ThemeSetting, "monokai");
        _app.Contrast.Apply(ContrastColors.Black);
        try
        {
            Settle();
            Assert.Equal(ContrastColors.Black.Window, BrushColor(EditorLook.Brush(diff, "BgEditor", Brushes.Transparent)));
            Assert.Equal(ContrastColors.Black.Window, MostOf(Frame(w, "compare-contrast"), AreaOf(diff, w)));
        }
        finally { _app.Contrast.Restore(); }
    }

    // ---- bold and italic ----

    private static IEnumerable<Avalonia.Media.TextFormatting.TextRunProperties> RunsOf(DocTab tab, int line, string fragment)
    {
        var view = tab.Editor!.TextArea.TextView;
        view.EnsureVisualLines();
        var visual = view.GetVisualLine(line)!;
        int column = tab.Editor.Document.GetLineByNumber(line).Offset;
        int at = tab.Editor.Document.Text.IndexOf(fragment, column, StringComparison.Ordinal) - column;
        return visual.Elements.Where(e => e.RelativeTextOffset < at + fragment.Length && at < e.RelativeTextOffset + e.DocumentLength).Select(e => (Avalonia.Media.TextFormatting.TextRunProperties)e.TextRunProperties);
    }

    [AvaloniaFact]
    public void Bold_and_italic_of_a_theme_and_of_the_toggles_show_in_the_editor_the_preview_and_every_highlighted_export()
    {
        var solarized = EditorColorTheme.BuiltIn.Single(t => t.Id == "solarized-dark");
        Assert.Equal(TokenStyle.Italic, solarized.Style("comment"));
        Assert.Equal(TokenStyle.Bold, solarized.Style("heading"));
        Assert.Equal(TokenStyle.Italic, EditorColorTheme.BuiltIn.Single(t => t.Id == "one-dark").Style("comment"));
        Assert.Empty(EditorColorTheme.Light.Styles);

        var w = _app.CreateWindow(null);
        w.Width = 1000;
        w.Height = 700;
        var tab = w.OpenFile(_temp.File("a.cs", CSharp), null);
        w.UpdateLayout();
        Settle();
        Assert.All(RunsOf(tab, 1, "comment"), r => Assert.Equal(FontStyle.Normal, r.Typeface.Style));

        _app.Workbench.Set(EditorThemes.ThemeSetting, "solarized-dark");
        Settle();
        Assert.All(RunsOf(tab, 1, "comment"), r => Assert.Equal(FontStyle.Italic, r.Typeface.Style));
        Assert.All(RunsOf(tab, 2, "using"), r => Assert.Equal(FontStyle.Normal, r.Typeface.Style));
        var view = OpenSettings();
        Assert.Equal(FontStyle.Italic, view.Preview!.RunOf("//")!.FontStyle);

        // the toggles: keyword bold over the theme, the comment plain although the theme makes it italic
        view.SetThemeStyle("keyword", TokenStyle.Bold);
        view.SetThemeStyle("comment", TokenStyle.None);
        Settle();
        Assert.Equal("keyword=bold;comment=normal", _app.Workbench.Settings.GetString(EditorThemes.ColorsSetting));
        Assert.All(RunsOf(tab, 2, "using"), r => Assert.Equal(FontWeight.Bold, r.Typeface.Weight));
        Assert.All(RunsOf(tab, 1, "comment"), r => Assert.Equal(FontStyle.Normal, r.Typeface.Style));
        Assert.Equal(FontWeight.Bold, view.Preview!.RunOf("function")!.FontWeight);
        Assert.NotEqual(FontStyle.Italic, view.Preview.RunOf("//")!.FontStyle);
        var toggles = view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().Where(t => t.Classes.Contains("themestyle") && Equals(t.Tag, "keyword")).ToList();
        Assert.Equal([true, false], toggles.Select(t => t.IsChecked == true));

        // a colour and a style of the same kind live side by side; the style the theme has itself needs no entry
        view.SetThemeColor("keyword", Red);
        view.SetThemeStyle("comment", TokenStyle.Italic);
        Assert.Equal("keyword=#FF0000 bold", _app.Workbench.Settings.GetString(EditorThemes.ColorsSetting));
        var look = _app.EditorThemes.Active(ThemeVariant.Light);
        Assert.Equal(TokenStyle.Bold, look.Style("keyword"));
        Assert.Equal(Red, look["keyword"]);

        // the exports write what the editor shows
        var theme = new ExportTheme(look[EditorColorTheme.Background], look[EditorColorTheme.Foreground], look.Palette);
        var tokenizer = LanguageModes.Tokenizer("csharp");
        string html = HighlightExport.Html(CSharp, tokenizer, null, theme);
        Assert.Contains("<span style=\"color:#FF0000;font-weight:bold\">using</span>", html);
        Assert.Matches("<span style=\"color:#[0-9A-F]{6};font-style:italic\">// comment", html);
        string rtf = HighlightExport.Rtf(CSharp, tokenizer, null, theme);
        Assert.Matches(@"\{\\cf\d+\\b using\}", rtf);
        Assert.Matches(@"\{\\cf\d+\\i // comment", rtf);
        string tex = HighlightExport.Tex(CSharp, tokenizer, null, theme);
        Assert.Contains(@"\textcolor[HTML]{FF0000}{\textbf{using}}", tex);
        Assert.Matches(@"\\textcolor\[HTML\]\{[0-9A-F]{6}\}\{\\textit\{// comment", tex);
        // the light theme has no styles, so the exports stay as they were
        Assert.DoesNotContain("font-style", HighlightExport.Html(CSharp, tokenizer, null, Theme()));

        view.Close();
    }

    [AvaloniaFact]
    public void Italic_comments_are_drawn_slanted_in_the_paged_view()
    {
        _app.Workbench.Set("files.largeFileThreshold", System.Text.Json.Nodes.JsonValue.Create(1));
        var w = _app.CreateWindow(null);
        w.Width = 900;
        w.Height = 500;
        var text = string.Concat(Enumerable.Repeat("// slanted comment text\nint value = 1;\n", 30_000));
        var tab = w.OpenFile(_temp.File("big.cs", text), null);
        Assert.NotNull(tab.Paged);
        for (int i = 0; i < 400 && !tab.Paged!.Index.Complete; i++) { Thread.Sleep(25); Dispatcher.UIThread.RunJobs(); }
        for (int i = 0; i < 100 && tab.Paged!.TokensOf(0) is null; i++) { Thread.Sleep(20); w.UpdateLayout(); Settle(); }
        Assert.Contains(tab.Paged!.TokensOf(0)!, t => t.Kind == "comment");

        _app.Workbench.Set(EditorThemes.ThemeSetting, "one-dark");
        Settle();
        var area = AreaOf(tab.Paged, w);
        var comment = _app.EditorThemes.Active(ThemeVariant.Light)["comment"];
        var slanted = Frame(w, "paged-italic");
        int italicPixels = CountOf(slanted, area, comment);
        _app.Workbench.Set(EditorThemes.ColorsSetting, "comment=normal");
        Settle();
        var upright = Frame(w, "paged-upright");
        int uprightPixels = CountOf(upright, area, comment);
        Assert.True(italicPixels > 100 && uprightPixels > 100, $"comment pixels {italicPixels} / {uprightPixels}");
        Assert.False(SamePixels(slanted, upright, area), "the comment lines look the same upright and italic");
    }

    private static bool SamePixels(Avalonia.Media.Imaging.Bitmap a, Avalonia.Media.Imaging.Bitmap b, Avalonia.Rect area)
    {
        static byte[] Bytes(Avalonia.Media.Imaging.Bitmap bitmap, out int rowBytes)
        {
            var frame = Assert.IsAssignableFrom<Avalonia.Media.Imaging.WriteableBitmap>(bitmap);
            using var buffer = frame.Lock();
            rowBytes = buffer.RowBytes;
            var data = new byte[buffer.RowBytes * buffer.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, data, 0, data.Length);
            return data;
        }
        var first = Bytes(a, out int row);
        var second = Bytes(b, out _);
        double scale = a.PixelSize.Width / a.Size.Width;
        for (int y = (int)(area.Top * scale); y < Math.Min(a.PixelSize.Height, (int)(area.Bottom * scale)); y++)
            if (!first.AsSpan(y * row + (int)(area.Left * scale) * 4, (int)(area.Width * scale) * 4).SequenceEqual(second.AsSpan(y * row + (int)(area.Left * scale) * 4, (int)(area.Width * scale) * 4))) return false;
        return true;
    }

    [AvaloniaFact]
    public async Task Bold_and_italic_come_over_from_TextMate_and_VS_Code_and_go_back_out_through_export()
    {
        var night = EditorColorTheme.FromTmTheme(TmTheme, "night", "night");
        Assert.Equal(TokenStyle.Italic, night.Style("comment")); // fontStyle italic on comment
        Assert.Equal(TokenStyle.None, night.Style("string"));

        const string styled = """
            {
              "name": "Styled",
              "type": "dark",
              "colors": { "editor.background": "#1E1E1E", "editor.foreground": "#D4D4D4" },
              "tokenColors": [
                { "scope": "keyword", "settings": { "foreground": "#C586C0", "fontStyle": "bold" } },
                { "scope": "keyword.control", "settings": { "foreground": "#D8A0DF" } },
                { "scope": "comment", "settings": { "foreground": "#8A9A7B", "fontStyle": "italic underline" } },
                { "scope": "entity.name.type", "settings": { "foreground": "#4EC9B0", "fontStyle": "bold italic" } },
                { "scope": "markup.heading", "settings": { "fontStyle": "bold" } },
                { "scope": "string", "settings": { "foreground": "#CE9178", "fontStyle": "italic" } },
                { "scope": "string.quoted", "settings": { "fontStyle": "" } }
              ]
            }
            """;
        var theme = EditorColorTheme.FromVsCode(styled, "styled", "Styled");
        Assert.Equal(TokenStyle.Bold, theme.Style("keyword")); // keyword.control gives the colour, the bold of keyword still covers it
        Assert.Equal(Color.Parse("#D8A0DF"), theme["keyword"]);
        Assert.Equal(TokenStyle.Italic, theme.Style("comment")); // underline is not drawn
        Assert.Equal(TokenStyle.Bold | TokenStyle.Italic, theme.Style("type"));
        Assert.Equal(TokenStyle.Bold, theme.Style("heading")); // a style without a colour
        Assert.Equal(TokenStyle.None, theme.Style("string")); // the empty style of string.quoted makes it plain again

        var back = EditorColorTheme.FromVsCode(theme.ToVsCodeJson(), "again", "x");
        Assert.Equal(theme.Styles, back.Styles);
        foreach (var key in EditorColorTheme.Keys) Assert.Equal(theme[key], back[key]);
        Assert.Contains("\"fontStyle\": \"bold italic\"", theme.ToVsCodeJson());

        // through the settings: imported, saved with a toggle on top, exported and imported again
        var view = OpenSettings();
        _dialogs.Picked.Add(_temp.File("styled.json", styled));
        await view.ImportThemeAsync();
        Assert.Equal("user:Styled", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
        view.SetThemeStyle("function", TokenStyle.Italic);
        _dialogs.Prompts.Enqueue("Styled 2");
        await view.SaveThemeAsync();
        var saved = _app.EditorThemes.Active(ThemeVariant.Light);
        Assert.Equal(TokenStyle.Italic, saved.Style("function"));
        Assert.Equal(TokenStyle.Bold | TokenStyle.Italic, saved.Style("type"));
        string exported = Path.Combine(_temp.Path, "styled-out.json");
        _dialogs.SaveFiles.Enqueue(exported);
        await view.ExportThemeAsync();
        var reread = EditorColorTheme.Load(exported, "x");
        Assert.Equal(saved.Styles, reread.Styles);
        view.Close();
        _app = TestApp.Restart(_temp, _app, _dialogs);
        Assert.Equal(TokenStyle.Italic, _app.EditorThemes.Active(ThemeVariant.Light).Style("function"));
    }

    // ---- include and semantic colours ----

    [AvaloniaFact]
    public async Task A_VS_Code_theme_reads_the_files_it_includes_next_to_it_refuses_cycles_and_says_why_semantic_colours_are_left_out()
    {
        string folder = Path.Combine(_temp.Path, "ext", "themes");
        Directory.CreateDirectory(Path.Combine(folder, "parts"));
        File.WriteAllText(Path.Combine(folder, "base.json"), """
            { "colors": { "editor.background": "#10141C", "editor.foreground": "#C8CCD4" },
              "tokenColors": [ { "scope": "keyword", "settings": { "foreground": "#FF6688", "fontStyle": "bold" } }, { "scope": "string", "settings": { "foreground": "#88CC66" } } ] }
            """);
        File.WriteAllText(Path.Combine(folder, "parts", "colors.tmTheme"), TmTheme);
        string child = Path.Combine(folder, "child.json");
        File.WriteAllText(child, """
            { "name": "Child", "type": "dark", "include": "./base.json",
              "colors": { "editor.foreground": "#D0D4DC" },
              "tokenColors": [ { "scope": "string", "settings": { "foreground": "#AADD88" } } ],
              "semanticHighlighting": true,
              "semanticTokenColors": { "variable.readonly": "#FFCC00" } }
            """);
        var theme = EditorColorTheme.Load(child, "child");
        Assert.Equal(Color.Parse("#10141C"), theme[EditorColorTheme.Background]); // from the included file
        Assert.Equal(Color.Parse("#D0D4DC"), theme[EditorColorTheme.Foreground]); // the including file wins
        Assert.Equal(Color.Parse("#FF6688"), theme["keyword"]);
        Assert.Equal(TokenStyle.Bold, theme.Style("keyword"));
        Assert.Equal(Color.Parse("#AADD88"), theme["string"]); // a later rule wins on a tie
        Assert.True(theme.SemanticColorsIgnored);

        // token colours in a TextMate file beside the theme
        string referencing = Path.Combine(folder, "ref.json");
        File.WriteAllText(referencing, """{ "name": "Ref", "type": "dark", "tokenColors": "./parts/colors.tmTheme" }""");
        var fromFile = EditorColorTheme.Load(referencing, "ref");
        Assert.Equal(Color.Parse("#7F8C8D"), fromFile["comment"]);
        Assert.Equal(TokenStyle.Italic, fromFile.Style("comment"));
        Assert.Equal(Color.Parse("#101820"), fromFile[EditorColorTheme.Background]); // the global settings of the TextMate file
        Assert.False(fromFile.SemanticColorsIgnored);

        // a cycle, a file outside the folder, a missing file and a chain too deep are refused with the reason
        File.WriteAllText(Path.Combine(folder, "a.json"), """{ "include": "./b.json", "colors": { "editor.background": "#000000" } }""");
        File.WriteAllText(Path.Combine(folder, "b.json"), """{ "include": "a.json" }""");
        Assert.Contains("includes itself", Assert.Throws<FormatException>(() => EditorColorTheme.Load(Path.Combine(folder, "a.json"), "a")).Message);
        File.WriteAllText(Path.Combine(_temp.Path, "ext", "outside.json"), """{ "colors": { "editor.background": "#000000" } }""");
        File.WriteAllText(Path.Combine(folder, "up.json"), """{ "include": "../outside.json" }""");
        Assert.Contains("outside the folder", Assert.Throws<FormatException>(() => EditorColorTheme.Load(Path.Combine(folder, "up.json"), "up")).Message);
        File.WriteAllText(Path.Combine(folder, "lost.json"), """{ "include": "./nowhere.json" }""");
        Assert.Contains("not found", Assert.Throws<FormatException>(() => EditorColorTheme.Load(Path.Combine(folder, "lost.json"), "lost")).Message);
        for (int i = 0; i <= EditorColorTheme.MaxIncludeDepth + 1; i++)
            File.WriteAllText(Path.Combine(folder, $"deep{i}.json"), i == EditorColorTheme.MaxIncludeDepth + 1 ? """{ "colors": { "editor.background": "#000000" } }""" : $$"""{ "include": "./deep{{i + 1}}.json" }""");
        Assert.Contains("levels deep", Assert.Throws<FormatException>(() => EditorColorTheme.Load(Path.Combine(folder, "deep0.json"), "deep")).Message);
        Assert.Equal(Color.Parse("#000000"), EditorColorTheme.Load(Path.Combine(folder, "deep1.json"), "deep")[EditorColorTheme.Background]); // exactly the limit is read

        // the import through the settings says why the semantic colours are left out
        var view = OpenSettings();
        _dialogs.Picked.Add(child);
        await view.ImportThemeAsync();
        Assert.Equal("user:Child", _app.Workbench.Settings.GetString(EditorThemes.ThemeSetting));
        Settle();
        var notice = view.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(t => t.Classes.Contains("themenotice"));
        Assert.Contains(Loc.Get(K.ColorThemeSemanticIgnored), notice.Text);
        // the saved custom theme stands alone: it needs neither the included file nor the folder
        File.Delete(Path.Combine(folder, "base.json"));
        Assert.Equal(Color.Parse("#FF6688"), EditorColorTheme.Load(Path.Combine(_app.EditorThemes.Folder, "Child.json"), "x")["keyword"]);
        view.Close();
    }

    [AvaloniaFact]
    public async Task The_import_notice_stays_through_page_builds_until_dismissed_or_the_page_is_left()
    {
        string file = Path.Combine(_temp.Path, "Night.tmTheme");
        File.WriteAllText(file, TmTheme);
        var view = OpenSettings(); // the search box reports its clearing later: that build comes after the import
        _dialogs.Picked.Add(file);
        await view.ImportThemeAsync();
        Avalonia.Controls.TextBlock? Notice() => view.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().SingleOrDefault(t => t.Classes.Contains("themenotice") && t.IsEffectivelyVisible);
        string expected = Loc.Get(K.ColorThemeImported, ("name", SettingsWindow.ThemeLabel(_app.Workbench.Settings.GetString(EditorThemes.ThemeSetting))));
        Settle();
        view.Refresh();
        view.Refresh();
        Settle();
        Assert.Equal(expected, Notice()?.Text);

        // the close button beside it dismisses it for good
        var close = view.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => Avalonia.Automation.AutomationProperties.GetName(b) == Loc.Get(K.DialogClose) && b.IsEffectivelyVisible);
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Assert.Null(Notice());
        view.Refresh();
        Settle();
        Assert.Null(Notice());

        // another page drops it
        _dialogs.Picked.Add(file);
        await view.ImportThemeAsync();
        Settle();
        Assert.NotNull(Notice());
        view.SelectCategory(SettingCategories.EditorDisplay);
        Settle();
        view.SelectCategory(SettingCategories.Appearance);
        Settle();
        Assert.Null(Notice());
        view.Close();
    }
}

