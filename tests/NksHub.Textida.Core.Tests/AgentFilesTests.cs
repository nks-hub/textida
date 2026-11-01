using NksHub.Textida.Core.Agents;

namespace NksHub.Textida.Core.Tests;

/// <summary>PZ.6.6: the files an agent loads for a file, with size and a token estimate, and the plans agents leave.</summary>
public sealed class AgentFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "agents-" + Guid.NewGuid().ToString("N")[..8]);

    public AgentFilesTests() => Directory.CreateDirectory(_root);

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

    [Fact]
    public void The_chain_goes_from_the_root_down_to_the_folder_of_the_file_with_size_and_tokens()
    {
        Put("AGENTS.md", new string('a', 400));
        Put("CLAUDE.md", "root rules");
        Put("src/CLAUDE.md", "src rules");
        Put("src/deep/AGENTS.md", "deep rules");
        Put("other/AGENTS.md", "not in the chain");
        string file = Put("src/deep/x.cs", "class X;");

        var files = AgentFiles.Find(_root, file, null, TokenEstimator.Generic);

        Assert.Equal(["AGENTS.md", "CLAUDE.md", "src/CLAUDE.md", "src/deep/AGENTS.md"], files.Select(f => f.Display));
        Assert.Equal((400L, 100L), (files[0].Bytes, files[0].Tokens));
        Assert.All(files, f => Assert.True(f.Applies));
    }

    [Fact]
    public void A_rule_limited_to_paths_applies_only_to_the_files_that_match_and_says_why_otherwise()
    {
        Put(".claude/rules/always.md", "no header");
        Put(".claude/rules/web.md", "---\npaths:\n  - \"src/**/*.ts\"\n---\nweb rules");
        Put(".cursor/rules/style.mdc", "---\nglobs: docs/**\nalwaysApply: false\n---\nstyle");
        Put(".cursor/rules/all.mdc", "---\nglobs: docs/**\nalwaysApply: true\n---\nall");
        string ts = Put("src/app/a.ts", "");
        string md = Put("docs/a.md", "");

        var forTs = AgentFiles.Find(_root, ts, null, TokenEstimator.Generic).ToDictionary(f => f.Display);
        Assert.True(forTs[".claude/rules/always.md"].Applies);
        Assert.True(forTs[".claude/rules/web.md"].Applies);
        Assert.False(forTs[".cursor/rules/style.mdc"].Applies);
        Assert.Equal("docs/**", forTs[".cursor/rules/style.mdc"].Limit);
        Assert.True(forTs[".cursor/rules/all.mdc"].Applies);

        var forMd = AgentFiles.Find(_root, md, null, TokenEstimator.Generic).ToDictionary(f => f.Display);
        Assert.False(forMd[".claude/rules/web.md"].Applies);
        Assert.True(forMd[".cursor/rules/style.mdc"].Applies);
    }

    [Fact]
    public void Skills_and_the_home_instructions_are_listed_and_a_folder_without_agent_files_gives_nothing()
    {
        Assert.Empty(AgentFiles.Find(_root, null, null, TokenEstimator.Generic));
        Put(".claude/skills/deploy/SKILL.md", "deploy steps");
        string home = Path.Combine(_root, "home");
        Directory.CreateDirectory(Path.Combine(home, ".claude"));
        File.WriteAllText(Path.Combine(home, ".claude", "CLAUDE.md"), "mine");

        var files = AgentFiles.Find(_root, null, home, TokenEstimator.Generic);

        Assert.Equal([("~/.claude/CLAUDE.md", AgentFileKind.Instructions), (".claude/skills/deploy/SKILL.md", AgentFileKind.Skill)], files.Select(f => (f.Display, f.Kind)));
    }

    [Fact]
    public void Plans_are_found_in_the_known_folders_newest_first()
    {
        string old = Put(".claude/plans/old.md", "1");
        string fresh = Put("docs/plans/new.md", "2");
        string single = Put("PLAN.md", "3");
        File.SetLastWriteTimeUtc(old, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(single, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(fresh, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal([fresh, single, old], AgentFiles.FindPlans(_root, null));
    }
}
