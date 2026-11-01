using Avalonia.Headless.XUnit;
using NksHub.Textida.Core.Compare;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.7.3: the command asks for the sides, reports in a new tab, shows the list of copies and copies only after the confirmation.</summary>
[Collection("LocState")]
public class FolderCompareAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;

    public FolderCompareAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task The_report_opens_in_a_tab_and_the_copy_waits_for_the_confirmed_list()
    {
        string left = Path.Combine(_temp.Path, "left");
        string right = Path.Combine(_temp.Path, "right");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);
        File.WriteAllText(Path.Combine(left, "new.txt"), "n");
        File.WriteAllText(Path.Combine(left, "diff.txt"), "long text");
        File.WriteAllText(Path.Combine(right, "diff.txt"), "x");
        var w = _app.CreateWindow(null);

        // by size, then "left to right", then Cancel at the list: nothing is written
        foreach (var a in new[] { 0, 0, 1 }) _dialogs.Answers.Enqueue(a);
        await w.CompareFolderSidesAsync(new LocalFolderSource(left), new LocalFolderSource(right));
        Assert.Contains("< new.txt", w.Active!.Editor!.Document.Text);
        Assert.Contains("! diff.txt", w.Active.Editor.Document.Text);
        var preview = _dialogs.Asked[^1];
        Assert.Contains(preview.Details!, d => d.EndsWith(": new.txt"));
        Assert.Contains(preview.Details!, d => d.EndsWith(": diff.txt"));
        Assert.False(File.Exists(Path.Combine(right, "new.txt")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(right, "diff.txt")));

        // the same, confirmed
        foreach (var a in new[] { 0, 0, 0 }) _dialogs.Answers.Enqueue(a);
        await w.CompareFolderSidesAsync(new LocalFolderSource(left), new LocalFolderSource(right));
        Assert.Equal("n", File.ReadAllText(Path.Combine(right, "new.txt")));
        Assert.Equal("long text", File.ReadAllText(Path.Combine(right, "diff.txt")));
        Assert.Contains("2", w.StatusMessageText);
    }
}
