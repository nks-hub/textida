using System.Globalization;
using NksHub.Textida.Workbench.Clips;

namespace NksHub.Textida.Workbench.Tests;

/// <summary>P4.1.1 to P4.1.3: the template language, the typed fields of a clip form and the one place a variable gets its value.</summary>
public class VariablesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "textida-vars-" + Guid.NewGuid().ToString("N"));

    public VariablesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Default = "<todo>";
    private static readonly string TodoPath = Path.Combine(Path.GetTempPath(), "notes", "todo.final.txt");

    private static VariableContext Context(string? file = Default, IReadOnlyDictionary<string, string>? user = null, string selection = "SEL") => new()
    {
        FilePath = file == Default ? TodoPath : file, Selection = selection, Now = new DateTime(2026, 10, 27, 13, 5, 9), User = user ?? new Dictionary<string, string>(),
        DateTimeFormat = "dd. MMMM yyyy", Culture = CultureInfo.GetCultureInfo("cs-CZ"), UserName = "pat",
    };

    private static ExpandResult Expand(string template, VariableContext? context = null, Dictionary<string, string>? fields = null, string newLine = "\n") =>
        Variables.Expand(template, new VariableScope(context ?? Context(), fields), newLine);

    [Fact]
    public void The_built_in_variables_have_the_documented_values()
    {
        Assert.Equal("todo.final.txt|todo.final|.txt|2026-10-27|13:05:09|27. října 2026|SEL|pat",
            Expand("${file.name}|${file.base}|${file.ext}|${date}|${time}|${datetime}|${selection}|${user.name}").Text);
        Assert.Equal(Path.GetDirectoryName(TodoPath), Expand("${file.dir}").Text);
        Assert.Equal(TodoPath, Expand("${ file.path }").Text);
        Assert.All(VariableScope.BuiltinNames, name => Assert.True(new VariableScope(Context()).TryGet(name, out _), name));
    }

    [Fact]
    public void Dollar_marks_are_a_caret_an_escaped_dollar_or_stay_as_they_are_and_line_breaks_follow_the_document()
    {
        var r = Expand("a$|b$$c$d$|\r\nx\ny${selection}z", newLine: "\r\n");
        Assert.Equal("ab$c$d\r\nx\r\nySELz", r.Text);
        Assert.Equal([1, 6], r.Carets);
        Assert.True(r.Ok);
        Assert.Equal("price $", Expand("price $").Text);
        Assert.Equal("line1\nline2", Expand("line1\r\nline2", Context(selection: "")).Text);
        Assert.Equal("a\r\nb\r\nc", Expand("a\n${selection}\nc", Context(selection: "b"), newLine: "\r\n").Text);
        Assert.Equal("1\r\n2\r\n3", Expand("${selection}", Context(selection: "1\n2\r\n3"), newLine: "\r\n").Text);
    }

    [Fact]
    public void A_text_escaped_for_the_template_comes_back_unchanged()
    {
        foreach (var text in new[] { "$x ${y} $| $$", "plain", "a$", "$", "${" })
            Assert.Equal(text, Expand(Variables.Escape(text)).Text);
    }

    [Fact]
    public void A_name_without_a_value_and_a_broken_template_are_reported_and_not_guessed()
    {
        var r = Expand("${nobody} ${file.name} ${nobody}", Context(file: null));
        Assert.Equal(["nobody", "file.name"], r.Missing);
        Assert.False(r.Ok);
        Assert.NotNull(Expand("text ${oops").SyntaxError);
        Assert.NotNull(Expand("${bad name}").SyntaxError);
        Assert.NotNull(Expand("${}").SyntaxError);
        Assert.Null(Variables.CheckSyntax("fine ${anything} $| $$"));
        Assert.NotNull(Variables.CheckSyntax("${"));
        Assert.Equal(["a", "file.name"], Variables.NamesIn("${a} $${b} ${ file.name } ${a}"));
    }

    [Fact]
    public void Precedence_is_the_form_then_the_user_variables_then_the_built_in_ones()
    {
        var user = new Dictionary<string, string> { ["date"] = "user date", ["author"] = "Ann", ["who"] = "user who" };
        var fields = new Dictionary<string, string> { ["who"] = "form who", ["file.name"] = "form name" };
        var scope = new VariableScope(Context(user: user), fields);

        Assert.True(scope.TryGet("who", out var who, out var whoSource));
        Assert.Equal(("form who", VariableSource.Field), (who, whoSource));
        Assert.True(scope.TryGet("author", out var author, out var authorSource));
        Assert.Equal(("Ann", VariableSource.User), (author, authorSource));
        Assert.True(scope.TryGet("date", out var date, out var dateSource));
        Assert.Equal(("user date", VariableSource.User), (date, dateSource));
        Assert.True(scope.TryGet("time", out var time, out var timeSource));
        Assert.Equal(("13:05:09", VariableSource.Builtin), (time, timeSource));
        Assert.True(scope.TryGet("file.name", out var name, out _));
        Assert.Equal("form name", name);
        Assert.False(scope.TryGet("unknown", out _));
        Assert.Equal("form who|Ann|user date|13:05:09", Variables.Expand("${who}|${author}|${date}|${time}", scope).Text);
    }

    [Fact]
    public void One_name_gives_the_same_value_in_a_clip_in_a_recipe_text_and_in_a_tool_argument()
    {
        var user = new Dictionary<string, string> { ["project"] = "My Project" };
        var scope = new VariableScope(Context(user: user));
        var clip = Variables.Expand("name: ${project}, file: ${file.path}", scope).Text;
        var recipeReplacement = Variables.Expand("${project}", scope).Text;
        string[] toolArguments = ["--title", "${project}", "${file.path}"];
        var expanded = toolArguments.Select(a => Variables.Expand(a, scope).Text).ToArray();

        Assert.Equal("name: My Project, file: " + TodoPath, clip);
        Assert.Equal("My Project", recipeReplacement);
        Assert.Equal(["--title", "My Project", TodoPath], expanded);
        Assert.Equal(3, expanded.Length);
    }

    [Fact]
    public void A_value_is_computed_once_per_scope_so_a_guid_is_the_same_each_time_and_differs_between_scopes()
    {
        var scope = new VariableScope(Context());
        var text = Variables.Expand("${guid} ${guid}", scope).Text.Split(' ');
        Assert.Equal(text[0], text[1]);
        Assert.NotEqual(text[0], Variables.Expand("${guid}", new VariableScope(Context())).Text);
        Assert.True(Guid.TryParse(text[0], out _));
    }

    [Fact]
    public void A_date_format_that_does_not_work_leaves_datetime_without_a_value()
    {
        var context = new VariableContext { DateTimeFormat = "%" };
        Assert.False(new VariableScope(context).TryGet("datetime", out _));
    }

    // ---- fields ----

    private static ClipField Field(string type, bool required = true, params string[] choices) => new() { Name = "f", Type = type, Required = required, Choices = [.. choices] };

    [Theory]
    [InlineData("integer", "12", FieldProblem.None)]
    [InlineData("integer", "-12", FieldProblem.None)]
    [InlineData("integer", "1.2", FieldProblem.NotInteger)]
    [InlineData("integer", "1 2", FieldProblem.NotInteger)]
    [InlineData("integer", "99999999999999999999", FieldProblem.NotInteger)]
    [InlineData("number", "1.5", FieldProblem.None)]
    [InlineData("number", "-2e3", FieldProblem.None)]
    [InlineData("number", "1,5", FieldProblem.NotNumber)]
    [InlineData("number", "NaN", FieldProblem.NotNumber)]
    [InlineData("number", "Infinity", FieldProblem.NotNumber)]
    [InlineData("color", "#abc", FieldProblem.None)]
    [InlineData("color", "#AABBCC", FieldProblem.None)]
    [InlineData("color", "#AABBCCDD", FieldProblem.None)]
    [InlineData("color", "#AABBC", FieldProblem.NotColor)]
    [InlineData("color", "AABBCC", FieldProblem.NotColor)]
    [InlineData("color", "#GGG", FieldProblem.NotColor)]
    [InlineData("date", "2026-02-28", FieldProblem.None)]
    [InlineData("date", "2026-02-29", FieldProblem.NotDate)]
    [InlineData("date", "2026-2-28", FieldProblem.NotDate)]
    [InlineData("date", "28.2.2026", FieldProblem.NotDate)]
    [InlineData("text", "anything $ at all", FieldProblem.None)]
    [InlineData("text", "", FieldProblem.Empty)]
    public void A_typed_value_is_accepted_or_refused_for_its_type(string type, string value, FieldProblem expected) =>
        Assert.Equal(expected, ClipFields.Validate(Field(type), value));

    [Fact]
    public void Choice_and_file_fields_and_the_required_flag_work()
    {
        var choice = Field("choice", true, "a", "b");
        Assert.Equal(FieldProblem.None, ClipFields.Validate(choice, "b"));
        Assert.Equal(FieldProblem.NotChoice, ClipFields.Validate(choice, "B"));
        Assert.Equal(FieldProblem.None, ClipFields.Validate(Field("date", required: false), ""));
        Assert.Equal(FieldProblem.Empty, ClipFields.Validate(Field("date"), ""));

        var real = Path.Combine(_dir, "real.txt");
        File.WriteAllText(real, "x");
        Assert.Equal(FieldProblem.None, ClipFields.Validate(Field("file"), real));
        Assert.Equal(FieldProblem.NoFile, ClipFields.Validate(Field("file"), Path.Combine(_dir, "none.txt")));
        Assert.Equal(FieldProblem.NoFile, ClipFields.Validate(Field("file"), _dir));
        Assert.Equal(FieldProblem.NoFile, ClipFields.Validate(Field("file"), "bad\0name"));
    }

    [Fact]
    public void A_form_reports_every_refused_field_and_starts_with_expanded_defaults_or_the_first_choice()
    {
        var fields = new List<ClipField>
        {
            new() { Name = "when", Type = "date", Default = "${date}" },
            new() { Name = "kind", Type = "choice", Choices = ["x", "y"] },
            new() { Name = "size", Type = "integer" },
        };
        var defaults = ClipFields.Defaults(fields, new VariableScope(Context()));
        Assert.Equal("2026-10-27", defaults["when"]);
        Assert.Equal("x", defaults["kind"]);
        Assert.Equal("", defaults["size"]);

        var problems = ClipFields.Validate(fields, new Dictionary<string, string> { ["when"] = "soon", ["kind"] = "x" });
        Assert.Equal(FieldProblem.NotDate, problems["when"]);
        Assert.Equal(FieldProblem.Empty, problems["size"]);
        Assert.False(problems.ContainsKey("kind"));
    }

    [Fact]
    public void The_definitions_of_a_form_are_checked()
    {
        var bad = new List<ClipField>
        {
            new() { Name = "ok", Type = "text" }, new() { Name = "ok", Type = "text" }, new() { Name = "1x", Type = "text" },
            new() { Name = "t", Type = "banana" }, new() { Name = "c", Type = "choice" }, new() { Name = "d", Type = "text", Choices = ["a"] },
            new() { Name = "e", Type = "text", Default = "${" },
        };
        Assert.Equal(6, ClipFields.Check(bad).Count());
        Assert.Empty(ClipFields.Check([new() { Name = "ok", Type = "text" }, new() { Name = "c", Type = "choice", Choices = ["a"] }]));
    }

    // ---- the user's variables ----

    [Fact]
    public void The_user_variables_file_gives_names_and_texts_or_a_reason()
    {
        var path = Path.Combine(_dir, UserVariables.FileName);
        Assert.Empty(UserVariables.Load(path).Values);
        Assert.Null(UserVariables.Load(path).Problem);

        File.WriteAllText(path, "{ // mine\n \"author\": \"Ann\", \"team.name\": \"Q\", }");
        var (values, problem) = UserVariables.Load(path);
        Assert.Null(problem);
        Assert.Equal("Ann", values["author"]);
        Assert.Equal("Q", values["team.name"]);

        foreach (var broken in new[] { "{ nope", "[1]", "{\"a b\":\"x\"}", "{\"a\":1}" })
        {
            File.WriteAllText(path, broken);
            var result = UserVariables.Load(path);
            Assert.NotNull(result.Problem);
            Assert.Empty(result.Values);
        }
    }

    // ---- the variables of a project ----

    private string ProjectFile(string folder, string json)
    {
        string dir = Directory.CreateDirectory(Path.Combine(_dir, folder, ProjectVariables.FolderName)).FullName;
        string file = Path.Combine(dir, UserVariables.FileName);
        File.WriteAllText(file, json);
        return file;
    }

    [Fact]
    public void The_nearest_variables_file_above_the_document_is_the_one_of_its_project()
    {
        string outer = ProjectFile("outer", "{\"who\":\"outer\",\"only.outer\":\"o\"}");
        string inner = ProjectFile(Path.Combine("outer", "inner"), "{\"who\":\"inner\"}");
        string deep = Directory.CreateDirectory(Path.Combine(_dir, "outer", "inner", "src", "deep")).FullName;
        string beside = Directory.CreateDirectory(Path.Combine(_dir, "outer", "other")).FullName;

        Assert.Equal(inner, ProjectVariables.FindFile(deep));
        Assert.Equal(outer, ProjectVariables.FindFile(beside));
        Assert.Null(ProjectVariables.FindFile(Path.Combine(_dir, "nowhere")));
        Assert.Null(ProjectVariables.FindFile(null));

        var (values, problem, file) = ProjectVariables.Load(Path.Combine(deep, "a.txt"));
        Assert.Null(problem);
        Assert.Equal(inner, file);
        Assert.Equal([("who", "inner")], values.Select(p => (p.Key, p.Value))); // the file of the outer folder is not merged in
    }

    [Fact]
    public void A_document_without_a_path_or_a_project_has_no_project_variables_and_a_damaged_file_names_itself()
    {
        Assert.Empty(ProjectVariables.Load(null).Values);
        Assert.Empty(ProjectVariables.Load(Path.Combine(_dir, "free", "a.txt")).Values);

        string file = ProjectFile("broken", "{ nope");
        var (values, problem, found) = ProjectVariables.Load(Path.Combine(_dir, "broken", "a.txt"));
        Assert.Empty(values);
        Assert.StartsWith(file + ": ", problem);
        Assert.Equal(file, found);
    }

    [Fact]
    public void The_file_of_the_user_is_not_read_again_as_the_one_of_a_project()
    {
        string file = ProjectFile("profile-in-project", "{\"a\":\"1\"}");
        var (values, problem, found) = ProjectVariables.Load(Path.Combine(_dir, "profile-in-project", "a.txt"), except: file);
        Assert.Empty(values);
        Assert.Null(problem);
        Assert.Null(found);
    }

    [Fact]
    public void Precedence_puts_the_variables_of_the_project_between_the_form_and_the_user_variables()
    {
        var context = new VariableContext
        {
            FilePath = TodoPath, Now = new DateTime(2026, 10, 27, 13, 5, 9),
            User = new Dictionary<string, string> { ["who"] = "user", ["only.user"] = "u", ["date"] = "user date" },
            Project = new Dictionary<string, string> { ["who"] = "project", ["only.project"] = "p", ["date"] = "project date" },
        };
        var scope = new VariableScope(context, new Dictionary<string, string> { ["date"] = "form date" });

        Assert.True(scope.TryGet("date", out var date, out var dateSource));
        Assert.Equal(("form date", VariableSource.Field), (date, dateSource));
        Assert.True(scope.TryGet("who", out var who, out var whoSource));
        Assert.Equal(("project", VariableSource.Project), (who, whoSource));
        Assert.True(scope.TryGet("only.user", out var user, out var userSource));
        Assert.Equal(("u", VariableSource.User), (user, userSource));
        Assert.True(scope.TryGet("only.project", out _, out var projectSource));
        Assert.Equal(VariableSource.Project, projectSource);
        Assert.True(scope.TryGet("time", out _, out var builtinSource));
        Assert.Equal(VariableSource.Builtin, builtinSource);

        // another file or another selection keeps the project's variables
        Assert.Equal("project", Variables.Expand("${who}", new VariableScope(context.WithFile(null))).Text);
        Assert.Equal("project", Variables.Expand("${who}", new VariableScope(context.WithSelection("x"))).Text);
    }

    [Fact]
    public void A_snippet_with_nested_stops_and_a_variable_transform_expands_and_one_over_the_limit_is_a_syntax_error()
    {
        var r = Expand(@"${1:a ${2:b}} ${file.name/(.*)\.(.*)/$2/} $0");
        Assert.Null(r.SyntaxError);
        Assert.Equal("a b txt ", r.Text);
        Assert.Equal([1, 2], r.Snippet!.Stops.Select(s => s.Number));

        var big = Variables.Expand("${1:$2$2$2$2}${2:" + new string('x', 30) + "}", new VariableScope(Context()), "\n", new NksHub.Textida.Core.Snippets.SnippetLimits(8, 100, TimeSpan.FromSeconds(1)));
        Assert.NotNull(big.SyntaxError);
        Assert.False(big.Ok);
        Assert.Equal("", big.Text);
    }
}
