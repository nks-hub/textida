using System.Text.Json;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;
using NksHub.Textida.Workbench.Icons;

namespace NksHub.Textida.Workbench.Tests;

/// <summary>P3.6.14: the SVG icon set, the reader of a plugin's icon and the icon of a command.</summary>
public class IconTests
{
    /// <summary>The icons the Git menu needs. Their commands come with the Git work, so until they exist these may be the only icons no command names.</summary>
    private static readonly string[] ReservedForGit = ["git-blame", "git-commit", "git-conflict", "git-fetch", "git-history", "git-merge", "git-pull", "git-push", "git-revert"];

    private static string SvgFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NksHub.Textida.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "NksHub.Textida.Workbench", "Icons", "svg");
    }

    private const string Good = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="none" stroke="currentColor"><path d="M2 2H14V14H2Z"/><circle cx="8" cy="8" r="3" class="accent"/></svg>""";

    [Fact]
    public void A_plain_icon_is_read_into_paths_with_their_paint()
    {
        Assert.True(SvgIcon.TryParse(Good, out var shape, out var error), error);
        Assert.Equal(16, shape!.Size);
        Assert.Equal(2, shape.Paths.Count);
        Assert.Equal(new IconPath("M2 2H14V14H2Z", IconPaint.Line), shape.Paths[0]);
        Assert.Equal(IconPaint.AccentLine, shape.Paths[1].Paint);
        Assert.StartsWith("M5,8 A3,3 0 1 0 11,8", shape.Paths[1].Data, StringComparison.Ordinal);
    }

    [Fact]
    public void Shapes_become_path_data_and_fills_are_told_from_lines()
    {
        const string svg = """
            <svg viewBox="0 0 16 16" fill="none" stroke="currentColor">
              <rect x="1" y="2" width="4" height="6"/>
              <rect x="1" y="2" width="4" height="6" rx="1"/>
              <line x1="0" y1="0" x2="4" y2="4"/>
              <polyline points="1,1 2,2 3,1"/>
              <polygon points="1,1 2,2 3,1" fill="currentColor" stroke="none"/>
              <ellipse cx="8" cy="8" rx="4" ry="2" class="accent" fill="currentColor" stroke="none"/>
            </svg>
            """;
        Assert.True(SvgIcon.TryParse(svg, out var shape, out var error), error);
        var paths = shape!.Paths;
        Assert.Equal("M1,2 L5,2 L5,8 L1,8 Z", paths[0].Data);
        Assert.StartsWith("M2,2 H4 A1,1", paths[1].Data, StringComparison.Ordinal);
        Assert.Equal("M0,0 L4,4", paths[2].Data);
        Assert.Equal("M1,1 L2,2 L3,1", paths[3].Data);
        Assert.Equal([IconPaint.Line, IconPaint.Line, IconPaint.Line, IconPaint.Line, IconPaint.Fill, IconPaint.AccentFill], paths.Select(p => p.Paint));
    }

    [Fact]
    public void A_root_that_fills_makes_shapes_without_a_paint_fills()
    {
        Assert.True(SvgIcon.TryParse("""<svg viewBox="0 0 24 24" fill="currentColor"><path d="M0 0L24 0L12 24Z"/></svg>""", out var shape, out var error), error);
        Assert.Equal(24, shape!.Size);
        Assert.Equal(IconPaint.Fill, Assert.Single(shape.Paths).Paint);
    }

    [Theory]
    [InlineData("""<svg viewBox="0 0 16 16"><script>alert(1)</script><path d="M0 0L1 1"/></svg>""", "script")]
    [InlineData("""<svg viewBox="0 0 16 16"><path d="M0 0L1 1" onload="x()"/></svg>""", "onload")]
    [InlineData("""<svg viewBox="0 0 16 16"><image href="file:///c:/secret.png"/></svg>""", "image")]
    [InlineData("""<svg viewBox="0 0 16 16"><use href="other.svg#a"/></svg>""", "use")]
    [InlineData("""<svg viewBox="0 0 16 16"><style>path{fill:red}</style><path d="M0 0L1 1"/></svg>""", "style")]
    [InlineData("""<svg viewBox="0 0 16 16"><path d="M0 0L1 1" style="fill:red"/></svg>""", "style")]
    [InlineData("""<svg viewBox="0 0 16 16"><path d="M0 0L1 1" fill="#ff0000"/></svg>""", "#ff0000")]
    [InlineData("""<svg viewBox="0 0 16 16"><path d="M0 0L1 1" stroke="url(#g)"/></svg>""", "url(#g)")]
    [InlineData("""<svg viewBox="0 0 16 16"><foreignObject/></svg>""", "foreignObject")]
    [InlineData("""<svg viewBox="0 0 16 16"><svg viewBox="0 0 8 8"><path d="M0 0L1 1"/></svg></svg>""", "nested")]
    [InlineData("""<html><path d="M0 0L1 1"/></html>""", "root")]
    [InlineData("""<svg><path d="M0 0L1 1"/></svg>""", "viewBox")]
    [InlineData("""<svg viewBox="0 0 16 12"><path d="M0 0L1 1"/></svg>""", "square")]
    [InlineData("""<svg viewBox="1 1 16 16"><path d="M0 0L1 1"/></svg>""", "square")]
    [InlineData("""<svg viewBox="0 0 4 4"><path d="M0 0L1 1"/></svg>""", "between")]
    [InlineData("""<svg viewBox="0 0 16 16"></svg>""", "nothing")]
    [InlineData("""<svg viewBox="0 0 16 16"><path/></svg>""", "path data")]
    [InlineData("""<svg viewBox="0 0 16 16"><circle cx="1" cy="1" r="0"/></svg>""", "positive")]
    [InlineData("<svg viewBox=\"0 0 16 16\"", "valid")]
    [InlineData("""<!DOCTYPE svg [<!ENTITY a "aaaa">]><svg viewBox="0 0 16 16"><path d="M0 0L1 1"/></svg>""", "valid")]
    [InlineData("", "empty")]
    public void An_icon_that_is_more_than_shapes_is_refused_and_says_why(string svg, string mention)
    {
        Assert.False(SvgIcon.TryParse(svg, out var shape, out var error));
        Assert.Null(shape);
        Assert.Contains(mention, error, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithShapes(int count) => "<svg viewBox=\"0 0 16 16\">" + string.Concat(Enumerable.Repeat("<path d=\"M0 0L1 1\"/>", count)) + "</svg>";

    [Fact]
    public void An_icon_with_too_many_shapes_is_refused()
    {
        Assert.False(SvgIcon.TryParse(WithShapes(SvgIcon.MaxShapes + 1), out _, out var error));
        Assert.Contains(SvgIcon.MaxShapes.ToString(), error, StringComparison.Ordinal);
        Assert.True(SvgIcon.TryParse(WithShapes(SvgIcon.MaxShapes), out _, out _));
    }

    [Theory]
    [InlineData("M0 0")]
    [InlineData("M0,0 L1,1 L2,0 Z")]
    [InlineData("m1.5-2.5l-3 4z")]
    [InlineData("M0 0C1 1 2 2 3 3 4 4 5 5 6 6")]
    [InlineData("M2 2A3 3 0 1 0 8 2")]
    [InlineData("M2 2a3 3 0 10.5 2")]
    [InlineData("M1e1 1E-1H3V4")]
    [InlineData("M0 0 L1 1 Z M3 3 L4 4")]
    public void Well_formed_path_data_is_accepted(string data) => Assert.True(SvgIcon.IsPathData(data), data);

    [Theory]
    [InlineData("")]
    [InlineData("M")]
    [InlineData("M0")]
    [InlineData("L0 0")]
    [InlineData("M0 0 L1")]
    [InlineData("M0 0 L1 1 2")]
    [InlineData("M0 0 X1 1")]
    [InlineData("M0 0 Z1")]
    [InlineData("M0 0 A1 1 0 2 0 3 3")]
    [InlineData("M0 0 C1 1 2 2 3")]
    [InlineData("M0 0 L1 1 url(")]
    [InlineData("M0 0 L1e 1")]
    [InlineData("M0 0 L-- 1")]
    [InlineData("M0 0 L1 1 H")]
    public void Path_data_a_drawing_could_not_parse_is_refused(string data) => Assert.False(SvgIcon.IsPathData(data), data);

    [Fact]
    public void Path_data_is_bounded()
    {
        Assert.False(SvgIcon.IsPathData("M0 0" + string.Concat(Enumerable.Repeat(" L1 1", 3000))));
    }

    [Fact]
    public void The_built_in_set_is_the_set_of_svg_files_in_the_repository_and_every_file_reads_the_same_way()
    {
        var files = Directory.GetFiles(SvgFolder(), "*.svg").OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.True(files.Count >= 100, $"{files.Count} icons");
        Assert.Equal(files.Select(f => Path.GetFileNameWithoutExtension(f)), BuiltinIcons.All.Keys.Order(StringComparer.Ordinal));
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            Assert.True(SvgIcon.TryParse(File.ReadAllText(file), out var shape, out var error), $"{name}: {error}");
            var built = BuiltinIcons.All[name];
            Assert.Equal(shape!.Size, built.Size);
            Assert.Equal(shape.Paths, built.Paths);
        }
    }

    [Fact]
    public void Every_built_in_icon_is_a_16_unit_drawing_with_a_few_valid_paths_inside_its_square()
    {
        foreach (var (name, icon) in BuiltinIcons.All)
        {
            Assert.Matches("^[a-z][a-z0-9-]*$", name);
            Assert.Equal(16, icon.Size);
            Assert.InRange(icon.Paths.Count, 1, 12);
            Assert.All(icon.Paths, p => Assert.True(SvgIcon.IsPathData(p.Data), name + ": " + p.Data));
        }
    }

    [Fact]
    public void No_built_in_icon_carries_a_colour_of_its_own()
    {
        // an icon draws with the text colour, and at most with the accent: the paint says which, never a colour
        var raw = Directory.GetFiles(SvgFolder(), "*.svg").Select(File.ReadAllText).ToList();
        Assert.All(raw, svg =>
        {
            Assert.DoesNotContain("#", svg, StringComparison.Ordinal);
            Assert.DoesNotContain("rgb", svg, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Every_command_of_the_builtin_manifest_has_an_icon_or_says_text_and_names_a_real_icon()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var missing = host.Commands.Commands.Where(c => !c.Hidden && c.Icon is null).Select(c => c.Id).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "These commands have no icon: give them one from Icons/svg, or \"icon\": \"text\" when a picture would add nothing: " + string.Join(", ", missing));
        var unknown = host.Commands.Commands.Where(c => c.Icon is not null && c.Icon != BuiltinIcons.TextOnly && !BuiltinIcons.All.ContainsKey(c.Icon)).Select(c => c.Id + " -> " + c.Icon).ToList();
        Assert.Empty(unknown);
        // a command with a picture has one to draw
        var withIcon = host.Commands.Commands.Where(c => c.Icon is not null and not BuiltinIcons.TextOnly).ToList();
        Assert.True(withIcon.Count >= 250, $"{withIcon.Count} commands with an icon");
        Assert.All(withIcon, c => Assert.NotNull(host.Commands.IconOf(c.Id)));
    }

    [Fact]
    public void No_icon_of_the_set_is_left_without_a_command_except_the_ones_kept_for_git()
    {
        var host = WorkbenchHost.CreateBuiltin();
        var used = host.Commands.Commands.Select(c => c.Icon).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var unused = BuiltinIcons.All.Keys.Where(n => !used.Contains(n)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unused.All(ReservedForGit.Contains), "No command uses: " + string.Join(", ", unused.Except(ReservedForGit)));
        Assert.All(ReservedForGit, n => Assert.True(BuiltinIcons.All.ContainsKey(n), n));
    }

    [Fact]
    public void The_set_has_the_git_icons_of_the_brief()
    {
        foreach (var name in new[] { "git-commit", "git-pull", "git-push", "git-fetch", "git-branch", "git-diff", "git-stage", "git-revert", "git-blame", "git-history" })
            Assert.True(BuiltinIcons.TryGet(name, out _), name);
    }

    [Fact]
    public void The_icon_of_a_command_comes_from_its_plugin_or_the_set_and_goes_with_its_source()
    {
        var registry = new CommandRegistry();
        var manifest = new ContributionManifest
        {
            Id = "acme.demo",
            Commands =
            [
                new CommandContribution { Id = "acme.demo.named", Title = "t", Icon = "save", Aliases = ["demo-alias"] },
                new CommandContribution { Id = "acme.demo.file", Title = "t", Icon = "icons/own.svg" },
                new CommandContribution { Id = "acme.demo.text", Title = "t", Icon = "text" },
                new CommandContribution { Id = "acme.demo.none", Title = "t" },
            ],
        };
        Assert.Empty(registry.AddManifest(manifest));
        Assert.True(SvgIcon.TryParse(Good, out var own, out _));
        registry.SetIcon("acme.demo.file", own!);

        Assert.Same(BuiltinIcons.All["save"], registry.IconOf("acme.demo.named"));
        Assert.Same(BuiltinIcons.All["save"], registry.IconOf("demo-alias"));
        Assert.Same(own, registry.IconOf("acme.demo.file"));
        Assert.Null(registry.IconOf("acme.demo.text"));
        Assert.Null(registry.IconOf("acme.demo.none"));
        Assert.Null(registry.IconOf("acme.demo.unknown"));
        Assert.Throws<ArgumentException>(() => registry.SetIcon("acme.demo.unknown", own!));

        registry.RemoveSource("acme.demo");
        Assert.Null(registry.IconOf("acme.demo.file"));
        Assert.Empty(registry.AddManifest(manifest));
        Assert.Null(registry.IconOf("acme.demo.file"));
    }

    private static IReadOnlyList<string> Validate(string? icon, ManifestTrust trust)
    {
        var id = trust == ManifestTrust.Builtin ? "nks.demo" : "acme.demo";
        return ManifestLoader.Validate(new ContributionManifest { Id = id, Commands = [new CommandContribution { Id = id + ".run", Title = "t", Icon = icon }] }, trust);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("text")]
    [InlineData("save")]
    [InlineData("git-commit")]
    public void A_manifest_may_name_a_built_in_icon_or_text_or_nothing(string? icon)
    {
        Assert.Empty(Validate(icon, ManifestTrust.Builtin));
        Assert.Empty(Validate(icon, ManifestTrust.Plugin));
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("Save")]
    [InlineData("")]
    [InlineData("svg")]
    [InlineData("../own.svg")]
    [InlineData("C:/icons/own.svg")]
    public void An_unknown_or_unusable_icon_is_a_manifest_error(string icon)
    {
        Assert.Contains(Validate(icon, ManifestTrust.Builtin), e => e.Contains("icon", StringComparison.Ordinal));
        Assert.Contains(Validate(icon, ManifestTrust.Plugin), e => e.Contains("icon", StringComparison.Ordinal));
    }

    [Fact]
    public void A_plugin_may_name_an_svg_file_inside_its_folder_and_a_built_in_command_may_not()
    {
        Assert.Empty(Validate("icons/own.svg", ManifestTrust.Plugin));
        Assert.Empty(Validate("own.svg", ManifestTrust.Plugin));
        Assert.NotEmpty(Validate("icons/own.svg", ManifestTrust.Builtin));
        Assert.NotEmpty(Validate("icons\\own.svg", ManifestTrust.Plugin));
        Assert.NotEmpty(Validate("/own.svg", ManifestTrust.Plugin));
        Assert.NotEmpty(Validate("c:own.svg", ManifestTrust.Plugin));
        Assert.True(ManifestLoader.IsIconFile("a/b.svg"));
        Assert.False(ManifestLoader.IsIconFile("a/b.png"));
        Assert.False(ManifestLoader.IsIconFile(".svg"));
        Assert.False(ManifestLoader.IsIconFile(null));
    }

    [Fact]
    public void The_icon_of_a_command_is_read_from_the_manifest_json()
    {
        var json = """{"schemaVersion":1,"id":"acme.demo","commands":[{"id":"acme.demo.run","title":"t","icon":"play"}]}""";
        var loaded = ManifestLoader.Load(json, ManifestTrust.Plugin);
        Assert.True(loaded.Succeeded, string.Join("; ", loaded.Errors));
        Assert.Equal("play", loaded.Manifest!.Commands[0].Icon);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(loaded.Manifest, ContributionJson.Default.ContributionManifest));
        Assert.Equal("play", doc.RootElement.GetProperty("commands")[0].GetProperty("icon").GetString());
    }
}
