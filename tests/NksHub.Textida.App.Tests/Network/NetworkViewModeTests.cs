using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// PR.6.4 for the view modes (P3.6.11, P9.3.4): the page of a Markdown file in reading and in editing, and a picture file, make no outbound attempt of any process when the file names remote hosts,
/// and the remote pictures wait behind the button that asks. A network proxy is set that nothing may reach.
/// </summary>
[Collection("LocState")]
[Trait("Category", "Network")]
public class NetworkViewModeTests
{
    private static IEnumerable<Control> Tree(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    [AvaloniaFact]
    public async Task The_page_in_reading_and_in_editing_and_a_picture_file_connect_nowhere_and_offer_to_load_instead()
    {
        using var rig = new NetworkRig();
        rig.App.Workbench.Set("markdown.defaultMode", "read");
        const string remote = "https://assets.example.test/";
        var tab = rig.Window.OpenFile(rig.ProjectFile("readme.md", $"# Title\n\n![logo]({remote}logo.png)\n\n[docs]({remote}docs)\n\n<img src=\"{remote}b.png\">\n"), null);
        await rig.Settle(700);
        rig.Window.UpdateLayout();
        var page = Assert.IsType<MarkdownPreview>(tab.Custom);
        Assert.Empty(Tree(page).OfType<Image>());
        Assert.NotEmpty(Tree(page).OfType<Button>());

        Assert.True(rig.Window.SetViewMode(tab.Doc, ViewMode.Edit));
        await rig.Settle(700);
        rig.Window.UpdateLayout();
        var side = rig.Window.Active!.SidePreview!;
        Assert.Empty(Tree(side).OfType<Image>());
        Assert.NotEmpty(Tree(side).OfType<Button>());

        // a picture file is read from the disk and nothing else
        string logo = Path.Combine(rig.Temp.Path, "proj", "logo.png");
        File.WriteAllBytes(logo, Pictures.Png());
        rig.Window.OpenFile(logo, null);
        await rig.Until(() => rig.Window.Active?.Custom is ImageView { Loading: false }, "the picture");
        Assert.Empty(rig.Dialogs.Asked);

        NetworkNoConsentTests.AssertSilent(await rig.Net.FinishAsync(), rig);
    }
}
