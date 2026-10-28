using System.Diagnostics;
using NksHub.Textida.Plugins.Lsp;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>
/// The Roslyn server does not look for a solution by itself: it has to be told to open one and says when it has loaded it. A fake server that behaves like that (a launcher named like the Roslyn command starts it)
/// shows that the plugin tells it, and that a rename waits for the answer instead of reaching the one document the server knows. The same against the real server is in <c>LspPluginTests.Real.cs</c>.
/// </summary>
public partial class LspPluginTests
{
    /// <summary>Deletes a folder of a test that started or killed programs: the system keeps their image files locked for a moment.</summary>
    private static void TestDirDelete(string folder)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                Directory.Delete(folder, true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(100); }
        }
    }

    /// <summary>A program named like the command of the Roslyn .NET tool that starts the dotnet host with the arguments it gets, so the plugin takes the fake server for Roslyn.</summary>
    private static string RoslynLauncher(string folder)
    {
        Directory.CreateDirectory(folder);
        if (OperatingSystem.IsWindows())
        {
            string batch = Path.Combine(folder, "roslyn-language-server.cmd");
            File.WriteAllText(batch, $"@echo off\r\n\"{T.DotnetFullPath}\" %*\r\n");
            return batch;
        }
        string script = Path.Combine(folder, "roslyn-language-server");
        File.WriteAllText(script, $"#!/bin/sh\nexec \"{T.DotnetFullPath}\" \"$@\"\n");
        File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return script;
    }

    [Fact]
    public async Task A_Roslyn_server_is_told_to_open_the_project_and_a_rename_waits_until_it_says_it_loaded_it()
    {
        string folder = Path.Combine(Path.GetTempPath(), "textida-roslyn-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await using var rig = await Rig.CreateAsync(["--load-ms", "2500"], declared: true, program: RoslynLauncher(folder));
            File.WriteAllText(Path.Combine(rig.Folder, "demo.csproj"), "<Project/>");
            var first = rig.Add("one.fake", "foo = foo + 1\n");
            var second = rig.Add("two.fake", "call foo\n");
            await rig.RunAsync(first, LspPlugin.StartCommand);
            await rig.RunAsync(second, LspPlugin.StartCommand);
            rig.Ui.Input = _ => "bar";

            var watch = Stopwatch.StartNew();
            var r = await rig.RunAsync(first, LspPlugin.RenameCommand, 1);

            Assert.Equal(("ok", "applied", 2, 2), (r.GetProperty("status").GetString(), r.GetProperty("decision").GetString(), r.GetProperty("accepted").GetInt32(), r.GetProperty("total").GetInt32()));
            Assert.Equal("bar = bar + 1\n", Rig.TextOf(first));
            Assert.Equal("call bar\n", Rig.TextOf(second));
            string[] notes = File.ReadAllLines(Assert.Single(Directory.GetFiles(rig.Dump, "process-*")));
            Assert.Single(notes, n => n.StartsWith("workspace project/open ", StringComparison.Ordinal) && n.Contains("demo.csproj", StringComparison.Ordinal));
        }
        finally { TestDirDelete(folder); }
    }

    [Fact]
    public async Task A_Roslyn_server_in_a_folder_without_solution_or_project_is_told_nothing_and_a_rename_does_not_wait()
    {
        string folder = Path.Combine(Path.GetTempPath(), "textida-roslyn-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            await using var rig = await Rig.CreateAsync(["--load-ms", "60000"], declared: true, program: RoslynLauncher(folder));
            var only = rig.Add("one.fake", "foo = foo + 1\n");
            await rig.RunAsync(only, LspPlugin.StartCommand);
            rig.Ui.Input = _ => "bar";

            var watch = Stopwatch.StartNew();
            var r = await rig.RunAsync(only, LspPlugin.RenameCommand, 1);

            Assert.Equal("ok", r.GetProperty("status").GetString());
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"waited {watch.Elapsed} for a project nobody opened");
            Assert.DoesNotContain(File.ReadAllLines(Assert.Single(Directory.GetFiles(rig.Dump, "process-*"))), n => n.StartsWith("workspace ", StringComparison.Ordinal));
        }
        finally { TestDirDelete(folder); }
    }

    [Fact]
    public void A_file_uri_with_an_escaped_drive_colon_and_a_lower_case_drive_letter_is_the_path_of_the_file_on_Windows()
    {
        // Roslyn answers file:///f%3A/dir/a.cs for the document the plugin opened as file:///F:/dir/a.cs
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal("/f:/x/a.cs", PathUri.ToPath("file:///f%3A/x/a.cs")); // there is no drive: it stays a folder name
            return;
        }
        Assert.Equal(@"f:\x\a.cs", PathUri.ToPath("file:///f%3A/x/a.cs"));
        Assert.Equal(@"C:\Users\a b\c.cs", PathUri.ToPath("file:///C%3a/Users/a%20b/c.cs"));
        Assert.Equal(@"F:\x\a.cs", PathUri.ToPath("file:///F:/x/a.cs"));
        Assert.True(PathUri.SamePath(PathUri.ToPath("file:///f%3A/x/a.cs")!, PathUri.ToPath(PathUri.FromPath(@"F:\x\a.cs"))!));
    }

    [Fact]
    public void The_workspace_a_Roslyn_server_is_told_to_open_is_the_solution_else_the_projects_of_the_root_and_nothing_for_a_folder_without_either()
    {
        string root = Directory.CreateTempSubdirectory("textida-ws-").FullName;
        try
        {
            Assert.Null(RoslynWorkspace.Open(root));
            File.WriteAllText(Path.Combine(root, "b.csproj"), "");
            File.WriteAllText(Path.Combine(root, "a.csproj"), "");
            Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(root, "nested", "n.csproj"), "");
            var projects = RoslynWorkspace.Open(root)!.Value;
            Assert.Equal("project/open", projects.Method);
            using (var json = System.Text.Json.JsonDocument.Parse(projects.Params))
                Assert.Equal(["a.csproj", "b.csproj"], json.RootElement.GetProperty("projects").EnumerateArray().Select(p => Path.GetFileName(PathUri.ToPath(p.GetString()!))));

            File.WriteAllText(Path.Combine(root, "z.slnx"), "");
            File.WriteAllText(Path.Combine(root, "m.sln"), "");
            var solution = RoslynWorkspace.Open(root)!.Value;
            Assert.Equal("solution/open", solution.Method);
            using (var json = System.Text.Json.JsonDocument.Parse(solution.Params))
                Assert.Equal("m.sln", Path.GetFileName(PathUri.ToPath(json.RootElement.GetProperty("solution").GetString()!)));

            Assert.True(RoslynWorkspace.IsRoslyn(Path.Combine(root, "roslyn-language-server.cmd")));
            Assert.True(RoslynWorkspace.IsRoslyn(Path.Combine(root, "Microsoft.CodeAnalysis.LanguageServer"))); // no extension: the dots belong to the name
            Assert.True(RoslynWorkspace.IsRoslyn(Path.Combine(root, "Microsoft.CodeAnalysis.LanguageServer.exe")));
            Assert.False(RoslynWorkspace.IsRoslyn(Path.Combine(root, "csharp-ls.exe")));
        }
        finally { Directory.Delete(root, true); }
    }
}
