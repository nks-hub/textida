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
    public async Task Formatting_shows_the_edits_of_the_server_in_the_review_and_applies_them_as_one_step()
    {
        await using var rig = await Rig.CreateAsync(["--features", "formatting"]);
        var doc = rig.Add("format.fake", "é😀 one   \ntwo\t\n\nthree  \n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);

        var r = await rig.RunAsync(doc, LspPlugin.FormatCommand);

        Assert.Equal(("ok", "applied"), (r.GetProperty("status").GetString(), r.GetProperty("decision").GetString()));
        var shown = Assert.Single(rig.Review.Shown);
        Assert.Equal("Format document", shown.Proposal.Title);
        Assert.Equal(3, Assert.Single(shown.Proposal.Files).Hunks.Count);
        Assert.Equal("é😀 one\ntwo\n\nthree\n", Rig.TextOf(doc));
        Assert.Equal(1, doc.UndoDepth);
    }

    [Fact]
    public async Task Formatting_is_not_asked_of_a_server_that_does_not_announce_it_and_a_changed_document_drops_the_answer()
    {
        await using (var plain = await Rig.CreateAsync())
        {
            var doc = plain.Add("plain.fake", "a  \n");
            await plain.RunAsync(doc, LspPlugin.StartCommand);
            var r = await plain.RunAsync(doc, LspPlugin.FormatCommand);
            Assert.Equal("notSupported", r.GetProperty("status").GetString());
            Assert.Empty(plain.Review.Shown);
        }

        await using var rig = await Rig.CreateAsync(["--features", "formatting"]);
        var clean = rig.Add("clean.fake", "already clean\n");
        await rig.RunAsync(clean, LspPlugin.StartCommand);
        Assert.Equal("none", (await rig.RunAsync(clean, LspPlugin.FormatCommand)).GetProperty("status").GetString());
        Assert.Empty(rig.Review.Shown);
    }

    [Fact]
    public async Task A_quick_fix_for_the_problem_at_the_caret_is_picked_and_applied_through_the_review()
    {
        await using var rig = await Rig.CreateAsync(["--features", "codeaction"]);
        var doc = rig.Add("fix.fake", "é😀 ok ERROR here\n");
        await rig.RunAsync(doc, LspPlugin.StartCommand);
        await T.Until(() => rig.DiagnosticsOf(doc).Count == 1);
        rig.Ui.Pick = request => request.Items[0].Id;

        var r = await rig.RunAsync(doc, LspPlugin.CodeActionsCommand, Bytes("é😀 ok ER"));

        Assert.Equal(("ok", "applied"), (r.GetProperty("status").GetString(), r.GetProperty("decision").GetString()));
        var picked = Assert.Single(rig.Ui.Picks);
        Assert.Equal(["Remove ERROR"], picked.Items.Select(i => i.Label)); // the action that only runs a command and the disabled one are not offered
        Assert.Equal("Remove ERROR", Assert.Single(rig.Review.Shown).Proposal.Title);
        Assert.Equal("é😀 ok  here\n", Rig.TextOf(doc));
        Assert.Equal(1, doc.UndoDepth);
    }

    [Fact]
    public async Task Code_actions_are_not_asked_of_a_server_that_does_not_announce_them_and_a_cancelled_pick_changes_nothing()
    {
        await using (var plain = await Rig.CreateAsync())
        {
            var doc = plain.Add("plain.fake", "ERROR\n");
            await plain.RunAsync(doc, LspPlugin.StartCommand);
            Assert.Equal("notSupported", (await plain.RunAsync(doc, LspPlugin.CodeActionsCommand, 1)).GetProperty("status").GetString());
            Assert.Empty(plain.Ui.Picks);
        }

        await using var rig = await Rig.CreateAsync(["--features", "codeaction"]);
        var elsewhere = rig.Add("elsewhere.fake", "fine\n");
        await rig.RunAsync(elsewhere, LspPlugin.StartCommand);
        Assert.Equal("none", (await rig.RunAsync(elsewhere, LspPlugin.CodeActionsCommand, 1)).GetProperty("status").GetString()); // no problem here: only the actions without an edit
        var cancel = rig.Add("cancel.fake", "ERROR\n");
        await rig.RunAsync(cancel, LspPlugin.StartCommand);
        await T.Until(() => rig.DiagnosticsOf(cancel).Count == 1);
        Assert.Equal("cancelled", (await rig.RunAsync(cancel, LspPlugin.CodeActionsCommand, 1)).GetProperty("status").GetString());
        Assert.Equal("ERROR\n", Rig.TextOf(cancel));
        Assert.Empty(rig.Review.Shown);
    }

    [Fact]
    public async Task Parameter_info_marks_the_parameter_at_the_caret_and_is_not_asked_of_a_server_without_it()
    {
        await using (var plain = await Rig.CreateAsync())
        {
            var doc = plain.Add("plain.fake", "run(1)\n");
            await plain.RunAsync(doc, LspPlugin.StartCommand);
            Assert.Equal("notSupported", (await plain.RunAsync(doc, LspPlugin.SignatureHelpCommand, 4)).GetProperty("status").GetString());
        }

        await using var rig = await Rig.CreateAsync(["--features", "signature"]);
        var text = "é😀 run(1, 2)\n";
        var call = rig.Add("sig.fake", text);
        await rig.RunAsync(call, LspPlugin.StartCommand);

        var first = await rig.RunAsync(call, LspPlugin.SignatureHelpCommand, Bytes("é😀 run(1"));
        Assert.Equal("ok", first.GetProperty("status").GetString());
        Assert.StartsWith("run(«a: int», b: string)", first.GetProperty("signature").GetString());
        var second = await rig.RunAsync(call, LspPlugin.SignatureHelpCommand, Bytes("é😀 run(1, 2"));
        Assert.StartsWith("run(a: int, «b: string»)", second.GetProperty("signature").GetString());
        Assert.Contains(rig.Ui.Notices, n => n.Message.StartsWith("run(a: int, «b: string»)", StringComparison.Ordinal));
        Assert.Equal("none", (await rig.RunAsync(call, LspPlugin.SignatureHelpCommand, 0)).GetProperty("status").GetString()); // outside any call
    }

    [Fact]
    public async Task The_symbols_of_the_document_are_published_on_the_bytes_of_their_names_for_the_revision_asked()
    {
        await using var rig = await Rig.CreateAsync(["--features", "symbols"]);
        var text = "# é😀\nclass Box\n  def area\n  def size\ndef helper\n";
        var doc = rig.Add("symbols.fake", text);
        await rig.RunAsync(doc, LspPlugin.StartCommand);

        var r = await rig.RunAsync(doc, LspPlugin.DocumentSymbolsCommand);

        Assert.Equal(("ok", 4), (r.GetProperty("status").GetString(), r.GetProperty("count").GetInt32()));
        var symbols = rig.Docs.SymbolsOf("lsp", doc.Id, out bool current);
        Assert.True(current);
        Assert.Equal(["Box", "area", "size", "helper"], symbols.Select(s => s.Name));
        Assert.Equal(["class", "method", "method", "function"], symbols.Select(s => s.Kind));
        Assert.Equal([null, symbols[0].Id, symbols[0].Id, null], symbols.Select(s => s.ParentId));
        var snapshot = doc.GetSnapshot();
        Assert.All(symbols, s => Assert.Equal(s.Name, Encoding.UTF8.GetString(snapshot.ReadBytes(s.Range))));
        Assert.Equal(Bytes("# é😀\nclass "), symbols[0].Range.Start);
    }

    [Fact]
    public async Task Symbols_of_a_server_without_them_are_not_asked_and_a_search_in_the_workspace_lists_the_places()
    {
        await using (var plain = await Rig.CreateAsync())
        {
            var doc = plain.Add("plain.fake", "def a\n");
            await plain.RunAsync(doc, LspPlugin.StartCommand);
            Assert.Equal("notSupported", (await plain.RunAsync(doc, LspPlugin.DocumentSymbolsCommand)).GetProperty("status").GetString());
            Assert.Equal("notSupported", (await plain.RunAsync(doc, LspPlugin.WorkspaceSymbolsCommand)).GetProperty("status").GetString());
        }

        await using var rig = await Rig.CreateAsync(["--features", "symbols"]);
        var first = rig.Add("one.fake", "é\ndef helper\n");
        var second = rig.Add("two.fake", "😀\ndef other\ndef helperTwo\n");
        await rig.RunAsync(first, LspPlugin.StartCommand);
        await rig.RunAsync(second, LspPlugin.StartCommand);
        rig.Ui.Input = _ => "HELPER"; // without regard to case

        var r = await rig.RunAsync(first, LspPlugin.WorkspaceSymbolsCommand);

        Assert.Equal(("ok", 2), (r.GetProperty("status").GetString(), r.GetProperty("count").GetInt32()));
        var panel = Assert.Single(rig.Ui.Panels);
        Assert.Equal(2, panel.Items.Count);
        var starts = r.GetProperty("locations").EnumerateArray().Select(l => (l.GetProperty("start").GetInt64(), l.GetProperty("inDocument").GetBoolean())).ToList();
        Assert.Equal([(Bytes("é\ndef "), true), (Bytes("😀\ndef other\ndef "), false)], starts);

        rig.Ui.Input = _ => "nothing like it";
        Assert.Equal("none", (await rig.RunAsync(first, LspPlugin.WorkspaceSymbolsCommand)).GetProperty("status").GetString());
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
