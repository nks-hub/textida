using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.5.3: an imported TextMate grammar becomes a language that colours a file by its extension, and the warnings are listed.</summary>
[Collection("LocState")]
public class TextMateAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public TextMateAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        LanguageModes.UseUserGrammars(new GrammarRegistry(BuiltinGrammars.Registry));
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task An_imported_grammar_colours_files_of_its_extension_and_its_warnings_are_in_the_tasks_tab()
    {
        string grammar = _temp.File("toy.tmLanguage.json", """
            { "name": "Toy", "scopeName": "source.toy", "fileTypes": ["toy"],
              "patterns": [ { "match": "\\b(if|else)\\b", "name": "keyword.control.toy" }, { "begin": "^```", "while": "^>", "name": "markup.raw.toy" } ] }
            """);
        var w = _app.CreateWindow(null);

        await w.ImportTextMateAsync(grammar);

        Assert.Equal("toy", LanguageModes.ForPath("a.toy").Id);
        var tab = w.OpenFile(_temp.File("a.toy", "if x else y\n"), null);
        Assert.Equal("toy", tab.Doc.Language.Id);
        var lines = _app.Output.Read(OutputChannel.Tasks).Select(l => l.Text).ToList();
        Assert.Contains(lines, l => l.Contains("toy"));
        Assert.Contains(lines, l => l.Contains("while"));

        await w.ImportTextMateAsync(_temp.File("broken.json", "{ nope"));
        Assert.Contains("broken.json", w.StatusMessageText);
    }
}
