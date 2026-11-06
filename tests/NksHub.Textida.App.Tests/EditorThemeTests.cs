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
}
