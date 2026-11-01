using Avalonia.Headless.XUnit;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.2.3 and PZ.2.4: the selection is searched through the user's address, and a file opens at the address its path rules give.</summary>
[Collection("LocState")]
public class WebLinksAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly List<Uri> _opened = [];

    public WebLinksAppTests()
    {
        _app = TestApp.Prepare(_temp, _dialogs);
        _app.UriOpener = uri =>
        {
            _opened.Add(uri);
            return Task.FromResult(true);
        };
    }

    public void Dispose()
    {
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task The_selection_or_the_word_at_the_caret_goes_to_the_address_of_the_setting_and_nothing_goes_without_one()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("notes.txt", "alpha beta gamma"), null);
        var surface = tab.Editor!;

        surface.SetSelections([new Sel(6, 10)]);
        await w.SearchSelectionOnWebAsync(); // no address set
        Assert.Empty(_opened);
        Assert.Contains("web.searchUrl", w.StatusMessageText);

        _app.Workbench.Set("web.searchUrl", "https://find.test/?q={query}");
        await w.SearchSelectionOnWebAsync();
        Assert.Equal("https://find.test/?q=beta", Assert.Single(_opened).AbsoluteUri);

        surface.SetSelections([Sel.At(13)]); // no selection: the word at the caret
        await w.SearchSelectionOnWebAsync();
        Assert.Equal("https://find.test/?q=gamma", _opened[1].AbsoluteUri);
    }

    [AvaloniaFact]
    public async Task A_file_opens_at_the_address_of_its_rule_and_a_file_without_a_rule_says_so()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("page.html", "<p>x</p>"), null);

        await w.OpenFileAtMappedUrlAsync();
        Assert.Empty(_opened);
        Assert.Contains("web.pathMap", w.StatusMessageText);

        _app.Workbench.Set("web.pathMap", _temp.Path + "=>http://site.loc/app");
        await w.OpenFileAtMappedUrlAsync();
        Assert.Equal("http://site.loc/app/page.html", Assert.Single(_opened).AbsoluteUri);

        _app.Workbench.Set("web.pathMap", Path.Combine(_temp.Path, "elsewhere") + "=>http://site.loc/");
        await w.OpenFileAtMappedUrlAsync();
        Assert.Single(_opened);
        Assert.Contains("page.html", w.StatusMessageText);
    }
}
