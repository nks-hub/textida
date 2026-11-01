using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Editing;
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

    [AvaloniaFact]
    public void The_signature_popup_follows_the_arguments_and_closes_on_Esc_and_when_the_caret_leaves_the_call()
    {
        var w = _app.CreateWindow(null);
        w.Width = 800;
        w.Height = 500;
        var tab = w.OpenFile(_temp.File("call.txt", "x = run(1, f(2), 3) + 4\n"), null);
        var surface = tab.Editor!;
        w.UpdateLayout();
        surface.TextArea.Focus();
        var info = new SignatureInfo("run(a: int, b: string, c: int)", [(4, 10), (12, 21), (23, 29)], 0, "Calls run.");

        Assert.False(surface.ShowSignature(info)); // the caret is not inside a call
        surface.CaretOffset = "x = run(1".Length;
        Assert.True(surface.ShowSignature(info));
        Assert.Same(info, surface.Signature);

        surface.CaretOffset = "x = run(1, f(2".Length; // inside a call of its own: still the second argument of run
        Assert.NotNull(surface.Signature);
        Assert.Equal(1, EditorSurface.ArgumentAt(surface.Document.Text, 7, surface.CaretOffset));
        surface.CaretOffset = "x = run(1, f(2), 3".Length;
        Assert.Equal(2, EditorSurface.ArgumentAt(surface.Document.Text, 7, surface.CaretOffset));

        surface.CaretOffset = "x = run(1, f(2), 3) + ".Length; // behind the closing bracket
        Assert.Null(surface.Signature);

        surface.CaretOffset = "x = run(1".Length;
        Assert.True(surface.ShowSignature(info));
        surface.TextArea.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape });
        Assert.Null(surface.Signature);

        Assert.True(surface.ShowSignature(info));
        surface.Document.Remove(7, 1); // the opening bracket is gone
        Assert.Null(surface.Signature);
    }

    [AvaloniaFact]
    public void A_line_with_inlay_hints_is_as_tall_as_a_line_without_them()
    {
        var w = _app.CreateWindow(null);
        w.Width = 800;
        w.Height = 500;
        var tab = w.OpenFile(_temp.File("hints.cs", "int total = run(1, 2);\nint plain = other;\nint again = run(3, 4);\nend\n"), null);
        var surface = tab.Editor!;
        w.UpdateLayout();
        var view = surface.TextArea.TextView;
        view.EnsureVisualLines();
        double plain = view.GetVisualTopByDocumentLine(2) - view.GetVisualTopByDocumentLine(1);

        string text = surface.Document.Text;
        surface.SetInlayHints([(text.IndexOf("run(1", StringComparison.Ordinal) + 4, "a:"), (text.IndexOf(" 2)", StringComparison.Ordinal) + 1, "b:"), (text.IndexOf("run(3", StringComparison.Ordinal) + 4, "a:")]);
        w.UpdateLayout();
        view.EnsureVisualLines();

        Assert.Equal(plain, view.GetVisualTopByDocumentLine(2) - view.GetVisualTopByDocumentLine(1), 0.5); // two hints on the line
        Assert.Equal(plain, view.GetVisualTopByDocumentLine(3) - view.GetVisualTopByDocumentLine(2), 0.5);
        Assert.Equal(plain, view.GetVisualTopByDocumentLine(4) - view.GetVisualTopByDocumentLine(3), 0.5); // one hint
    }

    [AvaloniaFact]
    public void The_signature_popup_is_put_above_the_line_of_the_caret_when_there_is_room_and_below_it_when_there_is_not()
    {
        var w = _app.CreateWindow(null);
        w.Width = 800;
        w.Height = 500;
        var tab = w.OpenFile(_temp.File("place.txt", string.Concat(Enumerable.Range(1, 12).Select(i => $"line {i}\n")) + "x = run(1, 2)\n"), null);
        var surface = tab.Editor!;
        w.UpdateLayout();
        surface.TextArea.Focus();
        surface.TextArea.TextView.EnsureVisualLines();
        var info = new SignatureInfo("run(a: int, b: int)", [(4, 10), (12, 18)], 0, null);

        surface.CaretOffset = surface.Document.GetLineByNumber(13).Offset + "x = run(1".Length;
        Assert.True(surface.ShowSignature(info));
        Assert.Equal(Avalonia.Controls.PlacementMode.Top, surface.SignaturePopup!.Placement); // the next line stays readable

        surface.CloseSignature();
        surface.Document.Insert(0, "y = run(1\n");
        surface.CaretOffset = "y = run(1".Length;
        Assert.True(surface.ShowSignature(info));
        Assert.Equal(Avalonia.Controls.PlacementMode.Bottom, surface.SignaturePopup!.Placement); // the first line has nothing above it
    }

    [AvaloniaTheory]
    [InlineData("f(a, (b, c), d", 1, 2)]
    [InlineData("f(a, b)", -1, -1)]
    public void The_bracket_before_the_caret_and_the_argument_are_found_by_counting_brackets(string text, int open, int argument)
    {
        int found = EditorSurface.OpenBracketBefore(text, text.Length);
        Assert.Equal(open == -1 ? -1 : 1, found);
        if (found >= 0) Assert.Equal(argument, EditorSurface.ArgumentAt(text, found, text.Length));
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
