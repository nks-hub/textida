#:project ../src/NksHub.Textida.Install/NksHub.Textida.Install.csproj
// Builds the Windows package: one ZIP with the NativeAOT publish output, the script installer (tools/windows-installer) and version.txt, and TextidaSetup-<version>-<rid>.exe next to it.
// The ZIP is the portable form (unpack, start Textida.exe) and the script installer (Install.cmd; per user, no administrator rights).
// The setup program is the installer with options: the NativeAOT program (src/NksHub.Textida.Setup), the native libraries of its window and the ZIP, appended to it, so the user downloads one file.
//
//   dotnet run tools/BuildWindowsPackage.cs -- [--version V] [--rid win-x64] [--output DIR] [--publish-dir DIR] [--dotnet PATH] [--no-run-setup]
//
// It publishes the app unless --publish-dir names an existing output, builds the AOT check and runs it on the publish output, zips, and runs it on the ZIP. Then it publishes the setup program, joins the three
// parts and runs the AOT check on the result, which starts the setup with --check (no window, nothing written) unless --no-run-setup is given.
// NativeAOT needs the Visual Studio C++ tools; link.exe finds them through vswhere.exe, so that folder must be on PATH.
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using NksHub.Textida.Install;

var options = new Dictionary<string, string>();
var flags = new HashSet<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--no-run-setup") flags.Add(args[i]);
    else if (i + 1 < args.Length) options[args[i]] = args[++i];
}

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

    if (rid.StartsWith("win-", StringComparison.Ordinal))
    {
        // the program is published without the native libraries of Skia and HarfBuzz inside it; they go into the file as a ZIP of their own, and the window needs no GPU, so ANGLE stays out
        var setupPublish = Path.Combine(work, "setup");
        Run(dotnet, "publish", Path.Combine(root, "src", "NksHub.Textida.Setup"), "-c", "Release", "-r", rid, "-o", setupPublish);
        var natives = Path.Combine(work, "natives.zip");
        using (var archive = ZipFile.Open(natives, ZipArchiveMode.Create))
            foreach (var library in Directory.GetFiles(setupPublish, "*.dll").Where(f => !Path.GetFileName(f).Equals("av_libglesv2.dll", StringComparison.OrdinalIgnoreCase)))
                archive.CreateEntryFromFile(library, Path.GetFileName(library), CompressionLevel.Optimal);

        var withNatives = Path.Combine(work, "TextidaSetup.natives.exe");
        PayloadFile.Append(Path.Combine(setupPublish, "TextidaSetup.exe"), natives, withNatives, PayloadKind.Natives);
        var setup = Path.Combine(output, $"TextidaSetup-{version}-{rid}.exe");
        PayloadFile.Append(withNatives, zip, setup);

        Run(checker, ["--setup", setup, "--expect-version", version, .. flags.Contains("--no-run-setup") ? Array.Empty<string>() : ["--run-setup"]]);
        Console.WriteLine(setup);
        Console.WriteLine("SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup))));
    }
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
