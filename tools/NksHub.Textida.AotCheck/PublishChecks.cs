using System.Diagnostics;

/// <summary>
/// Checks a publish output of the app (<c>--publish DIR</c>): every built-in plugin and <c>textida-mcp</c> sits where <c>PluginServices</c> and <c>McpService</c> look for it,
/// starts, and reaches its own <c>Main</c> (each exits with 2 when started without its options); no runtimeconfig.json, and no .pdb or .xml unless <c>--symbols</c> is given.
/// </summary>
internal static class PublishChecks
{
    private static readonly (string Folder, string Program)[] Programs =
    [
        ("plugins/json", "NksHub.Textida.Plugins.Json"),
        ("plugins/texttools", "NksHub.Textida.Plugins.TextTools"),
        ("plugins/webdata", "NksHub.Textida.Plugins.WebData"),
        ("plugins/codeexplorer", "NksHub.Textida.Plugins.CodeExplorer"),
        ("plugins/emmet", "NksHub.Textida.Plugins.Emmet"),
        ("plugins/formatters", "NksHub.Textida.Plugins.Formatters"),
        ("plugins/git", "NksHub.Textida.Plugins.Git"),
        ("plugins/lsp", "NksHub.Textida.Plugins.Lsp"),
        ("mcp", "textida-mcp"),
    ];

    public static void Run(string[] args, Action<bool, string> check)
    {
        int at = Array.IndexOf(args, "--publish");
        if (at < 0) return;
        var root = args[at + 1];
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        check(File.Exists(Path.Combine(root, "Textida" + suffix)), "publish: Textida" + suffix);
        foreach (var (folder, program) in Programs)
        {
            var path = Path.Combine(root, folder, program + suffix);
            if (!File.Exists(path)) { check(false, "publish: missing " + folder + "/" + program + suffix); continue; }
            // textida-mcp answers a stray argument with its usage and 2; the plugins answer missing options with 2
            using var process = Process.Start(new ProcessStartInfo(path, program == "textida-mcp" ? "--check" : "")
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            })!;
            var errors = process.StandardError.ReadToEndAsync();
            process.StandardInput.Close();
            bool exited = process.WaitForExit(15_000);
            if (!exited) process.Kill(true);
            check(exited && process.ExitCode == 2, $"publish: {folder}/{program}{suffix} starts (exit {(exited ? process.ExitCode : -1)}) {errors.Result.Trim()}");
        }
        bool symbols = args.Contains("--symbols");
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            bool stray = name.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase)
                || (!symbols && (name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)));
            check(!stray, "publish: unexpected file " + Path.GetRelativePath(root, file));
        }
    }
}
