using Avalonia.Headless.XUnit;

namespace NksHub.Textida.App.Tests;

/// <summary>PZ.5.1: the formatter of the project runs on a copy and its result goes to the change review instead of the document.</summary>
[Collection("LocState")]
public class FormatReviewAppTests : IDisposable
{
    private readonly TempDir _temp = new();
    private readonly ScriptedDialogs _dialogs = new();
    private readonly App _app;
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");

    public FormatReviewAppTests() => _app = TestApp.Prepare(_temp, _dialogs);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        TestApp.Cleanup(_app);
        _temp.Dispose();
    }

    [AvaloniaFact]
    public async Task A_file_without_a_formatter_configuration_says_so()
    {
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("plain.php", "<?php\n"), null);

        await w.FormatWithReviewAsync(tab);

        Assert.Null(w.FormatDecision);
        Assert.Contains("plain.php", w.StatusMessageText);
    }

    [AvaloniaFact]
    public async Task The_result_of_php_cs_fixer_waits_in_the_review_and_changes_the_document_only_when_applied()
    {
        if (Environment.GetEnvironmentVariable("TEXTIDA_TEST_PHP") is not { } php || Environment.GetEnvironmentVariable("TEXTIDA_TEST_PHAR") is not { } phar) return; // PHP and the PHAR are not available here
        Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(php) + Path.PathSeparator + _path);
        File.Copy(phar, Path.Combine(_temp.Path, "php-cs-fixer.phar"));
        _temp.File(".php-cs-fixer.dist.php", "<?php\nreturn (new PhpCsFixer\\Config())->setRules(['@PSR12' => true]);\n");
        var w = _app.CreateWindow(null);
        var tab = w.OpenFile(_temp.File("a.php", "<?php\nfunction  f( $a ){return $a+1;}\n"), null);

        await w.FormatWithReviewAsync(tab);

        Assert.NotNull(w.FormatDecision);
        Assert.False(w.FormatDecision!.IsCompleted);
        Assert.Equal("<?php\nfunction  f( $a ){return $a+1;}\n", tab.Editor!.Document.Text.ReplaceLineEndings("\n")); // nothing is written yet
        var report = w.ApplyPlan()!;
        Assert.Equal(1, report.Applied);
        Assert.Equal("<?php\n\nfunction f($a)\n{\n    return $a + 1;\n}\n", tab.Editor.Document.Text.ReplaceLineEndings("\n"));
    }
}
