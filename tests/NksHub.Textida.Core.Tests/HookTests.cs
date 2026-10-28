using System.Text.Json;
using NksHub.Textida.Core.Agents.Hooks;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Tests;

/// <summary>P8b.3.1: the hook catalogue, the table of results by exit code, the check against the schema, the dry run and the edits of the text of a hooks file. Nothing here runs a command.</summary>
public class HookTests
{
    private static readonly HookLimits Limits = new(64, TimeSpan.FromMilliseconds(200));

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static HookEventInfo Event(HookProduct product, string name) => HookCatalog.For(product).Find(name) ?? throw new InvalidOperationException(name);

    // ---- the catalogue ----

    [Fact]
    public void The_catalogues_hold_the_events_of_the_documentation_and_a_sample_input_for_each()
    {
        var claude = HookCatalog.For(HookProduct.ClaudeCode);
        var codex = HookCatalog.For(HookProduct.Codex);
        Assert.Equal(33, claude.Events.Count);
        Assert.Equal(12, codex.Events.Count);
        foreach (var catalog in new[] { claude, codex })
        {
            Assert.StartsWith("https://", catalog.Source);
            Assert.Equal(catalog.Events.Count, catalog.Events.Select(e => e.Name).Distinct().Count());
            foreach (var e in catalog.Events)
            {
                Assert.Equal(e.Name, e.Sample.GetProperty("hook_event_name").GetString());
                if (e.MatcherField is { } field) Assert.True(e.Sample.TryGetProperty(field, out _), $"{e.Name}: the sample has no {field}");
            }
        }
        Assert.Equal(["command", "http", "mcp_tool", "prompt", "agent"], claude.HandlerTypes.Select(t => t.Name));
        Assert.Equal(["command", "mcp_tool"], codex.HandlerTypes.Select(t => t.Name));
    }

    [Fact]
    public void Every_event_of_the_catalogue_is_in_the_schema_and_the_other_way_round()
    {
        foreach (var product in new[] { HookProduct.ClaudeCode, HookProduct.Codex })
        {
            var catalog = HookCatalog.For(product);
            using var schema = JsonDocument.Parse(catalog.SchemaText);
            var names = schema.RootElement.GetProperty("definitions").GetProperty("hooks").GetProperty("propertyNames").GetProperty("enum").EnumerateArray().Select(e => e.GetString()!);
            Assert.Equal(catalog.Events.Select(e => e.Name).Order(), names.Order());
        }
    }

    // ---- the table of results by exit code ----

    // Written from the table "Exit code 2 behavior per event" of https://code.claude.com/docs/en/hooks.md, not from the catalogue.
    public static TheoryData<string, HookExitEffect> ClaudeExitTwo => new()
    {
        { "PreToolUse", HookExitEffect.Blocks }, { "PermissionRequest", HookExitEffect.NotHonored }, { "UserPromptSubmit", HookExitEffect.Blocks },
        { "UserPromptExpansion", HookExitEffect.Blocks }, { "Stop", HookExitEffect.KeepsWorking }, { "SubagentStop", HookExitEffect.KeepsWorking },
        { "TeammateIdle", HookExitEffect.KeepsWorking }, { "TaskCreated", HookExitEffect.Blocks }, { "TaskCompleted", HookExitEffect.Blocks },
        { "ConfigChange", HookExitEffect.Blocks }, { "StopFailure", HookExitEffect.Ignored }, { "PostToolUse", HookExitEffect.StderrToModel },
        { "PostToolUseFailure", HookExitEffect.StderrToModel }, { "PostToolBatch", HookExitEffect.Blocks }, { "PermissionDenied", HookExitEffect.Ignored },
        { "Notification", HookExitEffect.Ignored }, { "SubagentStart", HookExitEffect.StderrToUser }, { "SessionStart", HookExitEffect.StderrToUser },
        { "Setup", HookExitEffect.Ignored }, { "SessionEnd", HookExitEffect.StderrToUser }, { "CwdChanged", HookExitEffect.StderrToUser },
        { "DirectoryAdded", HookExitEffect.DebugLog }, { "FileChanged", HookExitEffect.StderrToUser }, { "PreCompact", HookExitEffect.Blocks },
        { "PostCompact", HookExitEffect.StderrToUser }, { "PreModelSwitch", HookExitEffect.Blocks }, { "PostModelSwitch", HookExitEffect.StderrToUser },
        { "Elicitation", HookExitEffect.Blocks }, { "ElicitationResult", HookExitEffect.Blocks }, { "WorktreeCreate", HookExitEffect.Blocks },
        { "WorktreeRemove", HookExitEffect.Blocks }, { "InstructionsLoaded", HookExitEffect.Ignored }, { "MessageDisplay", HookExitEffect.DisplayOnly },
    };

    [Theory, MemberData(nameof(ClaudeExitTwo))]
    public void Claude_Code_exit_2_does_what_the_table_of_the_documentation_says(string name, HookExitEffect expected)
    {
        Assert.Equal(expected, HookExit.Interpret(Event(HookProduct.ClaudeCode, name), 2).Effect);
    }

    [Fact]
    public void The_table_of_Claude_Code_covers_every_event_of_the_catalogue()
    {
        var table = ClaudeExitTwo.Select(d => (string)d[0]).Order().ToList();
        Assert.Equal(HookCatalog.For(HookProduct.ClaudeCode).Events.Select(e => e.Name).Order(), table);
    }

    [Fact]
    public void Claude_Code_exit_0_is_success_and_exit_1_does_not_block_except_where_the_event_has_its_own_rule()
    {
        foreach (var e in HookCatalog.For(HookProduct.ClaudeCode).Events)
        {
            Assert.Equal(HookExitEffect.Success, HookExit.Interpret(e, 0).Effect);
            var one = HookExit.Interpret(e, 1);
            Assert.Equal(e.Name is "WorktreeCreate" or "WorktreeRemove", one.IsBlocking);
            Assert.Equal(1, one.ExitCode);
        }
        var preToolUse = Event(HookProduct.ClaudeCode, "PreToolUse");
        Assert.Equal(HookExitEffect.NonBlocking, HookExit.Interpret(preToolUse, 1).Effect);
        Assert.True(HookExit.Interpret(preToolUse, 2).IsBlocking);
        Assert.Equal(HookExitEffect.NonBlocking, HookExit.Interpret(preToolUse, 137).Effect);
    }

    [Fact]
    public void A_failing_hook_that_is_set_to_block_acts_like_exit_2_except_where_the_documentation_excludes_it()
    {
        var block = new HookHandlerTraits(OnFailure: "block");
        Assert.True(HookExit.Interpret(Event(HookProduct.ClaudeCode, "PreToolUse"), 1, block).IsBlocking);
        Assert.True(HookExit.Interpret(Event(HookProduct.ClaudeCode, "UserPromptSubmit"), 1, block).IsBlocking);
        Assert.Equal(HookExitEffect.Denies, HookExit.Interpret(Event(HookProduct.ClaudeCode, "PermissionRequest"), 1, block).Effect);
        Assert.Equal(HookExitEffect.StderrToModel, HookExit.Interpret(Event(HookProduct.ClaudeCode, "PostToolUse"), 1, block).Effect);
        foreach (var name in new[] { "Stop", "SubagentStop", "TaskCompleted", "TeammateIdle" })
            Assert.Equal(HookExitEffect.NonBlocking, HookExit.Interpret(Event(HookProduct.ClaudeCode, name), 1, block).Effect);
        Assert.Equal(HookExitEffect.NonBlocking, HookExit.Interpret(Event(HookProduct.ClaudeCode, "PreToolUse"), 1, new HookHandlerTraits(OnFailure: "continue")).Effect);
    }

    [Fact]
    public void A_hook_in_the_background_cannot_block_unless_it_wakes_the_agent_with_exit_2()
    {
        var preToolUse = Event(HookProduct.ClaudeCode, "PreToolUse");
        Assert.Equal(HookExitEffect.Background, HookExit.Interpret(preToolUse, 2, new HookHandlerTraits(Async: true)).Effect);
        Assert.Equal(HookExitEffect.Background, HookExit.Interpret(preToolUse, 1, new HookHandlerTraits(Async: true)).Effect);
        Assert.Equal(HookExitEffect.StderrToModel, HookExit.Interpret(preToolUse, 2, new HookHandlerTraits(Async: true, AsyncRewake: true)).Effect);
    }

    // Written from the sections of https://developers.openai.com/codex/hooks; what it does not say is Undocumented.
    public static TheoryData<string, HookExitEffect> CodexExitTwo => new()
    {
        { "PreToolUse", HookExitEffect.Blocks }, { "PostToolUse", HookExitEffect.StderrToModel }, { "UserPromptSubmit", HookExitEffect.Blocks },
        { "SubagentStop", HookExitEffect.KeepsWorking }, { "Stop", HookExitEffect.KeepsWorking }, { "Interrupt", HookExitEffect.Ignored },
        { "SessionStart", HookExitEffect.Undocumented }, { "SessionEnd", HookExitEffect.Undocumented }, { "SubagentStart", HookExitEffect.Undocumented },
        { "PermissionRequest", HookExitEffect.Undocumented }, { "PreCompact", HookExitEffect.Undocumented }, { "PostCompact", HookExitEffect.Undocumented },
    };

    [Theory, MemberData(nameof(CodexExitTwo))]
    public void Codex_exit_2_does_what_its_documentation_says_and_nothing_is_assumed_beyond_it(string name, HookExitEffect expected)
    {
        var e = Event(HookProduct.Codex, name);
        Assert.Equal(expected, HookExit.Interpret(e, 2).Effect);
        Assert.Equal(HookExitEffect.Success, HookExit.Interpret(e, 0).Effect);
    }

    [Fact]
    public void The_table_of_Codex_covers_every_event_of_the_catalogue_and_is_not_the_table_of_Claude_Code()
    {
        Assert.Equal(HookCatalog.For(HookProduct.Codex).Events.Select(e => e.Name).Order(), CodexExitTwo.Select(d => (string)d[0]).Order());
        Assert.NotEqual(HookExit.Interpret(Event(HookProduct.ClaudeCode, "SessionStart"), 2).Effect, HookExit.Interpret(Event(HookProduct.Codex, "SessionStart"), 2).Effect);
    }

    // ---- matchers ----

    [Fact]
    public void A_matcher_of_Claude_Code_is_an_exact_value_a_list_or_an_unanchored_regular_expression()
    {
        var tool = Event(HookProduct.ClaudeCode, "PreToolUse");
        bool? Match(string? matcher, string value) => HookMatcher.Matches(tool, matcher, value, Limits.RegexTimeout);
        Assert.True(Match("*", "Anything"));
        Assert.True(Match("", "Anything"));
        Assert.True(Match(null, "Anything"));
        Assert.True(Match("Bash", "Bash"));
        Assert.False(Match("Bash", "BashOutput"));
        Assert.True(Match("Edit|Write", "Write"));
        Assert.True(Match("Edit, Write", "Write"));
        Assert.False(Match("Edit|Write", "NotebookEdit"));
        Assert.True(Match("Edit.*", "NotebookEdit")); // a regular expression is not anchored
        Assert.False(Match("^Edit$", "NotebookEdit"));
        Assert.False(Match("mcp__memory", "mcp__memory__create_entities")); // only exact characters: compared as a whole
        Assert.True(Match("mcp__memory__.*", "mcp__memory__create_entities"));
        Assert.Equal(HookMatchKind.Exact, HookMatcher.Classify(tool, "Edit|Write"));
        Assert.Equal(HookMatchKind.Regex, HookMatcher.Classify(tool, "^Notebook"));
        Assert.Null(Match("(", "x"));
        Assert.True(HookMatcher.IsBrokenPattern(tool, "(", Limits.RegexTimeout));
        Assert.False(HookMatcher.IsBrokenPattern(tool, "Edit|Write", Limits.RegexTimeout));
    }

    [Fact]
    public void Two_events_take_a_narrower_set_of_exact_characters_and_an_event_without_a_matcher_always_fires()
    {
        var failure = Event(HookProduct.ClaudeCode, "StopFailure");
        Assert.Equal(HookMatchKind.Regex, HookMatcher.Classify(failure, "rate-limit"));
        Assert.Equal(HookMatchKind.Exact, HookMatcher.Classify(failure, "rate_limit|overloaded"));
        Assert.Equal(HookMatchKind.Exact, HookMatcher.Classify(Event(HookProduct.ClaudeCode, "PreToolUse"), "mcp-tool"));
        var prompt = Event(HookProduct.ClaudeCode, "UserPromptSubmit");
        Assert.Null(prompt.MatcherField);
        Assert.True(HookMatcher.Matches(prompt, "NeverMatches", "anything", Limits.RegexTimeout));
    }

    [Fact]
    public void A_matcher_of_Codex_is_a_regular_expression()
    {
        var tool = Event(HookProduct.Codex, "PreToolUse");
        Assert.Equal(HookMatchKind.Regex, HookMatcher.Classify(tool, "Bash"));
        Assert.True(HookMatcher.Matches(tool, "^Bash$", "Bash", Limits.RegexTimeout));
        Assert.False(HookMatcher.Matches(tool, "^Bash$", "Bash2", Limits.RegexTimeout));
        Assert.True(HookMatcher.Matches(tool, "*", "Bash2", Limits.RegexTimeout));
    }

    // ---- the dry run ----

    [Fact]
    public void The_dry_run_shows_the_input_the_hook_would_get_and_whether_the_matcher_fires_without_starting_anything()
    {
        var catalog = HookCatalog.For(HookProduct.ClaudeCode);
        var run = HookDryRunner.Run(catalog, "PreToolUse", "Edit|Write", "Write", null, Limits.RegexTimeout);
        Assert.True(run.Matches);
        Assert.Equal("tool_name", run.MatcherField);
        using var input = JsonDocument.Parse(run.StdinJson);
        Assert.Equal("PreToolUse", input.RootElement.GetProperty("hook_event_name").GetString());
        Assert.Equal("Write", input.RootElement.GetProperty("tool_name").GetString());
        Assert.True(input.RootElement.TryGetProperty("tool_input", out _));
        Assert.Equal([0, 1, 2], run.Outcomes.Select(o => o.ExitCode));
        Assert.Equal([HookExitEffect.Success, HookExitEffect.NonBlocking, HookExitEffect.Blocks], run.Outcomes.Select(o => o.Effect));

        var other = HookDryRunner.Run(catalog, "PreToolUse", "Edit|Write", "Read", null, Limits.RegexTimeout);
        Assert.False(other.Matches);
        Assert.Contains("\"tool_name\": \"Read\"", other.StdinJson);
    }

    [Fact]
    public void The_dry_run_takes_the_value_of_the_event_when_none_is_named_and_knows_an_unknown_event()
    {
        var claude = HookCatalog.For(HookProduct.ClaudeCode);
        var start = HookDryRunner.Run(claude, "SessionStart", "resume", null, null, Limits.RegexTimeout);
        Assert.Equal("startup", start.TestedValue);
        Assert.False(start.Matches);
        Assert.Equal(HookStdout.ContextText, start.Stdout);

        var changed = HookDryRunner.Run(claude, "FileChanged", ".envrc", ".envrc", null, Limits.RegexTimeout);
        Assert.Contains(".envrc", changed.StdinJson);

        var missing = HookDryRunner.Run(claude, "NoSuchEvent", "", null, null, Limits.RegexTimeout);
        Assert.Null(missing.Event);
        Assert.Empty(missing.Outcomes);

        var codex = HookDryRunner.Run(HookCatalog.For(HookProduct.Codex), "Stop", "", null, null, Limits.RegexTimeout);
        Assert.Equal(HookStdout.JsonRequired, codex.Stdout);
        Assert.Contains("\"hook_event_name\": \"Stop\"", codex.StdinJson);
    }

    // ---- the check of a file ----

    private const string Valid = """
        {
          "hooks": {
            "PreToolUse": [
              {
                "matcher": "Bash",
                "hooks": [
                  { "type": "command", "command": "echo hi", "timeout": 30 }
                ]
              }
            ]
          }
        }
        """;

    private static List<HookIssue> Check(string text, HookProduct product = HookProduct.ClaudeCode) => [.. HookValidator.Validate(text, product, Limits)];

    [Fact]
    public void A_file_that_follows_the_documentation_has_no_findings()
    {
        Assert.Empty(Check(Valid));
        Assert.Empty(Check("{}"));
        Assert.Empty(Check("{\"model\": \"x\", \"hooks\": {}}"));
    }

    [Fact]
    public void A_comment_in_the_file_is_a_syntax_error_because_the_settings_are_strict_JSON()
    {
        var issue = Assert.Single(Check("{ // note\n \"hooks\": {} }"));
        Assert.Equal(("syntax", HookSeverity.Error), (issue.Code, issue.Severity));
    }

    [Fact]
    public void The_schema_reports_an_unknown_event_a_missing_command_and_a_wrong_type_as_errors()
    {
        var unknown = Check("{\"hooks\": {\"PreToolUze\": []}}");
        Assert.Contains(unknown, i => i.Code == "propertyNames" && i.Severity == HookSeverity.Error && i.Args.SequenceEqual(["PreToolUze"]));

        var noCommand = Check("{\"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"command\"}]}]}}");
        Assert.Contains(noCommand, i => i.Code == "required" && i.Args.SequenceEqual(["command"]) && i.Severity == HookSeverity.Error);

        var wrong = Check("{\"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"command\", \"command\": \"x\", \"timeout\": \"30\"}]}]}}");
        Assert.Contains(wrong, i => i.Code == "type" && i.Pointer == "/hooks/Stop/0/hooks/0/timeout");

        var badType = Check("{\"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"shell\", \"command\": \"x\"}]}]}}");
        Assert.Contains(badType, i => i.Code == "enum");

        var noHooks = Check("{\"hooks\": {\"Stop\": [{\"matcher\": \"x\"}]}}");
        Assert.Contains(noHooks, i => i.Code == "required" && i.Args.SequenceEqual(["hooks"]));
    }

    [Fact]
    public void A_field_the_documentation_does_not_list_is_a_warning_not_an_error()
    {
        var issues = Check("{\"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"command\", \"command\": \"x\", \"retries\": 3}]}]}}");
        var warning = Assert.Single(issues);
        Assert.Equal(("additionalProperties", HookSeverity.Warning), (warning.Code, warning.Severity));
        Assert.Equal(["retries"], warning.Args);
    }

    [Fact]
    public void Settings_the_documentation_says_have_no_effect_are_warnings()
    {
        var issues = Check(Lines(
            "{\"hooks\": {",
            " \"UserPromptSubmit\": [{\"matcher\": \"x\", \"hooks\": [{\"type\": \"command\", \"command\": \"a\"}]}],",
            " \"Stop\": [{\"hooks\": [{\"type\": \"command\", \"command\": \"a\", \"if\": \"Bash(git *)\"}]}],",
            " \"PostToolUse\": [{\"matcher\": \"(\", \"hooks\": [{\"type\": \"http\", \"url\": \"http://127.0.0.1/x\", \"async\": true}, {\"type\": \"command\", \"command\": \"a\", \"args\": [], \"shell\": \"bash\"}]}],",
            " \"SessionEnd\": [{\"hooks\": []}]",
            "}}"));
        Assert.All(issues, i => Assert.Equal(HookSeverity.Warning, i.Severity));
        Assert.Equal(["hook.emptyGroup", "hook.fieldNotForType", "hook.ifNeverRuns", "hook.matcherIgnored", "hook.matcherPattern", "hook.shellIgnored"], issues.Select(i => i.Code).Distinct().Order());
    }

    [Fact]
    public void Codex_hooks_are_checked_against_its_own_events_and_handlers()
    {
        Assert.Empty(Check("{\"hooks\": {\"PreToolUse\": [{\"matcher\": \"^Bash$\", \"hooks\": [{\"type\": \"command\", \"command\": \"x\", \"timeout\": 30, \"statusMessage\": \"s\"}]}]}}", HookProduct.Codex));
        var unknown = Check("{\"hooks\": {\"PreModelSwitch\": []}}", HookProduct.Codex); // an event of Claude Code
        Assert.Contains(unknown, i => i.Code == "propertyNames");
        var skipped = Check("{\"hooks\": {\"Stop\": [{\"hooks\": [{\"type\": \"prompt\", \"prompt\": \"p\"}]}]}}", HookProduct.Codex);
        Assert.Equal(["hook.skipped"], skipped.Select(i => i.Code));
        var slow = Check("{\"hooks\": {\"SessionEnd\": [{\"hooks\": [{\"type\": \"command\", \"command\": \"x\", \"timeout\": 5}]}]}}", HookProduct.Codex);
        Assert.Equal(["hook.timeoutLimit"], slow.Select(i => i.Code));
        var mcp = Check("{\"hooks\": {\"PostToolUse\": [{\"hooks\": [{\"type\": \"mcp_tool\", \"server\": \"s\", \"tool\": \"t\", \"input\": {\"a\": \"${tool_input.file_path}\"}}]}]}}", HookProduct.Codex);
        Assert.Empty(mcp);
    }

    [Fact]
    public void The_findings_know_their_place_in_the_text()
    {
        string text = "{\"hooks\": {\"Nope\": []}}";
        var issue = Assert.Single(Check(text));
        Assert.Equal("Nope", text[issue.Start..issue.End].Trim('"'));
    }

    // ---- the tree ----

    [Fact]
    public void The_tree_lists_events_matcher_groups_and_handlers()
    {
        Assert.True(HookDocument.TryParse(Valid, HookProduct.ClaudeCode, Limits, out var doc, out _));
        var ev = Assert.Single(doc!.Events);
        Assert.Equal("PreToolUse", ev.Name);
        Assert.NotNull(ev.Info);
        var group = Assert.Single(ev.Groups);
        Assert.Equal("Bash", group.Matcher);
        var handler = Assert.Single(group.Handlers);
        Assert.Equal("command", handler.Type);
        Assert.Equal("command: echo hi", handler.Summary);
        Assert.Equal("30", handler.Raw("timeout", Valid));
        Assert.NotNull(doc.Find(new HookRef("PreToolUse", 0, 0)));
        Assert.Null(doc.Find(new HookRef("PreToolUse", 0, 1)));
        Assert.False(HookDocument.TryParse("{", HookProduct.ClaudeCode, Limits, out _, out var error));
        Assert.NotNull(error);
    }

    // ---- edits ----

    private static HookDraft Command(string ev, string matcher, string command, string? timeout = null)
    {
        var draft = new HookDraft { Event = ev, Matcher = matcher };
        draft.Fields["type"] = HookValue.String("command");
        draft.Fields["command"] = HookValue.String(command);
        draft.Fields["timeout"] = timeout is null ? null : HookValue.Number(timeout);
        return draft;
    }

    private static string Added(string text, HookDraft draft, HookProduct product = HookProduct.ClaudeCode)
    {
        var result = HookEdits.Upsert(text, product, null, draft, Limits);
        Assert.True(result.Ok, result.Error.ToString());
        return result.Text;
    }

    [Fact]
    public void A_hook_added_to_an_empty_file_gets_the_layout_of_the_documentation_and_passes_the_check()
    {
        string text = Added("{}", Command("PreToolUse", "Bash", "echo hi"));
        Assert.Equal(Lines(
            "{",
            "  \"hooks\": {",
            "    \"PreToolUse\": [",
            "      {",
            "        \"matcher\": \"Bash\",",
            "        \"hooks\": [",
            "          {",
            "            \"type\": \"command\",",
            "            \"command\": \"echo hi\"",
            "          }",
            "        ]",
            "      }",
            "    ]",
            "  }",
            "}"), text);
        Assert.Empty(Check(text));
    }

    [Fact]
    public void A_second_hook_joins_the_group_of_the_same_matcher_and_a_new_matcher_makes_a_group()
    {
        string first = Added("{}", Command("PreToolUse", "Bash", "echo one"));
        string second = Added(first, Command("PreToolUse", "Bash", "echo two", "10"));
        string third = Added(second, Command("PreToolUse", "Edit|Write", "echo three"));
        string fourth = Added(third, Command("Stop", "", "echo four"));
        Assert.Empty(Check(fourth));
        Assert.True(HookDocument.TryParse(fourth, HookProduct.ClaudeCode, Limits, out var doc, out _));
        var tool = doc!.Events.Single(e => e.Name == "PreToolUse");
        Assert.Equal(["Bash", "Edit|Write"], tool.Groups.Select(g => g.Matcher));
        Assert.Equal(["command: echo one", "command: echo two"], tool.Groups[0].Handlers.Select(h => h.Summary));
        Assert.Equal("10", tool.Groups[0].Handlers[1].Raw("timeout", fourth));
        Assert.Contains("\"Stop\": [", fourth);
        Assert.DoesNotContain("matcher\": \"\"", fourth);
    }

    [Fact]
    public void An_edit_changes_only_the_place_it_concerns()
    {
        string text = "{\n    \"model\": \"x\",\n    \"hooks\": {\n        \"Stop\": [\n            {\n                \"hooks\": [\n                    { \"type\": \"command\", \"command\": \"old\", \"retries\": 3 }\n                ]\n            }\n        ]\n    }\n}\n";
        var draft = Command("Stop", "", "new");
        draft.Fields["timeout"] = HookValue.Number("20");
        var result = HookEdits.Upsert(text, HookProduct.ClaudeCode, new HookRef("Stop", 0, 0), draft, Limits);
        Assert.True(result.Ok);
        string expected = text.Replace("\"command\": \"old\", \"retries\": 3", "\"command\": \"new\", \"retries\": 3, \"timeout\": 20", StringComparison.Ordinal);
        Assert.Equal(expected, result.Text);

        var removed = new HookDraft { Event = "Stop" };
        removed.Fields["timeout"] = null;
        var again = HookEdits.Upsert(result.Text, HookProduct.ClaudeCode, new HookRef("Stop", 0, 0), removed, Limits);
        Assert.Equal(text.Replace("\"old\"", "\"new\"", StringComparison.Ordinal), again.Text);
    }

    [Fact]
    public void The_indent_and_the_line_break_of_the_file_are_kept()
    {
        string text = "{\r\n\t\"hooks\": {\r\n\t}\r\n}\r\n";
        string result = Added(text, Command("Stop", "", "x"));
        Assert.DoesNotContain("\n  ", result);
        Assert.Contains("\r\n\t\t\"Stop\": [\r\n\t\t\t{", result);
        Assert.DoesNotContain("\n", result.Replace("\r\n", ""));
        Assert.Empty(Check(result));
    }

    [Fact]
    public void Removing_the_last_handler_removes_its_group_and_its_event_and_nothing_else()
    {
        string both = Added(Added("{\"model\": \"x\"}", Command("Stop", "", "one")), Command("PreToolUse", "Bash", "two"));
        var gone = HookEdits.Remove(both, HookProduct.ClaudeCode, new HookRef("Stop", 0, 0), Limits);
        Assert.True(gone.Ok);
        Assert.DoesNotContain("Stop", gone.Text);
        Assert.Contains("\"model\": \"x\"", gone.Text);
        Assert.Empty(Check(gone.Text));
        Assert.True(HookDocument.TryParse(gone.Text, HookProduct.ClaudeCode, Limits, out var doc, out _));
        Assert.Equal(["PreToolUse"], doc!.Events.Select(e => e.Name));

        string many = Added(Added(both, Command("PreToolUse", "Bash", "three")), Command("PreToolUse", "Read", "four"));
        var one = HookEdits.Remove(many, HookProduct.ClaudeCode, new HookRef("PreToolUse", 0, 0), Limits);
        Assert.True(HookDocument.TryParse(one.Text, HookProduct.ClaudeCode, Limits, out var after, out _));
        var tool = after!.Events.Single(e => e.Name == "PreToolUse");
        Assert.Equal(["Bash", "Read"], tool.Groups.Select(g => g.Matcher));
        Assert.Equal(["command: three"], tool.Groups[0].Handlers.Select(h => h.Summary));

        var all = HookEdits.Remove(Added("{}", Command("Stop", "", "x")), HookProduct.ClaudeCode, new HookRef("Stop", 0, 0), Limits);
        Assert.Empty(Check(all.Text));
        Assert.True(HookDocument.TryParse(all.Text, HookProduct.ClaudeCode, Limits, out var empty, out _));
        Assert.Empty(empty!.Events);
    }

    [Fact]
    public void A_hook_moved_to_another_event_keeps_the_fields_the_form_does_not_edit()
    {
        string text = Lines(
            "{",
            "  \"hooks\": {",
            "    \"PreToolUse\": [",
            "      { \"matcher\": \"Bash\", \"hooks\": [ { \"type\": \"command\", \"command\": \"a\", \"statusMessage\": \"Checking\", \"timeout\": 5 } ] }",
            "    ]",
            "  }",
            "}");
        var draft = new HookDraft { Event = "PostToolUse", Matcher = "Bash" };
        draft.Fields["command"] = HookValue.String("b");
        var result = HookEdits.Upsert(text, HookProduct.ClaudeCode, new HookRef("PreToolUse", 0, 0), draft, Limits);
        Assert.True(result.Ok);
        Assert.Empty(Check(result.Text));
        Assert.True(HookDocument.TryParse(result.Text, HookProduct.ClaudeCode, Limits, out var doc, out _));
        Assert.Equal(["PostToolUse"], doc!.Events.Select(e => e.Name));
        var handler = doc.Events[0].Groups[0].Handlers[0];
        Assert.Equal("command", handler.Type);
        Assert.Equal("b", handler.Text("command"));
        Assert.Equal("Checking", handler.Text("statusMessage"));
        Assert.Equal("5", handler.Raw("timeout", result.Text));
    }

    [Fact]
    public void An_edit_that_does_not_fit_the_file_changes_nothing_and_says_why()
    {
        Assert.Equal(HookEditError.Syntax, HookEdits.Upsert("{", HookProduct.ClaudeCode, null, Command("Stop", "", "x"), Limits).Error);
        Assert.Equal(HookEditError.NoEvent, HookEdits.Upsert("{}", HookProduct.ClaudeCode, null, Command("", "", "x"), Limits).Error);
        Assert.Equal(HookEditError.NotObject, HookEdits.Upsert("[]", HookProduct.ClaudeCode, null, Command("Stop", "", "x"), Limits).Error);
        Assert.Equal(HookEditError.HooksNotObject, HookEdits.Upsert("{\"hooks\": 1}", HookProduct.ClaudeCode, null, Command("Stop", "", "x"), Limits).Error);
        Assert.Equal(HookEditError.EventNotArray, HookEdits.Upsert("{\"hooks\": {\"Stop\": {}}}", HookProduct.ClaudeCode, null, Command("Stop", "", "x"), Limits).Error);
        Assert.Equal(HookEditError.NoSuchHook, HookEdits.Upsert("{}", HookProduct.ClaudeCode, new HookRef("Stop", 0, 0), Command("Stop", "", "x"), Limits).Error);
        Assert.Equal(HookEditError.NoSuchHook, HookEdits.Remove("{}", HookProduct.ClaudeCode, new HookRef("Stop", 0, 0), Limits).Error);
        var failed = HookEdits.Upsert("{\"hooks\": 1}", HookProduct.ClaudeCode, null, Command("Stop", "", "x"), Limits);
        Assert.Equal("{\"hooks\": 1}", failed.Text);
    }

    [Fact]
    public void A_hook_written_for_Codex_is_checked_by_the_Codex_schema()
    {
        var draft = new HookDraft { Event = "PreToolUse", Matcher = "^Bash$" };
        draft.Fields["type"] = HookValue.String("command");
        draft.Fields["command"] = HookValue.String("python3 check.py");
        draft.Fields["timeout"] = HookValue.Number("30");
        string text = Added("{\"description\": \"d\"}", draft, HookProduct.Codex);
        Assert.Empty(Check(text, HookProduct.Codex));
        Assert.Contains("\"description\": \"d\"", text);
    }

    // ---- splices ----

    [Fact]
    public void Splices_keep_every_other_byte_and_the_commas_right()
    {
        string text = "{ \"a\": 1, \"b\": [1, 2, 3], \"c\": { \"d\": 4 } }";
        Assert.True(JsonDataReader.TryParse(text, false, 16, out var root, out _));
        Assert.Equal("{ \"b\": [1, 2, 3], \"c\": { \"d\": 4 } }", JsonSplice.RemoveMember(root!, 0).ApplyTo(text));
        Assert.Equal("{ \"a\": 1, \"c\": { \"d\": 4 } }", JsonSplice.RemoveMember(root!, 1).ApplyTo(text));
        Assert.Equal("{ \"a\": 1, \"b\": [1, 2, 3] }", JsonSplice.RemoveMember(root!, 2).ApplyTo(text));
        var array = root!.Get("b")!;
        Assert.Equal("{ \"a\": 1, \"b\": [2, 3], \"c\": { \"d\": 4 } }", JsonSplice.RemoveItem(array, 0).ApplyTo(text));
        Assert.Equal("{ \"a\": 1, \"b\": [1, 3], \"c\": { \"d\": 4 } }", JsonSplice.RemoveItem(array, 1).ApplyTo(text));
        Assert.Equal("{ \"a\": 1, \"b\": [1, 2, 3, 4], \"c\": { \"d\": 4 } }", JsonSplice.InsertItem(text, array, _ => "4", JsonStyle.Default).ApplyTo(text));
        Assert.Equal("{ \"a\": 1, \"b\": [1, 2, 3], \"c\": { \"d\": 4 }, \"e\": true }", JsonSplice.InsertMember(text, root, "e", _ => "true", JsonStyle.Default).ApplyTo(text));
        Assert.Equal("{ \"a\": 1, \"b\": [1, 2, 3], \"c\": {} }", JsonSplice.RemoveMember(root.Get("c")!, 0).ApplyTo(text));
        Assert.Equal("\"a\\\"b\\n\"", JsonSplice.Quote("a\"b\n"));
    }
}
