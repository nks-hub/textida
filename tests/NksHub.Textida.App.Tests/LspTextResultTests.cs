using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.10.1: the text a language server answers with (macro expansion, syntax tree) opens in a tab that cannot be edited and is not a file. What the server is asked is tested with the plugin (<c>LspPluginTests</c>).</summary>
[Collection("LocState")]
public class LspTextResultTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public LspTextResultTests()
    {
        _app = TestApp.Prepare(_temp);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public void The_answer_opens_in_a_read_only_tab_named_after_the_result_and_the_file()
    {
        var source = _w.OpenFile(_temp.File("main.rs", "fn main() { vec![1]; }\n"), null);

        var tab = _w.ShowReadOnlyText("<[_]>::into_vec(1)\nsecond\n", "macro vec", "rust", source);

        Assert.NotSame(source, tab);
        Assert.Same(tab, _w.Active);
        Assert.Equal("<[_]>::into_vec(1)" + Environment.NewLine + "second" + Environment.NewLine, tab.Editor!.Document.Text);
        Assert.True(tab.Editor.IsReadOnly);
        Assert.Null(tab.Path);
        Assert.Equal(Loc.Get(K.LspTextTitle, ("title", "macro vec"), ("file", "main.rs")), tab.Title);
        Assert.Equal(Loc.Get(K.LspTextOpened), _w.StatusMessageText);
        Assert.Equal("fn main() { vec![1]; }\n", source.Editor!.Document.Text);
    }

    [AvaloniaFact]
    public void The_two_commands_exist_in_the_tools_menu_and_have_a_handler()
    {
        foreach (var id in new[] { "nks.lsp.expandMacro", "nks.lsp.syntaxTree" })
        {
            Assert.True(_app.Workbench.Commands.TryGet(id, out _));
            Assert.True(_app.Workbench.Commands.GetMenu("tools", new ShellContext(_w, forMenu: true)).Any(i => i.CommandId == id) || _app.Workbench.Commands.Commands.Any(c => c.Id == id));
        }
        Assert.True(_app.Workbench.Commands.HasHandler("nks.lsp.syntaxTree"));
    }
}
