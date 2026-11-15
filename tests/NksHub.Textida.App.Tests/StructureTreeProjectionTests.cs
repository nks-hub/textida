using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureTreeProjectionTests
{
    [AvaloniaFact]
    public async Task Collapsing_a_subtree_keeps_surviving_containers_and_their_cached_peers()
    {
        var parent = Node("A");
        parent.HasChildren = true;
        parent.IsExpanded = true;
        parent.Items.AddRange([Node("a1"), Node("a2")]);
        var second = Node("B");
        var third = Node("C");
        var tree = new StructureTree();
        tree.Items.AddRange([parent, second, third]);
        var window = new Window { Width = 360, Height = 300, Content = tree };
        try
        {
            window.Show();
            await Drain(window);
            var treePeer = ControlAutomationPeer.CreatePeerForElement(tree)!;
            Assert.Equal(AutomationControlType.Tree, treePeer.GetAutomationControlType());
            Assert.True(tree.AutomationRequested);
            tree.SelectAndReveal(second);
            await Drain(window);
            var secondRow = Assert.IsType<StructureTreeRow>(tree.TryGetRealizedRow(second));
            var thirdRow = Assert.IsType<StructureTreeRow>(tree.TryGetRealizedRow(third));
            var secondPeer = ControlAutomationPeer.CreatePeerForElement(secondRow)!;
            var thirdPeer = ControlAutomationPeer.CreatePeerForElement(thirdRow)!;
            long secondVersion = second.BindingVersion, thirdVersion = third.BindingVersion;
            int created = tree.CreatedRowCount;
            Assert.True(secondRow.IsFocused);

            parent.IsExpanded = false;
            await Drain(window);

            Assert.Equal(new[] { parent, second, third }, tree.VisibleNodes);
            Assert.Same(secondRow, tree.TryGetRealizedRow(second));
            Assert.Same(thirdRow, tree.TryGetRealizedRow(third));
            Assert.Equal(created, tree.CreatedRowCount);
            Assert.Same(second, tree.SelectedNode);
            Assert.True(secondRow.IsFocused);
            Assert.Same(secondPeer, ControlAutomationPeer.CreatePeerForElement(secondRow));
            Assert.Same(thirdPeer, ControlAutomationPeer.CreatePeerForElement(thirdRow));
            Assert.Equal(secondVersion, secondRow.BindingVersion);
            Assert.Equal(thirdVersion, thirdRow.BindingVersion);

            parent.IsExpanded = true;
            await Drain(window);

            Assert.Same(secondRow, tree.TryGetRealizedRow(second));
            Assert.Same(thirdRow, tree.TryGetRealizedRow(third));
            Assert.Same(secondPeer, ControlAutomationPeer.CreatePeerForElement(secondRow));
            Assert.Same(thirdPeer, ControlAutomationPeer.CreatePeerForElement(thirdRow));
            var selection = Assert.IsAssignableFrom<ISelectionItemProvider>(thirdPeer.GetProvider<ISelectionItemProvider>());
            selection.Select();
            await Drain(window);
            Assert.Same(third, tree.SelectedNode);
            Assert.True(thirdRow.IsFocused);
            Assert.True(selection.IsSelected);
        }
        finally { window.Close(); }
    }

    private static StructureTreeNode Node(string name) => new() { Heading = name, Tag = new object() };

    private static async Task Drain(Window window)
    {
        for (int index = 0; index < 3; index++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            await Task.Delay(5);
        }
    }
}
