namespace NksPad.App.Tests;

public class HardCodedTextTests
{
    private static IEnumerable<string> Files(string pattern) =>
        Directory.EnumerateFiles(Repo.AppSources, pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Application_code_contains_no_hard_coded_ui_text()
    {
        var violations = Files("*.cs").SelectMany(f => UiLiteralScanner.ScanSource(Path.GetRelativePath(Repo.Root, f), File.ReadAllText(f)))
            .Concat(Files("*.axaml").SelectMany(f => UiLiteralScanner.ScanAxaml(Path.GetRelativePath(Repo.Root, f), File.ReadAllText(f))))
            .ToList();
        Assert.True(violations.Count == 0, "hard-coded UI text:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void The_scan_actually_covers_the_source_tree() => Assert.True(Files("*.cs").Count() >= 10 && Files("*.axaml").Any());

    [Theory]
    [InlineData("class C { void M(){ var t = new TextBlock { Text = \"Save\" }; } }")]
    [InlineData("class C { void M(Button b){ b.Content = \"OK\"; } }")]
    [InlineData("class C { void M(Window w){ w.Title = \"Main\" + Loc.Get(K.AppName); } }")]
    [InlineData("class C { void M(Window w){ w.Title = $\"Open {x}\"; } }")]
    [InlineData("class C { void M(Control c){ AutomationProperties.SetName(c, \"Close\"); } }")]
    [InlineData("class C { void M(){ var i = new NativeMenuItem(\"File\"); } }")]
    [InlineData("class C { void M(){ var i = new MenuCommand(\"File\", () => {}); } }")]
    [InlineData("class C { void M(){ var t = Loc.Get(\"menu.file\"); } }")]
    [InlineData("class C { void M(){ var t = Loc.Plural(\"status.tabs\", 3); } }")]
    [InlineData("class C { void M(TextBox b){ b.PlaceholderText = tab.ReadOnly ? null : \"Type here\"; } }")]
    public void Scanner_flags_hard_coded_text(string code) => Assert.NotEmpty(UiLiteralScanner.ScanSource("x.cs", code));

    [Theory]
    [InlineData("class C { void M(Window w){ w.Title = Loc.Get(K.AppName); } }")]
    [InlineData("class C { void M(Window w){ w.Title = $\"{a} - {Loc.Get(K.AppName)}\"; } }")]
    [InlineData("class C { void M(TextBlock t){ t.Text = \"\"; t.Text = \"·\"; t.Text = \"×\"; t.Text = \"1/2\"; } }")]
    [InlineData("class C { void M(){ var f = new FontFamily(\"Segoe UI\"); var n = Path.Combine(\"a\", \"b\"); } }")]
    [InlineData("class C { void M(Control c){ AutomationProperties.SetName(c, Loc.Get(K.A11yTabs)); } }")]
    [InlineData("class C { void M(){ var t = Loc.Plural(K.StatusTabs, 3, (\"x\", \"y\")); } }")]
    public void Scanner_accepts_catalogue_text_and_non_text(string code) => Assert.Empty(UiLiteralScanner.ScanSource("x.cs", code));

    [Theory]
    [InlineData("<TextBlock Text=\"Hello\" />", 1)]
    [InlineData("<Button Content=\"OK\" AutomationProperties.Name=\"Close\" />", 2)]
    [InlineData("<TextBlock Text=\"{Binding Title}\" />", 0)]
    [InlineData("<Border Background=\"{DynamicResource Bg0}\" Tag=\"label\" />", 0)]
    public void Axaml_scanner(string xaml, int expected) => Assert.Equal(expected, UiLiteralScanner.ScanAxaml("x.axaml", xaml).Count);
}
