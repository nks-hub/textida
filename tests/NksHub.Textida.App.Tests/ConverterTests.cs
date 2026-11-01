using System.Text;
using Avalonia.Headless.XUnit;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Converters;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.5.2: the contract of the file type converters, the built-in CSV and TSV converter, an external converter that is a real program, and the open and save through them in the window.</summary>
public class ConverterContractTests
{
    private static byte[] Utf8(string s) => new UTF8Encoding(false).GetBytes(s);

    [Fact]
    public void Csv_is_shown_as_tsv_and_saved_back_with_its_separator_and_line_ends()
    {
        byte[] csv = Utf8("name;note\r\nAda;\"say \"\"hi\"\"; ok\"\r\nBob;\"two\r\nlines\"\r\n");
        var read = FileConverters.CsvTsv.Read(csv);
        Assert.Equal("name\tnote\nAda\tsay \"hi\"; ok\nBob\ttwo\\nlines\n".Replace("\\n", "\\n"), read.Text.Replace("\\r\\n", "\\n"));
        Assert.Contains("semicolon", read.Notes[0]);

        var written = FileConverters.CsvTsv.Write(read.Text, csv);
        Assert.Empty(written.Losses);
        Assert.Equal(csv, written.Bytes); // quoting and line ends come back as they were
    }

    [Fact]
    public void A_tab_a_backslash_and_a_line_end_inside_a_field_survive_the_trip()
    {
        byte[] csv = Utf8("a,b\n\"x\ty\",\"back\\slash\"\n");
        var read = FileConverters.CsvTsv.Read(csv);
        Assert.Equal("a\tb\nx\\ty\tback\\\\slash\n", read.Text);
        var report = ConverterRoundTrip.Check(FileConverters.CsvTsv, read.Text, csv);
        Assert.True(report.Stable);
        Assert.False(report.NeedsConsent);
        Assert.Equal(read.Text, FileConverters.CsvTsv.Read(report.Bytes).Text); // the quotes the file did not need are gone, the records are the same
    }

    [Fact]
    public void An_edited_text_is_written_in_the_encoding_the_file_had_and_a_character_it_lacks_is_a_named_loss()
    {
        byte[] cp1250 = Encoding.GetEncoding(1250, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) is var e && e.CodePage == 1250 ? e.GetBytes("město;kůň\nžlutý;kůň\n") : [];
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        cp1250 = Encoding.GetEncoding(1250).GetBytes("město;kůň\nžlutý;kůň\n");
        var read = FileConverters.CsvTsv.Read(cp1250);
        Assert.Equal("město\tkůň\nžlutý\tkůň\n", read.Text);

        var fine = ConverterRoundTrip.Check(FileConverters.CsvTsv, "město\tkůň\nnový\tkoně\n", cp1250);
        Assert.False(fine.NeedsConsent);
        Assert.Equal("město;kůň\r\nnový;koně\r\n".Replace("\r\n", "\n"), Encoding.GetEncoding(1250).GetString(fine.Bytes).Replace("\r\n", "\n"));

        var lossy = ConverterRoundTrip.Check(FileConverters.CsvTsv, "město\t日本\n", cp1250);
        Assert.True(lossy.NeedsConsent);
        Assert.Contains(lossy.Losses, l => l.Contains("1250") || l.Contains("Windows"));
    }

    [Fact]
    public void A_converter_that_changes_what_it_reads_back_is_not_stable()
    {
        var shout = new ExternalConverter("x", "x", [".up"], FindProgram("python"), ["-c", "import sys;print(open(sys.argv[1],encoding='utf-8').read().upper(),end='')", "{input}"],
            ["-c", "import sys;open(sys.argv[2],'wb').write(open(sys.argv[1],'rb').read().lower())", "{input}", "{output}"]);
        byte[] file = Utf8("hello\n");
        var read = shout.Read(file);
        Assert.Equal("HELLO\n", read.Text);

        Assert.True(ConverterRoundTrip.Check(shout, "HELLO\n", file) is { Stable: true, NeedsConsent: false });
        var changed = ConverterRoundTrip.Check(shout, "Hello\n", file); // written as "hello", read back as "HELLO"
        Assert.False(changed.Stable);
        Assert.True(changed.NeedsConsent);
    }

    [Fact]
    public void An_external_converter_that_fails_or_is_missing_throws_with_a_reason()
    {
        var failing = new ExternalConverter("f", "failing", [".x"], FindProgram("python"), ["-c", "import sys;sys.stderr.write('bad file');sys.exit(3)", "{input}"]);
        var error = Assert.Throws<ConverterException>(() => failing.Read([1, 2, 3]));
        Assert.Contains("exit code 3", error.Message);
        Assert.Contains("bad file", error.Message);
        Assert.Throws<ConverterException>(() => new ExternalConverter("m", "missing", [".x"], Path.Combine(Path.GetTempPath(), "no-such-program.exe"), ["{input}"]).Read([]));
        Assert.Throws<ConverterException>(() => failing.Write("x", []));
        var slow = new ExternalConverter("s", "slow", [".x"], FindProgram("python"), ["-c", "import time;time.sleep(30)"], timeout: TimeSpan.FromMilliseconds(300));
        Assert.Contains("no answer", Assert.Throws<ConverterException>(() => slow.Read([])).Message);
    }

    [Fact]
    public void Converters_are_offered_by_extension_and_pandoc_only_when_it_is_found()
    {
        Assert.Contains(FileConverters.CsvTsv, FileConverters.For("data.CSV"));
        Assert.Empty(FileConverters.For("data.bin"));
        if (NksHub.Textida.Core.Tools.ToolLocator.Find("pandoc") is not null) Assert.Contains(FileConverters.For("a.docx"), c => c.Id == "docx-text");
        else Assert.Empty(FileConverters.For("a.docx"));
    }

    internal static string FindProgram(string name)
    {
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (folder.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) continue;
            string candidate = Path.Combine(folder, OperatingSystem.IsWindows() ? name + ".exe" : name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new InvalidOperationException(name + " is not on the PATH of this machine.");
    }
}

[Collection("LocState")]
public class ConverterAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly MainWindow _w;

    public ConverterAppTests()
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

    [AvaloniaFact]
    public async Task A_csv_opens_as_tsv_and_saves_back_as_csv_after_an_edit()
    {
        string path = _temp.File("data.csv", "a,b\n1,2\n");
        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(path);
        await _w.OpenThroughConverterAsync();

        var tab = _w.Active!;
        Assert.Equal("a\tb\n1\t2\n", tab.Editor!.Document.Text);
        Assert.NotNull(tab.Doc.Converted);
        Assert.False(tab.Doc.Doc.IsModified);

        tab.Editor.Document.Insert(tab.Editor.Document.TextLength, "3\t4,5\n");
        Assert.True(await _w.SaveAsync(tab));
        Assert.Equal("a,b\n1,2\n3,\"4,5\"\n", File.ReadAllText(path));
        Assert.False(tab.Doc.Doc.IsModified);
        Assert.Empty(_dialogs.Asked);
    }

    [AvaloniaFact]
    public async Task A_save_that_loses_something_asks_and_a_no_leaves_the_file_as_it_was()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string path = Path.Combine(_temp.Path, "latin.csv");
        File.WriteAllBytes(path, Encoding.GetEncoding(1250).GetBytes("město;kůň\nžlutý;kůň\n"));
        byte[] before = File.ReadAllBytes(path);
        var tab = _w.OpenConverted(path, FileConverters.CsvTsv)!;
        tab.Editor!.Document.Insert(tab.Editor.Document.TextLength, "日本\tx\n");

        _dialogs.Answers.Enqueue(1);
        Assert.False(await _w.SaveAsync(tab));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(Loc.Get(K.ConverterLossyTitle), _dialogs.Asked[0].Title);
        Assert.True(tab.Doc.Doc.IsModified);

        _dialogs.Answers.Enqueue(0);
        Assert.True(await _w.SaveAsync(tab));
        Assert.Contains("?", Encoding.GetEncoding(1250).GetString(File.ReadAllBytes(path)));
    }

    [AvaloniaFact]
    public async Task A_read_only_converter_opens_a_read_only_tab_that_cannot_be_saved()
    {
        var reader = new ExternalConverter("r", "reader", [".up"], ConverterContractTests.FindProgram("python"), ["-c", "import sys;print(open(sys.argv[1],encoding='utf-8').read().upper(),end='')", "{input}"]);
        string path = _temp.File("note.up", "quiet\n");
        var tab = _w.OpenConverted(path, reader)!;
        Assert.Equal("QUIET\n", tab.Editor!.Document.Text);
        Assert.True(tab.Doc.UserReadOnly);
        Assert.False(await _w.SaveAsync(tab));
        Assert.Equal("quiet\n", File.ReadAllText(path));
    }

    [AvaloniaFact]
    public async Task A_file_without_a_converter_or_one_the_converter_cannot_read_says_so()
    {
        string path = _temp.File("x.bin", "data");
        _dialogs.Picked.Clear();
        _dialogs.Picked.Add(path);
        await _w.OpenThroughConverterAsync();
        Assert.Contains("x.bin", _w.StatusMessageText);

        var failing = new ExternalConverter("f", "failing", [".bin"], ConverterContractTests.FindProgram("python"), ["-c", "import sys;sys.exit(2)", "{input}"]);
        Assert.Null(_w.OpenConverted(path, failing));
        Assert.Contains("exit code 2", _w.StatusMessageText);
    }
}
