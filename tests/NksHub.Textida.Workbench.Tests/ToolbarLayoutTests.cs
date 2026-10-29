using NksHub.Textida.Workbench.Toolbar;

namespace NksHub.Textida.Workbench.Tests;

/// <summary>P3.6.13: the buttons of the user toolbar, their file and the rules that keep the list usable.</summary>
public class ToolbarLayoutTests
{
    private const string Sep = ToolbarLayout.Separator;

    [Fact]
    public void The_first_set_is_the_one_of_the_brief_and_every_id_in_it_is_valid()
    {
        var names = ToolbarLayout.Default.Where(b => b != Sep).ToList();
        Assert.Equal(["nks.file.new", "nks.file.open", "nks.file.save", "nks.file.saveAll", "nks.edit.undo", "nks.edit.redo", "nks.search.find", "nks.search.replace", "nks.git.commit", "nks.git.pull", "nks.git.push"], names);
        Assert.All(ToolbarLayout.Default, b => Assert.True(ToolbarLayout.IsValidButton(b), b));
        Assert.Equal(ToolbarLayout.Default, ToolbarLayout.Clean(ToolbarLayout.Default, 40));
    }

    [Fact]
    public void Every_command_of_the_first_set_but_the_git_ones_is_a_builtin_command_with_an_icon()
    {
        var host = WorkbenchHost.CreateBuiltin();
        foreach (var id in ToolbarLayout.Default.Where(b => b != Sep && !b.StartsWith("nks.git.", StringComparison.Ordinal)))
        {
            Assert.True(host.Commands.TryGet(id, out _), id);
            Assert.NotNull(host.Commands.IconOf(id));
        }
    }

    [Fact]
    public void A_button_is_a_command_id_or_a_separator()
    {
        Assert.True(ToolbarLayout.IsValidButton("nks.file.save"));
        Assert.True(ToolbarLayout.IsValidButton("acme.demo.run"));
        Assert.True(ToolbarLayout.IsValidButton(Sep));
        Assert.False(ToolbarLayout.IsValidButton(""));
        Assert.False(ToolbarLayout.IsValidButton(null));
        Assert.False(ToolbarLayout.IsValidButton("Save"));
        Assert.False(ToolbarLayout.IsValidButton("--"));
        Assert.False(ToolbarLayout.IsValidButton("nks file save"));
    }

    [Fact]
    public void A_list_loses_duplicates_stray_separators_and_what_is_over_the_limit()
    {
        Assert.Equal(["a.b", Sep, "c.d"], ToolbarLayout.Clean(["a.b", "a.b", Sep, Sep, "c.d", Sep], 10));
        Assert.Equal(["a.b", "c.d"], ToolbarLayout.Clean([Sep, "a.b", Sep, "c.d", Sep], 10).Where(b => b != Sep));
        Assert.Equal(["a.b", "c.d"], ToolbarLayout.Clean(["a.b", "not valid", "c.d"], 10));
        Assert.Equal(["a.b", Sep, "c.d"], ToolbarLayout.Clean(["a.b", Sep, "c.d", Sep, "e.f"], 3));
        Assert.Equal(["a.b"], ToolbarLayout.Clean(["a.b", Sep, "c.d"], 2));
        Assert.Empty(ToolbarLayout.Clean([Sep, Sep], 10));
        Assert.Empty(ToolbarLayout.Clean([], 10));
    }

    [Fact]
    public void A_button_whose_command_is_missing_is_not_drawn_and_its_separator_goes_with_it()
    {
        var all = ToolbarLayout.Shown(ToolbarLayout.Default, _ => true);
        Assert.Equal(ToolbarLayout.Default, all);
        var withoutGit = ToolbarLayout.Shown(ToolbarLayout.Default, id => !id.StartsWith("nks.git.", StringComparison.Ordinal));
        Assert.Equal(["nks.file.new", "nks.file.open", "nks.file.save", "nks.file.saveAll", Sep, "nks.edit.undo", "nks.edit.redo", Sep, "nks.search.find", "nks.search.replace"], withoutGit);
        Assert.Equal(["nks.edit.undo", "nks.edit.redo"], ToolbarLayout.Shown(ToolbarLayout.Default, id => id.StartsWith("nks.edit.", StringComparison.Ordinal)));
        Assert.Empty(ToolbarLayout.Shown(ToolbarLayout.Default, _ => false));
        // a command in the middle that is gone leaves one separator between its neighbours
        Assert.Equal(["a.a", Sep, "c.c"], ToolbarLayout.Shown(["a.a", Sep, "b.b", Sep, "c.c"], id => id != "b.b"));
    }

    [Fact]
    public void The_file_round_trips_and_says_what_is_wrong_when_it_cannot_be_used()
    {
        var text = ToolbarLayout.Write(ToolbarLayout.Default);
        Assert.Contains("\"schemaVersion\": 1", text, StringComparison.Ordinal);
        Assert.Contains("\"buttons\"", text, StringComparison.Ordinal);
        Assert.True(ToolbarLayout.TryRead(text, 40, out var back, out var problem), problem);
        Assert.Equal(ToolbarLayout.Default, back);
        Assert.Null(problem);

        foreach (var (json, mention) in new[]
        {
            ("{ not json", ""),
            ("", ""),
            ("null", "empty"),
            ("{\"schemaVersion\":2,\"buttons\":[]}", "schemaVersion"),
            ("{\"schemaVersion\":1,\"buttons\":[\"nks.file.new\",\"Save\"]}", "Save"),
            ("{\"schemaVersion\":1,\"buttons\":[\"nks.file.new\"],\"extra\":1}", "extra"),
            ("{\"schemaVersion\":1,\"buttons\":[\"a.b\",\"c.d\",\"e.f\"]}", "more than 2"),
        })
        {
            Assert.False(ToolbarLayout.TryRead(json, 2, out var fallback, out problem), json);
            Assert.False(string.IsNullOrWhiteSpace(problem));
            Assert.Contains(mention, problem!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ToolbarLayout.Default, fallback);
        }
    }

    [Fact]
    public void A_file_with_comments_and_trailing_commas_is_read_and_stray_entries_are_cleaned()
    {
        const string json = """
            {
              // my buttons
              "schemaVersion": 1,
              "buttons": ["-", "nks.file.new", "nks.file.new", "-", "-", "nks.file.open", "-",],
            }
            """;
        Assert.True(ToolbarLayout.TryRead(json, 40, out var buttons, out var problem), problem);
        Assert.Equal(["nks.file.new", Sep, "nks.file.open"], buttons);
    }

    [Fact]
    public void An_empty_toolbar_is_a_valid_toolbar()
    {
        Assert.True(ToolbarLayout.TryRead("""{"schemaVersion":1,"buttons":[]}""", 40, out var buttons, out _));
        Assert.Empty(buttons);
    }
}
