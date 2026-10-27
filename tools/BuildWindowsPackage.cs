// Builds the Windows package: one ZIP with the NativeAOT publish output, the per-user installer (tools/windows-installer) and version.txt.
// The ZIP is the portable form (unpack, start Textida.exe) and the installer (Install.cmd; per user, no administrator rights).
//
//   dotnet run tools/BuildWindowsPackage.cs -- [--version V] [--rid win-x64] [--output DIR] [--publish-dir DIR] [--dotnet PATH]
//
// It publishes the app unless --publish-dir names an existing output, builds the AOT check and runs it on the publish output, zips, and runs it on the ZIP.
// NativeAOT needs the Visual Studio C++ tools; link.exe finds them through vswhere.exe, so that folder must be on PATH.
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

var options = new Dictionary<string, string>();
for (int i = 0; i + 1 < args.Length; i += 2) options[args[i]] = args[i + 1];

var tools = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetFullPath("tools");
var root = Path.GetFullPath(Path.Combine(tools, ".."));
var rid = options.GetValueOrDefault("--rid", "win-x64");
var dotnet = options.GetValueOrDefault("--dotnet", "dotnet");
var version = options.TryGetValue("--version", out var v) ? v : "0.0.0-" + Capture("git", "-C", root, "rev-parse", "--short", "HEAD").Trim();
var output = Path.GetFullPath(options.GetValueOrDefault("--output", Path.Combine(root, "artifacts")));
var work = Path.Combine(Path.GetTempPath(), "textida-package-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var publish = options.TryGetValue("--publish-dir", out var given) ? Path.GetFullPath(given) : Path.Combine(work, "publish");
    if (!options.ContainsKey("--publish-dir")) Run(dotnet, "publish", Path.Combine(root, "src", "NksHub.Textida.App"), "-c", "Release", "-r", rid, "-o", publish);

    var check = Path.Combine(work, "aotcheck");
    Run(dotnet, "publish", Path.Combine(root, "tools", "NksHub.Textida.AotCheck"), "-c", "Release", "-r", rid, "-o", check);
    var checker = Path.Combine(check, "NksHub.Textida.AotCheck" + (OperatingSystem.IsWindows() ? ".exe" : ""));
    Run(checker, "--publish", publish);

    var stage = Path.Combine(work, "stage");
    CopyTree(publish, stage);
    foreach (var file in Directory.GetFiles(Path.Combine(tools, "windows-installer"))) File.Copy(file, Path.Combine(stage, Path.GetFileName(file)));
    File.WriteAllText(Path.Combine(stage, "version.txt"), version);

    Directory.CreateDirectory(output);
    var zip = Path.Combine(output, $"Textida-{version}-{rid}.zip");
    File.Delete(zip);
    ZipFile.CreateFromDirectory(stage, zip);

    Run(checker, "--package", zip);
    Console.WriteLine(zip);
    Console.WriteLine("SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))));
}
finally { Directory.Delete(work, recursive: true); }

static void CopyTree(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    foreach (var dir in Directory.GetDirectories(from)) CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
}

static void Run(string program, params string[] arguments)
{
    using var process = Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = false })!;
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(program)} {arguments[0]} failed ({process.ExitCode})");
}

static string Capture(string program, params string[] arguments)
{
    using var process = Process.Start(new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, UseShellExecute = false })!;
    var text = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return text;
}
