using System.Text;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.PluginHost.Manifest;

namespace NksHub.Textida.App.Tests;

/// <summary>P4.7.1: a highlighter from a data plugin colours files by its extension without a process, and an invalid rule keeps the plugin out with the reason.</summary>
[Collection("LocState")]
public class UserHighlighterTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    private const string Header = "\"schemaVersion\":1,\"id\":\"acme.lang\",\"name\":\"Acme language\",\"version\":\"1.0.0\",\"api\":{\"min\":1,\"max\":1}";
    private const string Grammar = "{\"formatVersion\":1,\"id\":\"acme-lang\",\"name\":\"Acme\",\"extensions\":[\".acme\"],\"fileNames\":[\"Acmefile\"],\"comments\":{\"line\":\"#\"}," +
        "\"rules\":[{\"match\":\"#.*\",\"token\":\"comment\"}]}";

    public UserHighlighterTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    private void Install(string grammar)
    {
        var dir = Path.Combine(_app.ProfileDirectory, "plugins", "acme");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PluginManifest.FileName), "{" + Header + ",\"contributes\":{\"grammars\":[\"acme.json\"]}}", Encoding.UTF8);
        File.WriteAllText(Path.Combine(dir, "acme.json"), grammar, Encoding.UTF8);
        _app.ReloadClips();
    }

    [AvaloniaFact]
    public void A_highlighter_from_a_data_plugin_selects_files_by_extension_and_file_name_and_colours_them()
    {
        Install(Grammar);
        Assert.Empty(_app.Clips.Problems);
        Assert.Equal("acme-lang", LanguageModes.ForPath("x.acme").Id);
        Assert.Equal("acme-lang", LanguageModes.ForPath("C:\\work\\Acmefile").Id);
        Assert.Equal("#", LanguageModes.ById("acme-lang").Comments.Line);
        Assert.Equal("acme-lang", LanguageModes.GrammarId(LanguageModes.ById("acme-lang")));

        var tab = _w.OpenFile(_temp.File("a.acme", "word\n# note\n"), null);
        _w.UpdateLayout();
        for (int i = 0; i < 20; i++) Dispatcher.UIThread.RunJobs();
        Assert.Equal("Acme", tab.Doc.Language.Name);
        Assert.True(tab.Syntax!.Active);
        tab.Editor!.TextArea.TextView.EnsureVisualLines();
        var colours = tab.Editor.TextArea.TextView.GetVisualLine(2)!.Elements.Select(e => e.TextRunProperties.ForegroundBrush).OfType<ISolidColorBrush>().Select(b => b.Color).ToHashSet();
        var comment = (ISolidColorBrush)SyntaxPalette.Light.GetBrush("comment")!;
        var dark = (ISolidColorBrush)SyntaxPalette.Dark.GetBrush("comment")!;
        Assert.Contains(colours, c => c == comment.Color || c == dark.Color);
    }

    [AvaloniaFact]
    public void An_invalid_rule_keeps_the_plugin_out_and_the_mode_does_not_appear()
    {
        Install(Grammar.Replace("#.*", "(#"));
        Assert.Single(_app.Clips.Problems);
        Assert.Contains("Grammar 'acme.json'", _app.Clips.Problems[0]);
        Assert.Equal(LanguageModes.PlainId, LanguageModes.ForPath("x.acme").Id);
        Assert.DoesNotContain(LanguageModes.All, m => m.Id == "acme-lang");
    }

    [AvaloniaFact]
    public void A_highlighter_id_that_another_plugin_already_uses_is_reported()
    {
        Install(Grammar);
        var other = Path.Combine(_app.ProfileDirectory, "plugins", "zeta");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, PluginManifest.FileName), "{" + Header.Replace("acme.lang", "zeta.lang") + ",\"contributes\":{\"grammars\":[\"g.json\"]}}", Encoding.UTF8);
        File.WriteAllText(Path.Combine(other, "g.json"), Grammar, Encoding.UTF8);
        _app.ReloadClips();
        Assert.Single(_app.Clips.Problems);
        Assert.Contains("already used", _app.Clips.Problems[0]);
        Assert.Single(LanguageModes.All, m => m.Id == "acme-lang");
    }
}
