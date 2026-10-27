using System.Text;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Tests;

/// <summary>P5.5.2: the batch encoding conversion shows the files that lose characters before it writes and summarises the result.</summary>
[Collection("LocState")]
public class EncodingBatchAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    static EncodingBatchAppTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public EncodingBatchAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _w = _app.CreateWindow(null);
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        _temp.Dispose();
    }

    private string Folder(out string[] lossy, out string[] all)
    {
        string dir = Path.Combine(_temp.Path, "tree");
        Directory.CreateDirectory(dir);
        var bad = new List<string>();
        all = new string[100];
        for (int i = 0; i < 100; i++)
        {
            bool lose = i % 25 == 0;
            string path = Path.Combine(dir, $"f{i:000}.txt");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes($"Příliš žluťoučký kůň {i}\n" + (lose ? "★\n" : "")));
            all[i] = path;
            if (lose) bad.Add(path);
        }
        lossy = [.. bad];
        return dir;
    }

    private void Script(string dir, string masks, params int[] answers)
    {
        _dialogs.Folders.Enqueue(dir);
        _dialogs.Prompts.Enqueue(masks);
        _dialogs.Encodings.Enqueue(TextCodec.Cp1250);
        foreach (int a in answers) _dialogs.Answers.Enqueue(a);
    }

    [AvaloniaFact]
    public async Task The_preview_names_the_files_that_lose_characters_and_cancel_writes_nothing()
    {
        string dir = Folder(out var lossy, out var all);
        var before = all.ToDictionary(p => p, File.ReadAllBytes);
        Script(dir, "*.txt", 2); // third button: cancel

        await _w.ConvertFilesInFolderAsync();

        var ask = Assert.Single(_dialogs.Asked);
        Assert.Equal(Loc.Get(K.BatchTitle), ask.Title);
        Assert.Contains("96", ask.Message);
        Assert.Equal(4, ask.Details!.Count(d => d.Contains('★')));
        Assert.All(lossy, p => Assert.Contains(ask.Details!, d => d.StartsWith(Path.GetFileName(p), StringComparison.Ordinal)));
        Assert.Equal(3, ask.Buttons.Count);
        foreach (var p in all) Assert.Equal(before[p], File.ReadAllBytes(p));
    }

    [AvaloniaFact]
    public async Task Converting_the_clean_files_leaves_the_lossy_ones_and_the_summary_lists_them()
    {
        string dir = Folder(out var lossy, out var all);
        var before = all.ToDictionary(p => p, File.ReadAllBytes);
        Script(dir, "", 0, 0);

        await _w.ConvertFilesInFolderAsync();

        var cp1250 = Encoding.GetEncoding(1250);
        foreach (var p in all)
        {
            if (lossy.Contains(p)) Assert.Equal(before[p], File.ReadAllBytes(p));
            else Assert.Equal(cp1250.GetBytes(Encoding.UTF8.GetString(before[p])), File.ReadAllBytes(p));
        }
        Assert.Equal(2, _dialogs.Asked.Count);
        Assert.Equal(Loc.Get(K.BatchResultTitle), _dialogs.Asked[1].Title);
        Assert.Contains("96", _dialogs.Asked[1].Message);
        Assert.Equal(4, _dialogs.Asked[1].Details!.Count(d => d.Contains(Loc.Get(K.BatchSkipLossy))));
    }

    [AvaloniaFact]
    public async Task Replacing_lost_characters_converts_every_file()
    {
        string dir = Folder(out var lossy, out var all);
        Script(dir, "*.txt", 1, 0);

        await _w.ConvertFilesInFolderAsync();

        var cp1250 = Encoding.GetEncoding(1250);
        foreach (var p in lossy)
            Assert.Equal(cp1250.GetBytes($"Příliš žluťoučký kůň {int.Parse(Path.GetFileNameWithoutExtension(p)[1..])}\n?\n"), File.ReadAllBytes(p));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [AvaloniaFact]
    public async Task An_open_document_with_unsaved_changes_is_left_out()
    {
        string dir = Folder(out _, out var all);
        var tab = _w.OpenFile(all[1], null);
        tab.Editor!.Document.Insert(0, "x");
        var before = File.ReadAllBytes(all[1]);
        Script(dir, "", 0, 0);

        await _w.ConvertFilesInFolderAsync();

        Assert.Equal(before, File.ReadAllBytes(all[1]));
        Assert.Contains("1", _dialogs.Asked[0].Message);
    }
}
