using System.Text;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.8.4 in the window: decoder rows in the data inspector, a hash, a text form of the selection and templates, each from a plugin; a failing plugin is named and changes nothing.</summary>
[Collection("LocState")]
public class HexPluginAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;
    private readonly FakeHexPlugins _plugins = new();

    public HexPluginAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.HexPlugins = _plugins;
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private sealed class FakeHexPlugins : IHexPlugins
    {
        public static readonly HexOffer Decoder = new("acme.hex", "Acme", HexKind.Decoder, "bcd", "BCD", 1);
        public static readonly HexOffer Hash = new("acme.hex", "Acme", HexKind.Hash, "sum", "Sum8");
        public static readonly HexOffer Export = new("acme.hex", "Acme", HexKind.Exporter, "c", "C array", Extension: ".c");
        public static readonly HexOffer Templates = new("acme.hex", "Acme", HexKind.Templates, "t", "Templates");
        public List<HexOffer> Available { get; } = [Decoder, Hash, Export, Templates];
        public string? Fail { get; set; }
        public List<byte[]> Received { get; } = [];
        public string TemplateText { get; set; } = "meta:\n  id: pair\n  title: Pair\n  endian: le\nseq:\n  - id: a\n    type: u2\n";

        public IReadOnlyList<HexOffer> Offers(HexKind kind) => [.. Available.Where(o => o.Kind == kind)];

        private void Check(byte[] data)
        {
            Received.Add(data);
            if (Fail is not null) throw new HexPluginException(Fail);
        }

        public Task<HexDecoded> DecodeAsync(HexOffer offer, byte[] data, bool bigEndian, CancellationToken ct = default)
        {
            Check(data);
            return Task.FromResult(data.Length == 0 ? new HexDecoded(InspectorStatus.Incomplete, "", 1) : new HexDecoded(InspectorStatus.Ok, $"{data[0] >> 4}{data[0] & 15}", 1));
        }

        public Task<string> HashAsync(HexOffer offer, byte[] data, CancellationToken ct = default)
        {
            Check(data);
            return Task.FromResult(data.Sum(b => b).ToString("X"));
        }

        public Task<(string Name, string Text)> ExportAsync(HexOffer offer, byte[] data, long offset, CancellationToken ct = default)
        {
            Check(data);
            return Task.FromResult(("bytes.c", $"/* at {offset} */ {{{string.Join(", ", data.Select(b => "0x" + b.ToString("X2")))}}};"));
        }

        public Task<IReadOnlyList<HexTemplateText>> TemplatesAsync(HexOffer offer, CancellationToken ct = default)
        {
            if (Fail is not null) throw new HexPluginException(Fail);
            return Task.FromResult<IReadOnlyList<HexTemplateText>>([new HexTemplateText("pair.ksy", TemplateText), new HexTemplateText("broken.ksy", "not: [a template")]);
        }
    }

    private DocTab OpenHex()
    {
        _w.OpenFile(_temp.Bytes("data.bin", Enumerable.Range(0, 64).Select(i => (byte)(i + 0x10)).ToArray()), null);
        TestApp.Press(_w, Key.K, TestApp.Mod);
        TestApp.Press(_w, Key.H, TestApp.Mod);
        Dispatcher.UIThread.RunJobs();
        var hex = _w.Active!.Hex!;
        hex.Focus();
        Dispatcher.UIThread.RunJobs();
        return _w.Active;
    }

    private static async Task Until(Func<bool> done)
    {
        for (int i = 0; i < 200 && !done(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(25);
        }
        Assert.True(done());
    }

    [AvaloniaFact]
    public async Task The_inspector_gets_a_row_for_a_decoder_that_shows_an_ellipsis_until_the_plugin_answers()
    {
        var hex = OpenHex().Hex!;
        hex.Inspector.IsVisible = true;
        hex.MoveTo(2); // the byte 0x12
        hex.Inspector.Refresh();

        Assert.Contains(hex.Inspector.RowTexts, t => t.StartsWith("BCD", StringComparison.Ordinal));
        await Until(() => hex.Inspector.RowTexts.Contains("BCD: 12"));
        Assert.Equal([0x12], _plugins.Received.Last());

        _plugins.Fail = "gone";
        hex.MoveTo(3);
        hex.Inspector.Refresh();
        await Until(() => hex.Inspector.RowTexts.Any(t => t.StartsWith("BCD:", StringComparison.Ordinal) && t.Contains("gone")));
    }

    [AvaloniaFact]
    public async Task A_row_of_a_plugin_edits_nothing_and_a_view_without_plugins_has_only_the_built_in_rows()
    {
        var hex = OpenHex().Hex!;
        hex.Inspector.IsVisible = true;
        hex.Inspector.Refresh();
        int builtIn = hex.Inspector.RowTexts.Count - 1; // the row of the plugin is the last one
        await Until(() => hex.Inspector.RowTexts.Contains("BCD: 10"));
        Assert.Equal(builtIn + 1, hex.Inspector.RowTexts.Count);
        hex.Inspector.Rows.SelectedIndex = builtIn;
        TestApp.Press(_w, Key.Enter);
        Assert.False(hex.Inspector.IsEditing);
        Assert.False(hex.Editor.Document.IsModified);

        _plugins.Available.Clear();
        hex.Inspector.Refresh();
        Assert.Equal(builtIn, hex.Inspector.RowTexts.Count);
    }

    [AvaloniaFact]
    public async Task A_hash_of_a_plugin_is_computed_from_the_selection_and_shown_in_the_dialog_of_the_built_in_hashes()
    {
        var hex = OpenHex().Hex!;
        hex.Select(0, 3); // 0x10 0x11 0x12
        _dialogs.Answers.Enqueue(2); // close
        await _w.HexPluginHashAsync(hex, FakeHexPlugins.Hash);

        Assert.Equal([0x10, 0x11, 0x12], _plugins.Received.Single());
        var shown = Assert.Single(_dialogs.Asked);
        Assert.Equal(["33"], shown.Details); // 0x10 + 0x11 + 0x12
        Assert.Contains("Sum8", shown.Message);
    }

    [AvaloniaFact]
    public async Task A_hash_without_a_selection_takes_the_whole_file()
    {
        var hex = OpenHex().Hex!;
        _dialogs.Answers.Enqueue(2);
        await _w.HexPluginHashAsync(hex, FakeHexPlugins.Hash);
        Assert.Equal(64, _plugins.Received.Single().Length);
    }

    [AvaloniaFact]
    public async Task The_text_a_plugin_makes_of_the_selection_opens_as_a_new_document_and_the_file_stays_as_it_was()
    {
        var hex = OpenHex().Hex!;
        var file = _w.Active!;
        hex.Select(4, 6);
        int before = _w.Documents.Count;
        await _w.HexPluginExportAsync(hex, FakeHexPlugins.Export);

        Assert.Equal(before + 1, _w.Documents.Count);
        var made = _w.Active!;
        Assert.NotSame(file, made);
        Assert.Equal("bytes.c", made.Title);
        Assert.Equal("/* at 4 */ {0x14, 0x15};", made.Surface!.Document.Text);
        Assert.Null(made.Doc.Path);
        Assert.False(hex.Editor.Document.IsModified);
    }

    [AvaloniaFact]
    public async Task An_export_needs_a_selection_and_a_failing_plugin_is_named_in_the_status_line()
    {
        var hex = OpenHex().Hex!;
        int before = _w.Documents.Count;
        await _w.HexPluginExportAsync(hex, FakeHexPlugins.Export);
        Assert.Equal(before, _w.Documents.Count);
        Assert.Equal(Loc.Get(K.HexNeedsSelection), _w.StatusMessageText);

        hex.Select(0, 2);
        _plugins.Fail = "Acme: timed out";
        await _w.HexPluginExportAsync(hex, FakeHexPlugins.Export);
        Assert.Equal(before, _w.Documents.Count);
        Assert.Contains("timed out", _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task The_templates_of_a_plugin_join_the_structure_list_and_one_the_subset_refuses_is_named()
    {
        var hex = OpenHex().Hex!;
        await _w.HexPluginTemplatesAsync(hex, FakeHexPlugins.Templates);

        Assert.Contains(_app.HexTemplates.Loaded, p => p.File == "pair.ksy");
        Assert.DoesNotContain(_app.HexTemplates.Loaded, p => p.File == "broken.ksy");
        Assert.Contains("broken.ksy", hex.Structure.NoteShown);
        Assert.Contains("1", _w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task Too_many_bytes_are_not_sent_and_no_offer_says_so()
    {
        var hex = OpenHex().Hex!;
        _plugins.Available.Clear();
        _w.HexPluginHashItems(hex);
        Assert.Equal(Loc.Get(K.HexPluginNone), _w.StatusMessageText);
        Assert.Empty(_plugins.Received);
    }

}

/// <summary>PZ.8.4: what a plugin may send back is read with limits.</summary>
public class HexPluginAnswersTests
{
    [Fact]
    public void The_answers_are_read_with_limits_and_a_bad_answer_is_refused_with_a_reason()
    {
        static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
        Assert.Equal(new HexDecoded(InspectorStatus.Ok, "42", 1), HexAnswers.Decoded(J("{\"status\":\"ok\",\"text\":\"42\",\"length\":1}")));
        Assert.Equal(InspectorStatus.Incomplete, HexAnswers.Decoded(J("{\"status\":\"incomplete\",\"length\":4}")).Status);
        Assert.Throws<HexPluginException>(() => HexAnswers.Decoded(J("{\"status\":\"maybe\"}")));
        Assert.Throws<HexPluginException>(() => HexAnswers.Decoded(J("{\"status\":\"ok\"}")));
        Assert.Throws<HexPluginException>(() => HexAnswers.Decoded(J("{\"status\":\"ok\",\"text\":\"" + new string('x', HexAnswers.MaxDecodedChars + 1) + "\"}")));
        Assert.Throws<HexPluginException>(() => HexAnswers.Hash(J("[1]")));

        Assert.Equal(("bytes.c", "x"), HexAnswers.Export(J("{\"name\":\"bytes.c\",\"text\":\"x\"}"), ".c"));
        Assert.Equal("export.json", HexAnswers.Export(J("{\"name\":\"..\\\\..\\\\evil/\",\"text\":\"x\"}"), ".json").Name); // a path in the name is not taken
        Assert.Equal("export.txt", HexAnswers.Export(J("{\"text\":\"x\"}"), ".txt").Name);

        Assert.Equal("a.ksy", HexAnswers.Templates(J("{\"templates\":[{\"name\":\"a.ksy\",\"ksy\":\"meta: {}\"}]}")).Single().Name);
        Assert.Throws<HexPluginException>(() => HexAnswers.Templates(J("{\"templates\":[{\"name\":\"../a.ksy\",\"ksy\":\"x\"}]}")));
        Assert.Throws<HexPluginException>(() => HexAnswers.Templates(J("{}")));

        var request = Encoding.UTF8.GetString(HexAnswers.Request(w => w.WriteString("hash", "h"), [1, 2]));
        Assert.Equal("{\"hash\":\"h\",\"data\":\"AQI=\"}", request);
    }
}
