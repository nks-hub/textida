using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Conversions;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;

namespace NksHub.Textida.App.Tests;

/// <summary>
/// P8.1.2: a saved recipe over a set of files. The recipe of the first recipe version (a data plugin manifest) runs over new files with a preview,
/// a conflict in one file leaves only that file out, a format step runs a formatter of the save actions, and a file in another encoding keeps its bytes and line endings.
/// </summary>
public sealed class RecipeFilesTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A manifest as the first recipe version wrote it: no version in the recipe, the converter in the manifest next to it.</summary>
    private const string OldManifest = """
        {
          "schemaVersion": 1, "id": "acme.old", "name": "Old recipes", "version": "1.0.0", "api": {"min": 1, "max": 1},
          "contributes": {
            "converters": [{"id": "colon", "title": "Colon", "rules": [{"find": "=", "replace": ": "}]}],
            "recipes": [{"id": "rename", "title": "Rename price", "steps": [
              {"kind": "matches", "find": "price=\\d+", "regex": true},
              {"kind": "replace", "find": "price", "replacement": "cost"},
              {"kind": "convert", "converter": "colon"}]}]
          }
        }
        """;

    private static (RecipeContribution Recipe, IReadOnlyList<ConverterContribution> Converters) OldRecipe()
    {
        var manifest = JsonSerializer.Deserialize(OldManifest, PluginManifestJson.Default.PluginManifest)!;
        return (manifest.Contributes.Recipes[0], manifest.Contributes.Converters);
    }

    private static Task<ReplacePlan> Plan(RecipeContribution recipe, IReadOnlyList<ConverterContribution> converters, IEnumerable<string> paths,
        TimeSpan? timeout = null, Func<string, FormatterDef?>? formatters = null) =>
        RecipeBatch.PlanAsync(recipe, converters, paths, [], new VariableContext(), timeout, formatters);

    private ChangeApplier Applier() => new(_ => null, Path.Combine(_temp.Path, "journal"));

    [Fact]
    public async Task A_recipe_saved_by_the_first_version_runs_over_a_new_folder_and_the_preview_shows_the_right_changes()
    {
        var (recipe, converters) = OldRecipe();
        Assert.Empty(RecipeRunner.Check(recipe, converters.Select(c => c.Id).ToHashSet()));
        _temp.File("a.txt", "intro\nprice=10 and more\n");
        _temp.File("b.txt", "nothing here\n");
        _temp.File("c.txt", "price=7\nprice=8\n");
        _temp.File("d.md", "price=99\n");

        var plan = await Plan(recipe, converters, RecipeBatch.FolderFiles(_temp.Path, "*.txt"));

        Assert.Empty(plan.Skipped);
        Assert.Equal(["a.txt", "c.txt"], plan.Proposal.Files.Select(f => Path.GetFileName(f.DisplayName)));
        Assert.Equal("recipe:rename", plan.Proposal.Origin);
        var a = plan.Proposal.Files[0].Hunks.Single();
        Assert.Equal(("price=10", "cost: 10", 2), (a.OldText, a.NewText, a.Line));
        Assert.Equal([("price=7", "cost: 7", 1), ("price=8", "cost: 8", 2)], plan.Proposal.Files[1].Hunks.Select(h => (h.OldText, h.NewText, h.Line)));

        var report = Applier().Apply(plan.Proposal, new ChangeSelection());
        Assert.Equal((2, 0), (report.Applied, report.Conflicts));
        Assert.Equal("intro\ncost: 10 and more\n", File.ReadAllText(Path.Combine(_temp.Path, "a.txt")));
        Assert.Equal("price=99\n", File.ReadAllText(Path.Combine(_temp.Path, "d.md")));
    }

    [Fact]
    public async Task A_conflict_in_one_of_three_files_skips_that_file_reports_it_and_applies_the_other_two()
    {
        var (recipe, converters) = OldRecipe();
        var paths = new[] { _temp.File("one.txt", "price=1\n"), _temp.File("two.txt", "price=2\n"), _temp.File("three.txt", "price=3\n") };
        var plan = await Plan(recipe, converters, paths);
        Assert.Equal(3, plan.Proposal.Files.Count);

        File.WriteAllText(paths[1], "price=2\nchanged by another program\n"); // after the preview

        var report = Applier().Apply(plan.Proposal, new ChangeSelection());

        Assert.Equal((2, 1, 0), (report.Applied, report.Conflicts, report.Failed));
        var conflict = Assert.Single(report.Files, f => f.Status == ChangeStatus.Conflict);
        Assert.Equal(ChangeConflict.ExternalChange, conflict.Conflict);
        Assert.True(conflict.NeedsRepreview);
        Assert.EndsWith("two.txt", conflict.DisplayName);
        Assert.Equal("cost: 1\n", File.ReadAllText(paths[0]));
        Assert.Equal("price=2\nchanged by another program\n", File.ReadAllText(paths[1]));
        Assert.Equal("cost: 3\n", File.ReadAllText(paths[2]));
    }

    private FormatterDef Fake(string mode = "--format") => new("fake", "Fake", ToolTestKit.Dotnet, [ToolTestKit.EchoDll, mode, "${File}"], [], [".txt"]);

    private static RecipeContribution ReplaceThenFormat() => new()
    {
        Id = "shout", Title = "Shout",
        Steps = [new RecipeStep { Kind = RecipeStep.Replace, Find = "beta", Replacement = "gamma" }, new RecipeStep { Kind = RecipeStep.Format, Formatter = "fake" }],
    };

    [Fact]
    public async Task A_format_step_runs_the_formatter_over_the_result_of_the_steps_before_it_as_one_previewed_change()
    {
        var path = _temp.File("f.txt", "alpha\nbeta\nomega\n");
        var plan = await Plan(ReplaceThenFormat(), [], [path], TimeSpan.FromSeconds(60), _ => Fake());

        Assert.Empty(plan.Skipped);
        var hunk = Assert.Single(plan.Proposal.Files.Single().Hunks);
        Assert.Equal(("ALPHA\nGAMMA\nOMEGA\n", "alpha\nbeta\nomega\n"), (hunk.NewText, hunk.OldText));
        Assert.Equal([path], Directory.GetFiles(_temp.Path)); // no temporary copy is left behind
        Assert.Equal("alpha\nbeta\nomega\n", File.ReadAllText(path)); // the preview wrote nothing

        Assert.Equal(1, Applier().Apply(plan.Proposal, new ChangeSelection()).Applied);
        Assert.Equal("ALPHA\nGAMMA\nOMEGA\n", File.ReadAllText(path));
    }

    [Fact]
    public async Task A_format_step_that_cannot_run_leaves_that_file_out_with_the_reason_and_the_other_files_stay()
    {
        var good = _temp.File("good.txt", "beta\n");
        var wrongType = _temp.File("notes.md", "beta\n");

        var failing = await Plan(ReplaceThenFormat(), [], [good], TimeSpan.FromSeconds(60), _ => Fake("--format-fail"));
        Assert.Empty(failing.Proposal.Files);
        Assert.Contains("syntax error", Assert.Single(failing.Skipped));

        var noLimit = await Plan(ReplaceThenFormat(), [], [good], null, _ => Fake());
        Assert.Contains("formatTimeoutSeconds", Assert.Single(noLimit.Skipped));

        var mixed = await Plan(ReplaceThenFormat(), [], [good, wrongType], TimeSpan.FromSeconds(60), _ => Fake());
        Assert.Equal([good], mixed.Proposal.Files.Select(f => f.DisplayName));
        Assert.Contains("does not format .md", Assert.Single(mixed.Skipped));
        Assert.Equal("beta\n", File.ReadAllText(good));
    }

    [Fact]
    public async Task A_file_in_another_encoding_keeps_its_other_bytes_and_its_line_endings()
    {
        var recipe = new RecipeContribution
        {
            Id = "lines", Title = "Lines",
            Steps = [new RecipeStep { Kind = RecipeStep.Matches, Find = "price=\\d+", Regex = true }, new RecipeStep { Kind = RecipeStep.Set, Text = "cena\n${selection}" }],
        };
        var original = TextCodec.Cp1250.Encode("Příliš žluťoučký kůň úpěl ďábelské ódy\r\nprice=10\r\nděkuji\r\n");
        var path = Path.Combine(_temp.Path, "cz.txt");
        File.WriteAllBytes(path, original);

        var plan = await Plan(recipe, [], [path]);

        var change = plan.Proposal.Files.Single();
        Assert.False(change.Codec.SameBytesAs(TextCodec.Utf8));
        Assert.Equal("cena\r\nprice=10", change.Hunks.Single().NewText); // the line break of the file, not the one of the platform
        Assert.Equal(1, Applier().Apply(plan.Proposal, new ChangeSelection()).Applied);
        Assert.Equal(change.Codec.Encode("Příliš žluťoučký kůň úpěl ďábelské ódy\r\ncena\r\nprice=10\r\nděkuji\r\n"), File.ReadAllBytes(path));
    }

    [Fact]
    public void A_recipe_file_without_a_version_is_version_one_and_loads_and_the_saved_form_carries_the_current_version()
    {
        const string old = """{"id":"x","title":"X","steps":[{"kind":"replace","find":"a","replacement":"b"}]}""";
        var loaded = RecipeFile.Parse(old, out var error);
        Assert.Null(error);
        Assert.Equal(1, loaded!.Version);

        var saved = RecipeFile.Of(loaded.Recipe, []).ToJson();
        Assert.Contains("\"version\": 2", saved);
        Assert.Equal("x", RecipeFile.Parse(saved, out _)!.Id);

        Assert.Null(RecipeFile.Parse(saved.Replace("\"version\": 2", "\"version\": 3"), out var newer));
        Assert.Contains("version 3", newer);
        Assert.Null(RecipeFile.Parse("""{"id":"x","title":"X","steps":[{"kind":"format","formatter":"f"},{"kind":"replace","find":"a","replacement":"b"}]}""", out var order));
        Assert.Contains("format steps come last", order);
    }
}
