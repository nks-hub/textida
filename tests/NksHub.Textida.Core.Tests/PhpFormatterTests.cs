using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.5.1: the PHP formatter of the catalog is found in the project, as a PHAR run by php, never downloaded, and its result can be planned for the change review.</summary>
[Collection("PATH")]
public sealed class PhpFormatterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "phpfmt-" + Guid.NewGuid().ToString("N")[..8]);

    public PhpFormatterTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private string Put(string path, string text)
    {
        string full = Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    private const string Config = "<?php\nreturn (new PhpCsFixer\\Config())->setRules(['@PSR12' => true]);\n";

    [Fact]
    public void The_catalog_has_php_cs_fixer_for_php_files_with_its_configuration_files_and_a_phar_and_nothing_to_download()
    {
        var def = ConfigFormatters.Find("php-cs-fixer")!;
        Assert.Equal(("php-cs-fixer", "php-cs-fixer.phar", true), (def.Executable, def.Archive, def.ProjectLocal));
        Assert.Contains(".php-cs-fixer.dist.php", def.ConfigFiles);
        string file = Put("src/a.php", "<?php\n");
        Assert.DoesNotContain(ConfigFormatters.Offered(file), f => f.Id == "php-cs-fixer"); // no configuration yet
        Put(".php-cs-fixer.dist.php", Config);
        Assert.Contains(ConfigFormatters.Offered(file), f => f.Id == "php-cs-fixer");
        Assert.Null(ConfigFormatters.ConfigFor(def, Put("src/a.txt", "x")));
    }

    [Fact]
    public async Task Without_the_program_or_a_phar_the_formatter_is_missing_and_a_phar_without_php_says_so()
    {
        Put(".php-cs-fixer.dist.php", Config);
        string file = Put("src/a.php", "<?php\nfunction  f(){}\n");
        var def = ConfigFormatters.Find("php-cs-fixer")!;
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        var saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "");
            var missing = await ConfigFormatters.FormatAsync(doc, def, TimeSpan.FromSeconds(30));
            Assert.Equal(FormatResult.Missing, missing.Result);
            Assert.Contains("php-cs-fixer", missing.Reason);

            Put("tools/php-cs-fixer.phar", "not really a phar");
            var noPhp = await ConfigFormatters.FormatAsync(doc, def, TimeSpan.FromSeconds(30));
            Assert.Equal(FormatResult.Missing, noPhp.Result);
            Assert.Contains("'php' was not found", noPhp.Reason);
        }
        finally { Environment.SetEnvironmentVariable("PATH", saved); }
    }

    /// <summary>With TEXTIDA_TEST_PHP and TEXTIDA_TEST_PHAR set (a php program and a php-cs-fixer.phar), the PHAR in the project is run by php and the edit is planned.</summary>
    [Fact]
    public async Task A_phar_in_the_project_is_run_by_php_and_the_result_is_planned_without_touching_the_document()
    {
        if (Environment.GetEnvironmentVariable("TEXTIDA_TEST_PHP") is not { } php || Environment.GetEnvironmentVariable("TEXTIDA_TEST_PHAR") is not { } phar) return;
        Put(".php-cs-fixer.dist.php", Config);
        string file = Put("src/a.php", "<?php\nfunction  f( $a ){return $a+1;}\n");
        File.Copy(phar, Path.Combine(_root, "php-cs-fixer.phar"));
        var def = ConfigFormatters.Find("php-cs-fixer")!;
        using var doc = Doc.Open(file, ReferenceBackendFactory.Instance);
        var saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", Path.GetDirectoryName(php) + Path.PathSeparator + saved); // php on the PATH, php-cs-fixer not
            var plan = await ConfigFormatters.PlanAsync(doc, def, TimeSpan.FromSeconds(120));

            Assert.Equal(FormatResult.Formatted, plan.Outcome.Result);
            Assert.Equal("<?php\nfunction  f( $a ){return $a+1;}\n", doc.Codec.GetString(doc.ReadBytes(new ByteRange(0, doc.ByteLength)))); // not changed
            string before = doc.Codec.GetString(doc.ReadBytes(new ByteRange(0, doc.ByteLength)));
            string after = string.Concat(before.AsSpan(0, (int)plan.Range.Start), plan.NewText, before.AsSpan((int)plan.Range.End));
            Assert.Equal("<?php\n\nfunction f($a)\n{\n    return $a + 1;\n}\n", after.ReplaceLineEndings("\n"));
            Assert.Empty(Directory.GetFiles(_root, "*.textida-fmt-*", SearchOption.AllDirectories));
        }
        finally { Environment.SetEnvironmentVariable("PATH", saved); }
    }
}
