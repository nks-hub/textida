using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Tests;

/// <summary>P8.5.6: the editor draws the inlay hints and semantic tokens plugins publish, and leaves the text itself alone.</summary>
[Collection("LocState")]
public class DecorationsSurfaceTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;

    public DecorationsSurfaceTests() => _app = TestApp.Prepare(_temp);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public void Inlay_hints_and_semantic_tokens_are_kept_for_the_text_they_fit_and_change_no_character_of_it()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("deco.txt", "class Box\nrun(1)\n"), null);
        var surface = tab.Editor!;
        string before = surface.Document.Text;

        surface.SetInlayHints([(8, "a:"), (999, "outside"), (9, "")]);
        surface.SetSemanticTokens([(0, 5, "keyword"), (6, 9, "class"), (2, 99, "outside"), (12, 12, "empty")]);

        Assert.Equal([(8, "a:")], surface.InlayHints);
        Assert.Equal([(0, 5, "keyword"), (6, 9, "type")], surface.SemanticTokens); // a type name is coloured as the kind of syntax it stands for
        Assert.Equal(before, surface.Document.Text);

        surface.SetInlayHints([]);
        surface.SetSemanticTokens([]);
        Assert.Empty(surface.InlayHints);
        Assert.Empty(surface.SemanticTokens);
    }

    [AvaloniaTheory]
    [InlineData("keyword", "keyword")]
    [InlineData("method", "function")]
    [InlineData("parameter", "variable")]
    [InlineData("enumMember", "property")]
    [InlineData("namespace", "type")]
    [InlineData("string", "string")]
    public void A_token_type_is_coloured_as_the_kind_of_syntax_it_stands_for(string type, string kind) => Assert.Equal(kind, SemanticTokenColorizer.KindOf(type));
}
