using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class DocumentDataAutomationLifecycleTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly ScriptedDialogs dialogs = new();
    private readonly App app;
    private readonly MainWindow window;
    private readonly ITestOutputHelper output;

    public DocumentDataAutomationLifecycleTests(ITestOutputHelper output)
    {
        this.output = output;
        app = TestApp.Prepare(temporary, dialogs);
        Loc.SetLanguage("en");
        window = app.CreateWindow(null);
        window.Width = 1600; window.Height = 1000;
        window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temporary.Dispose();
    }

    [AvaloniaFact]
    public async Task Warmed_window_automation_keeps_structure_and_toolbar_after_graph_edit_source_undo_and_split()
    {
        string text = "{\"alpha\":1,\"bravo\":{\"label\":\"sample\",\"enabled\":true},\"rows\":["
            + string.Join(',', Enumerable.Range(0, 160)) + "]}";
        var tab = window.OpenFile(temporary.Bytes("workspace.json", TextCodec.Utf8.Encode(text)), null);
        var document = tab.Doc.Doc;
        var editor = Assert.IsType<EditorSurface>(tab.Surface);
        WarmWindow();

        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Tree));
        var view = Assert.IsType<StructureView>(tab.Structure);
        await Drain(view);
        AssertExposed(view.Tree, "initial tree");
        AssertExposed(view.FindButton, "initial toolbar");
        WarmParents(view.Tree);

        var root = Assert.Single(view.Tree.VisibleNodes);
        root.IsExpanded = true;
        await Drain(view);
        var alpha = Assert.Single(view.Tree.VisibleNodes, node => node.DisplayKey == "alpha");
        view.Tree.SelectedNode = alpha;
        await Drain(view);

        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Table));
        await Drain(view);
        AssertExposed(view.Table, "table");
        WarmParents(view.Table);
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Graph));
        await Drain(view);
        AssertExposed(view.Graph, "graph");
        WarmParents(view.Graph);

        dialogs.Prompts.Enqueue("42");
        Assert.True(view.EditButton.IsEnabled);
        view.EditButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Drain(view);
        Assert.Contains("\"alpha\":42", editor.Document.Text, StringComparison.Ordinal);
        Assert.True(document.CanUndo);
        WarmWindow();

        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Text));
        StructureLayoutTests.Layout(window);
        WarmWindow();
        WarmParents(editor.TextArea);
        if (OperatingSystem.IsWindows())
        {
            var nativeHost = Assert.Single(tab.Root.Children.OfType<EditorAutomationHost>());
            Assert.True(nativeHost.IsVisible);
            output.WriteLine("Native editor proxy peer in this platform: "
                + (nativeHost.CreateProxyPeer()?.GetType().FullName ?? "unavailable in headless platform"));
        }
        Assert.NotNull(document.Undo());
        Assert.Equal(text, editor.Document.Text);
        WarmWindow();

        Assert.True(window.SetViewMode(tab.Doc, ViewMode.StructureSplit));
        await Drain(view);
        Assert.Same(view, tab.Structure);
        Assert.True(tab.StructureShown);
        Assert.True(tab.View.IsVisible);
        Assert.True(view.Tree.IsEffectivelyVisible);
        Assert.True(view.Tree.Bounds.Width > 100 && view.Tree.Bounds.Height > 100);
        AssertExposed(view.Tree, "split tree after source undo");
        AssertExposed(view.FindButton, "split find toolbar");
        AssertExposed(view.CopyPathButton, "split copy toolbar");
        WarmParents(view.Tree);
        Assert.Empty(dialogs.Prompts);
    }

    private Task Drain(StructureView view) => StructureLayoutTests.Drain(view, window);

    private IReadOnlyList<AutomationPeer> WarmWindow()
    {
        var result = new List<AutomationPeer>();
        var seen = new HashSet<AutomationPeer>();
        var pending = new Stack<AutomationPeer>();
        pending.Push(ControlAutomationPeer.CreatePeerForElement(window)!);
        while (pending.TryPop(out var peer))
        {
            if (!seen.Add(peer)) continue;
            result.Add(peer);
            foreach (var child in peer.GetChildren()) pending.Push(child);
        }
        return result;
    }

    private void AssertExposed(Control control, string stage)
    {
        var expected = ControlAutomationPeer.CreatePeerForElement(control)!;
        var peers = WarmWindow();
        Assert.True(peers.Contains(expected), stage + " missing from fresh main peer traversal: "
            + string.Join(" | ", peers.Select(peer => peer.GetClassName() + ":" + peer.GetAutomationId())));
        Assert.False(expected.IsOffscreen());
        Assert.True(expected.GetBoundingRectangle().Width > 0 && expected.GetBoundingRectangle().Height > 0);
    }

    private static void WarmParents(Control control)
    {
        foreach (var ancestor in control.GetSelfAndVisualAncestors().OfType<Control>())
            ControlAutomationPeer.CreatePeerForElement(ancestor)?.GetChildren();
        var peer = ControlAutomationPeer.CreatePeerForElement(control)!;
        var seen = new HashSet<AutomationPeer>();
        while (peer.GetParent() is { } parent && seen.Add(parent))
        {
            Assert.Contains(peer, parent.GetChildren());
            peer = parent;
        }
    }
}
