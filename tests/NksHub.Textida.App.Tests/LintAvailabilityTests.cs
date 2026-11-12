using System.Text.Json.Nodes;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public class LintAvailabilityTests : IDisposable
{
    private const string Command = "nks.tools.lint";
    private readonly TempDir temp = new();
    private readonly ScriptedDialogs dialogs = new();
    private readonly App app;
    private readonly MainWindow window;

    public LintAvailabilityTests()
    {
        app = TestApp.Prepare(temp, dialogs);
        window = app.CreateWindow(null, look: TestApp.WindowsLook);
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temp.Dispose();
    }

    private void AssertOffered(char expected)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var context = new ShellContext(window, forMenu: true);
        var menu = window.GetVisualDescendants().OfType<Menu>().Single();
        static IEnumerable<MenuItem> Walk(ItemsControl root) => root.Items.OfType<MenuItem>()
            .Where(i => i.IsVisible).SelectMany(i => Walk(i).Prepend(i));
        var items = Walk(menu).Where(i => i.Tag is MenuCommand { CommandId: Command }).ToArray();
        Assert.Equal(expected != '-', items.Length > 0);
        Assert.All(items, i =>
        {
            Assert.Equal(expected == 'S', i.IsEnabled);
            Assert.Equal(expected == 'S', ControlAutomationPeer.CreatePeerForElement(i).IsEnabled());
        });
        Assert.True(app.Workbench.Commands.TryGet(Command, out var command));
        Assert.Equal(expected != '-', command.IsAvailable(context));
        Assert.Equal(expected == 'S', command.IsAvailable(context) && command.IsEnabled(context));
    }

    [AvaloniaTheory]
    [InlineData("a.py", true)]
    [InlineData("a.PY", true)]
    [InlineData("a.ts", true)]
    [InlineData("a.sql", true)]
    [InlineData("a.sh", true)]
    [InlineData("a.zsh", false)]
    [InlineData("a.cs", false)]
    [InlineData("a.html", false)]
    [InlineData("package.json", false)]
    public void Saved_files_use_their_extension_even_after_choosing_another_language(string name, bool supported)
    {
        var tab = window.OpenFile(temp.File(name, "print(1)\n"), null);
        AssertOffered(supported ? 'S' : '-');
        tab.Doc.ChooseLanguage(LanguageModes.ById(supported ? "plaintext" : "python"));
        AssertOffered(supported ? 'S' : '-');
    }

    [AvaloniaFact]
    public async Task Untitled_python_needs_a_name_and_save_as_rebuilds_the_menu_from_the_new_extension()
    {
        var tab = window.NewDocument();
        tab.Doc.ChooseLanguage(LanguageModes.ById("python"));
        tab.Editor!.Document.Text = "print(1)\n";
        AssertOffered('G');
        Assert.Equal(CommandOutcome.Disabled, await app.Workbench.Commands.ExecuteAsync(Command, new ShellContext(window, forMenu: true)));
        dialogs.SaveAs.Enqueue(new SaveAsChoice(Path.Combine(temp.Path, "a.txt"), TextCodec.Utf8));
        await window.SaveAsAsync(tab);
        AssertOffered('-');
        dialogs.SaveAs.Enqueue(new SaveAsChoice(Path.Combine(temp.Path, "a.py"), TextCodec.Utf8));
        await window.SaveAsAsync(tab);
        AssertOffered('S');
    }

    [AvaloniaFact]
    public void Hex_hides_the_linter_and_returning_to_text_restores_it()
    {
        var tab = window.OpenFile(temp.File("a.py", "print(1)\n"), null);
        AssertOffered('S');
        Assert.True(window.SetHex(tab.Doc, true));
        AssertOffered('-');
        Assert.True(window.SetHex(tab.Doc, false));
        AssertOffered('S');
    }

    [AvaloniaFact]
    public void The_paged_editor_keeps_lint_available_for_a_saved_python_file()
    {
        app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        var tab = window.OpenFile(temp.File("large.py", new string(' ', 1_100_000)), null);
        Assert.NotNull(tab.Paged);
        AssertOffered('S');
    }
}
