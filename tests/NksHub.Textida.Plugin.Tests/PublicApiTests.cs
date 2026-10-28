using System.Reflection;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Tests;

/// <summary>P8b.2.1: the public surface of the SDK package is a list in the repository, and a test fails when the code and the list differ.</summary>
public class PublicApiTests
{
    [Fact]
    public void The_public_api_of_the_sdk_package_is_the_one_in_the_baseline_file()
    {
        var actual = PublicApiBaseline.Current();
        var file = PublicApiBaseline.FilePath();

        // TEXTIDA_UPDATE_PUBLIC_API=1 rewrites the file: a change of the API is then a change of that file in the same commit, and the reviewer sees it
        if (Environment.GetEnvironmentVariable("TEXTIDA_UPDATE_PUBLIC_API") == "1") File.WriteAllLines(file, actual);

        Assert.True(File.Exists(file), "PublicApi.txt is missing; run the tests with TEXTIDA_UPDATE_PUBLIC_API=1 to write it.");
        var expected = File.ReadAllLines(file);
        var added = actual.Except(expected).ToList();
        var removed = expected.Except(actual).ToList();
        Assert.True(added.Count == 0 && removed.Count == 0,
            $"The public API changed. Added:\n{string.Join("\n", added)}\nRemoved:\n{string.Join("\n", removed)}\nIf this is intended, run the tests with TEXTIDA_UPDATE_PUBLIC_API=1 and commit PublicApi.txt.");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void The_baseline_pins_the_names_the_wire_protocol_is_made_of()
    {
        var lines = File.ReadAllLines(PublicApiBaseline.FilePath());

        Assert.Contains("  const Read : string = \"document.read\"", lines);
        Assert.Contains("  const EditApply : string = \"edit/apply\"", lines);
        Assert.Contains("  const RevisionConflict : int = -32020", lines);
        Assert.Contains("  const Version : int = 1", lines);
    }

    [Fact]
    public void A_public_member_without_an_xml_comment_fails_the_build_of_the_sdk_and_of_the_connection_layer()
    {
        foreach (var project in new[] { "NksHub.Textida.Plugin.Sdk", "NksHub.Textida.Rpc" })
        {
            var text = File.ReadAllText(Path.Combine(T.RepoRoot(), "src", project, project + ".csproj"));
            Assert.Contains("<GenerateDocumentationFile>true</GenerateDocumentationFile>", text);
            Assert.Contains("CS1591", text);
        }
    }

    [Fact]
    public void Error_codes_of_the_plugin_api_are_in_their_ranges_and_none_is_used_twice()
    {
        var document = Constants(typeof(DocumentErrorCodes));
        var ui = Constants(typeof(UiErrorCodes));

        Assert.All(document, code => Assert.InRange(code, -32049, -32020));
        Assert.All(ui, code => Assert.InRange(code, -32059, -32050));
        var all = document.Concat(ui).Concat(Constants(typeof(RpcErrorCodes))).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
    }

    [Fact]
    public void Every_error_code_of_the_plugin_api_is_described_in_the_error_file_with_a_way_back()
    {
        var rows = File.ReadAllLines(Path.Combine(T.RepoRoot(), "src", "NksHub.Textida.Plugin.Sdk", "ERRORS.md")).Where(l => l.StartsWith("| -")).Select(l => l.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToList();

        foreach (var type in new[] { typeof(DocumentErrorCodes), typeof(UiErrorCodes), typeof(RpcErrorCodes) })
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral))
            {
                var code = ((int)field.GetRawConstantValue()!).ToString();
                var row = Assert.Single(rows, r => r[0] == code);
                Assert.Equal(4, row.Length); // code, name, when, what to do
                Assert.Equal(field.Name, row[1]);
                Assert.False(string.IsNullOrWhiteSpace(row[2]) || string.IsNullOrWhiteSpace(row[3]), $"{field.Name} has no description or no way back");
            }
    }

    private static List<int> Constants(Type type) =>
        [.. type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (int)f.GetRawConstantValue()!)];
}
