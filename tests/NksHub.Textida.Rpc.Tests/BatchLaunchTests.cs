using System.Text;
using NksHub.Textida.Plugins.Git;
using NksHub.Textida.Plugins.Lsp;

namespace NksHub.Textida.Rpc.Tests;

/// <summary>A program resolved by the Git and LSP plugins may be a Windows batch file; it is started through cmd.exe with every argument quoted, or the start is refused before anything runs.</summary>
[Collection("PATH")]
public sealed class BatchLaunchTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-batch-").FullName;

    private static string Dotnet => Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";

    private static string EchoDll => Path.Combine(AppContext.BaseDirectory, "NksHub.Textida.ToolEcho.dll");

    private static readonly string[] Awkward = ["a&b", "%PATH%", "say \"hi\"", "c^d", "x y", "100%", "back\\"];

    private static string[] Brackets(IEnumerable<string> args) => [.. args.Select(a => "[" + a + "]")];

    public void Dispose() => T.DeleteFolder(_dir);

    /// <summary>A batch file that prints its arguments, one per line in brackets, to standard output (or to <paramref name="outFile"/>).</summary>
    private string EchoBatch(string name, string? outFile = null)
    {
        string bat = Path.Combine(_dir, name);
        File.WriteAllText(bat, $"@echo off\r\n\"{Dotnet}\" \"{EchoDll}\" %*" + (outFile is null ? "" : $" > \"{outFile}\"") + "\r\n");
        return bat;
    }

    [Fact]
    public async Task Git_found_as_a_batch_file_gets_every_argument_back_and_a_line_break_is_refused()
    {
        if (!OperatingSystem.IsWindows()) return;
        // answers the probe for the repository root itself, echoes the arguments of every other command
        File.WriteAllText(Path.Combine(_dir, "git.cmd"), $"@echo off\r\nif \"%~2\"==\"rev-parse\" (\r\necho {_dir}\r\necho.\r\nexit /b 0\r\n)\r\n\"{Dotnet}\" \"{EchoDll}\" %*\r\n");
        string path = Environment.GetEnvironmentVariable("PATH") ?? "";
        Environment.SetEnvironmentVariable("PATH", _dir + Path.PathSeparator + path);
        try
        {
            var repo = await GitRepository.OpenAsync(_dir, 10_000, 1 << 20, CancellationToken.None);
            Assert.NotNull(repo);
            foreach (var text in Awkward)
                Assert.Equal(Brackets(["--literal-pathspecs", "cat-file", "blob", text]), Encoding.UTF8.GetString(await repo.ReadBlobAsync(text, CancellationToken.None)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
            await Assert.ThrowsAsync<GitException>(() => repo.ReadBlobAsync("one\ntwo", CancellationToken.None));
        }
        finally { Environment.SetEnvironmentVariable("PATH", path); }
    }

    private static Task<LspServer> StartLsp(string program, IReadOnlyList<string> args, string root) =>
        LspServer.StartAsync(new ServerLaunch("x", program, args, new Dictionary<string, string>()), root,
            new LspSettings(1 << 20, 10_000, 10_000, 1_000, 100, 10, 1_000, 100), (_, _) => ValueTask.CompletedTask, CancellationToken.None);

    [Fact]
    public async Task A_language_server_that_is_a_batch_file_gets_every_argument_back()
    {
        if (!OperatingSystem.IsWindows()) return;
        string output = Path.Combine(_dir, "args.txt");
        // the batch file ends without speaking LSP, so the start fails after the arguments were written
        await Assert.ThrowsAsync<ServerStartException>(() => StartLsp(EchoBatch("server.cmd", output), Awkward, _dir));
        Assert.Equal(Brackets(Awkward), File.ReadAllLines(output));
    }

    [Fact]
    public async Task A_language_server_batch_file_with_a_line_break_in_an_argument_is_refused_before_it_starts()
    {
        if (!OperatingSystem.IsWindows()) return;
        string output = Path.Combine(_dir, "args.txt");
        var e = await Assert.ThrowsAsync<ServerStartException>(() => StartLsp(EchoBatch("server.cmd", output), ["fine", "one\ntwo"], _dir));
        Assert.Contains("control character", e.Message);
        Assert.False(File.Exists(output));
    }
}
