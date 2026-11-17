using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data.Structure;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class StructureLayoutTests
{
    [AvaloniaTheory]
    [InlineData("en", 420, 700)]
    [InlineData("de", 420, 700)]
    [InlineData("cs", 420, 700)]
    [InlineData("sk", 420, 700)]
    [InlineData("en", 1100, 600)]
    public async Task Compact_commands_and_closed_help_leave_the_document_height_to_the_tree(string language, int width, int height)
    {
        using var document = Doc.FromBytes(TextCodec.Utf8.Encode("{\"name\":\"sample\",\"items\":[1,2]}"),
            new PagedBackendFactory(startIndexing: false), new DocumentOptions { Format = TextCodec.Utf8 });
        using var view = new StructureView(() => new(document, document.Id, document.Revision, document.Codec, 0,
            StructureFormat.Json, 0, false, document.GetSnapshot, _ => { }, (_, _, _) => Task.FromResult(false)),
            _ => Task.CompletedTask, (_, _, _) => Task.FromResult<string?>(null));
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            Loc.SetLanguage(language); view.ApplyTexts(); window.Show(); view.Sync();
            await Drain(view, window);
            var clear = view.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == Loc.Get(K.StructureClear));
            Assert.False(BoundsIn(view.SearchScope, view).Intersects(BoundsIn(view.Filter, view)));
            Assert.False(BoundsIn(view.Filter, view).Intersects(BoundsIn(view.FindButton, view)));
            Assert.False(BoundsIn(view.FindButton, view).Intersects(BoundsIn(clear, view)));
            var tree = BoundsIn(view.Tree, view);
            Assert.True(new Rect(view.Bounds.Size).Inflate(0.5).Contains(tree));
            Assert.True(tree.Height > view.Bounds.Height * 0.70, $"Tree has only {tree.Height} of {view.Bounds.Height} pixels.");
            Assert.DoesNotContain(view.TypeCounts, view.GetVisualDescendants());
            Assert.DoesNotContain(view.Filter.GetVisualAncestors(), item => item is ScrollViewer);
            Assert.IsType<StructureIcon>(view.CopyPathButton.Content);
            Assert.DoesNotContain(view.Tree.VisibleNodes, node => node.HeaderHidden);
            var root = Assert.Single(view.Tree.VisibleNodes.Where(node => node.Kind == StructureTreeNodeKind.Normal));
            Assert.Equal("", root.DisplayKey); Assert.Equal("{2}", root.DisplayValue);
        }
        finally { window.Close(); Loc.SetLanguage(Loc.DefaultLanguage); }
    }

    internal static async Task Drain(StructureView view, Window window)
    {
        for (int attempt = 0; attempt < 1500; attempt++)
        {
            Layout(window);
            Task current = view.Working;
            if (current.IsCompleted)
            {
                await current; Layout(window);
                if (ReferenceEquals(current, view.Working)) return;
            }
            await Task.Delay(10);
        }
        Assert.Fail("The structure worker did not finish.");
    }

    internal static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
    }

    private static Rect BoundsIn(Control target, Visual reference)
        => new(target.TranslatePoint(default, reference)!.Value, target.Bounds.Size);
}
