#:project ../src/NksHub.Textida.Install/NksHub.Textida.Install.csproj
// Builds the macOS package: Textida.app (the NativeAOT publish output: the program and its libraries in Contents/MacOS, the plugins, the MCP program and the notices in Contents/Resources; Info.plist, the icon), signed ad hoc, as a ZIP and as a disk image with a link to Applications.
//
//   dotnet run tools/BuildMacPackage.cs -- [--version V] [--rid osx-arm64|osx-x64] [--output DIR] [--publish-dir DIR] [--dotnet PATH] [--no-dmg]
//
// It runs only on macOS (it needs ditto, hdiutil, codesign and the SDK of the Mac) and says so on any other system. It publishes the app unless --publish-dir names an existing output, draws the icon with
// tools/MakeIcon.cs, signs every Mach-O file and then the bundle with the ad-hoc identity ("-"), which is what an Apple silicon Mac needs to start a program at all, and makes the packages. Then it runs the AOT check on
// the ZIP and on the disk image: the contents, the minimum system version (macOS 14) of every Mach-O file, the signature, and that every built-in plugin starts. It ends with the record of the build next to the packages:
// the SHA-256 of each file, the version of macOS and the architecture of the Mac that made them.
//
// DECISION: one package for each architecture (--rid), not one universal bundle made with lipo. The two packages are built by the same call on any Mac (a Mac with Apple silicon builds the Intel one as well,
// and starts its programs under Rosetta for the check); a universal bundle would double the size of every download and needs every plugin and library merged, which no user needs.
// Developer ID signing and notarization are not done here (PR.3.1); the help describes the first start of an application that is not notarized.
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Install;

var options = new Dictionary<string, string>();
var flags = new HashSet<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--no-dmg") flags.Add(args[i]);
    else if (i + 1 < args.Length) options[args[i]] = args[++i];
    else
    {
        Console.Error.WriteLine($"BuildMacPackage: {args[i]} needs a value.");
        return 2;
    }
}

if (!OperatingSystem.IsMacOS())
{
    Console.Error.WriteLine("BuildMacPackage runs on macOS only: it needs ditto, hdiutil and codesign of the Mac. Run it on a Mac.");
    return 2;
}

var tools = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetFullPath("tools");
var root = Path.GetFullPath(Path.Combine(tools, ".."));
var hostRid = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
var rid = options.GetValueOrDefault("--rid", hostRid);
if (rid is not ("osx-arm64" or "osx-x64"))
{
    Console.Error.WriteLine($"BuildMacPackage: --rid is osx-arm64 or osx-x64, not {rid}.");
    return 2;
}
var dotnet = options.GetValueOrDefault("--dotnet", "dotnet");
var version = options.TryGetValue("--version", out var v) ? v : "0.0.0-" + Capture("git", "-C", root, "rev-parse", "--short", "HEAD").Trim();
var output = Path.GetFullPath(options.GetValueOrDefault("--output", Path.Combine(root, "artifacts")));
var work = Path.Combine(Path.GetTempPath(), "textida-package-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var publish = options.TryGetValue("--publish-dir", out var given) ? Path.GetFullPath(given) : Path.Combine(work, "publish");
    if (!options.ContainsKey("--publish-dir")) Run(dotnet, "publish", Path.Combine(root, "src", "NksHub.Textida.App"), "-c", "Release", "-r", rid, "-o", publish);

    // the AOT check runs on this Mac, so it is built for this Mac's architecture; the package may be for the other one (Rosetta starts those programs)
    var check = Path.Combine(work, "aotcheck");
    Run(dotnet, "publish", Path.Combine(root, "tools", "NksHub.Textida.AotCheck"), "-c", "Release", "-r", hostRid, "-o", check);
    var checker = Path.Combine(check, "NksHub.Textida.AotCheck");
    Run(checker, "--publish", publish);

    var icons = Path.Combine(work, "icon");
    Run(dotnet, "run", Path.Combine(tools, "MakeIcon.cs"), "--", icons);

    var stage = Path.Combine(work, "stage");
    var app = Path.Combine(stage, MacBundle.AppName + ".app");
    var contents = Path.Combine(app, "Contents");
    // Contents/MacOS may hold only the executable and its libraries, or the signature of the bundle is refused; the plugins, the MCP program and the notices go to Contents/Resources,
    // where the program looks for them in a bundle (ProgramFolders)
    var macos = Path.Combine(contents, "MacOS");
    var resources = Path.Combine(contents, "Resources");
    Directory.CreateDirectory(macos);
    Directory.CreateDirectory(resources);
    foreach (var file in Directory.GetFiles(publish)) File.Copy(file, Path.Combine(IsMachO(file) ? macos : resources, Path.GetFileName(file))); // File.Copy keeps the permission bits
    foreach (var dir in Directory.GetDirectories(publish)) CopyTree(dir, Path.Combine(resources, Path.GetFileName(dir)));
    File.WriteAllText(Path.Combine(resources, "version.txt"), version);
    File.Copy(Path.Combine(icons, "textida.icns"), Path.Combine(resources, MacBundle.IconName + ".icns"));
    File.WriteAllText(Path.Combine(contents, "Info.plist"), MacBundle.InfoPlist(version), new UTF8Encoding(false));
    File.WriteAllText(Path.Combine(contents, "PkgInfo"), "APPL????", Encoding.ASCII);
    Run("plutil", "-lint", Path.Combine(contents, "Info.plist"));

    // every Mach-O file first, then the bundle; the identity "-" is the ad-hoc signature (no certificate, no Apple account)
    foreach (var file in Directory.EnumerateFiles(contents, "*", SearchOption.AllDirectories).Where(IsMachO))
        Run("codesign", "--force", "--sign", "-", "--timestamp=none", file);
    Run("codesign", "--force", "--sign", "-", "--timestamp=none", app);
    Run("codesign", "--verify", "--deep", "--strict", app);

    Directory.CreateDirectory(output);
    var zip = Path.Combine(output, $"Textida-{version}-{rid}.zip");
    File.Delete(zip);
    Run("ditto", "-c", "-k", "--keepParent", app, zip); // ditto keeps the permissions of the programs; a ZIP written by .NET would not
    var packages = new List<string> { zip };

    if (!flags.Contains("--no-dmg"))
    {
        var image = Path.Combine(work, "image");
        Directory.CreateDirectory(image);
        Run("ditto", app, Path.Combine(image, MacBundle.AppName + ".app"));
        File.CreateSymbolicLink(Path.Combine(image, "Applications"), "/Applications");
        var dmg = Path.Combine(output, $"Textida-{version}-{rid}.dmg");
        File.Delete(dmg);
        Run("hdiutil", "create", "-volname", MacBundle.AppName, "-srcfolder", image, "-ov", "-format", "UDZO", dmg);
        packages.Add(dmg);
    }

    foreach (var package in packages) Run(checker, "--package", package, "--expect-version", version);

    var record = new StringBuilder();
    record.AppendLine($"Textida {version} {rid}");
    record.AppendLine($"built {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z on macOS {Capture("sw_vers", "-productVersion").Trim()} ({Capture("sw_vers", "-buildVersion").Trim()}), {Capture("uname", "-m").Trim()}");
    record.AppendLine($"commit {Capture("git", "-C", root, "rev-parse", "--short", "HEAD").Trim()}");
    foreach (var package in packages) record.AppendLine($"SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package)))} {Path.GetFileName(package)} ({new FileInfo(package).Length} bytes)");
    var recordFile = Path.Combine(output, $"Textida-{version}-{rid}.build.txt");
    File.WriteAllText(recordFile, record.ToString());
    foreach (var package in packages) Console.WriteLine(package);
    Console.Write(record);
    return 0;
}
finally { Directory.Delete(work, recursive: true); }

static bool IsMachO(string path)
{
    using var file = File.OpenRead(path);
    Span<byte> head = stackalloc byte[4];
    if (file.Read(head) < 4) return false;
    uint le = BitConverter.ToUInt32(head), be = (uint)((head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3]);
    return le == 0xFEEDFACF || be == 0xCAFEBABE;
}

static void CopyTree(string from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file))); // File.Copy keeps the permission bits
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
