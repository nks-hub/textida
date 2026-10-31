using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Plugins.Lsp;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>P8.5.6: what the LSP plugin offers beyond navigation, completion and rename, each against the fake server that announces it (<c>--features</c>) and, where it matters, against one that does not.</summary>
public partial class LspPluginTests
{
    static async Task<NksHub.Textida.PluginHost.Documents.HoverAnswer> HoverAsync(Rig rig, Document doc, long offset)
    {
        await T.Until(() => rig.Docs.HasHoverProviders);
        return await rig.Docs.RequestHoverAsync(doc.Id, offset);
    }

    [Fact]
    public async Task Hover_gives_the_server_text_for_the_word_in_bytes_of_the_revision()
    {
        await using var rig = await Rig.CreateAsync(["--features", "hover"]);
        var doc = rig.Add("hover.fake", "é€😀 hello world\n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);

        var answer = await HoverAsync(rig, doc, Bytes("é€😀 hel"));

        Assert.False(answer.Stale);
        Assert.Equal("**hello** (5 chars)", answer.Text);
        Assert.Equal(new ByteRange(Bytes("é€😀 "), Bytes("é€😀 hello")), answer.Range);
        Assert.Equal("lsp", answer.PluginId);
    }

    [Fact]
    public async Task Hover_is_not_asked_of_a_server_that_does_not_announce_it_and_an_old_answer_is_dropped()
    {
        await using (var plain = await Rig.CreateAsync())
        {
            var doc = plain.Add("plain.fake", "hello world\n");
            await plain.RunAsync(doc, LspPlugin.StartCommand);
            Assert.Null((await HoverAsync(plain, doc, 2)).Text);
        }

        await using var rig = await Rig.CreateAsync(["--features", "hover"]);
        var slow = rig.Add("slow.fake", "SLOW hello\n");
        await rig.RunAsync(slow, LspPlugin.StartCommand);
        await T.Until(() => rig.Docs.HasHoverProviders);
        var pending = rig.Docs.RequestHoverAsync(slow.Id, Bytes("SLOW hel")); // the fake server needs a second for this one
        await Task.Delay(300);
        await rig.Edit(slow, 0, 0, "x");
        Assert.Null((await pending).Text);
        Assert.Equal("**xSLOW** (5 chars)", (await HoverAsync(rig, slow, 1)).Text);
    }
}
