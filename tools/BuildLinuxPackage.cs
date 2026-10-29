// Builds the Linux packages: Textida-<version>-linux-x64.tar.gz (the NativeAOT publish output with install.sh, which installs for one user into ~/.local, and uninstall.sh)
// and textida_<version>_amd64.deb (the same program in /opt/textida with /usr/bin/textida, a desktop entry and the icon).
//
//   dotnet run tools/BuildLinuxPackage.cs -- [--version V] [--rid linux-x64] [--output DIR] [--publish-dir DIR] [--dotnet PATH] [--no-deb]
//
// It runs only on Linux (it needs tar, dpkg-deb and the NativeAOT tool chain: clang and zlib). It publishes the app unless --publish-dir names an existing output and runs the AOT check on it,
// which starts every built-in plugin. Linux is not a supported system yet (R10 names Windows and macOS), so these packages are a preview.
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

var options = new Dictionary<string, string>();
var flags = new HashSet<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--no-deb") flags.Add(args[i]);
    else if (i + 1 < args.Length) options[args[i]] = args[++i];
    else
    {
        Console.Error.WriteLine($"BuildLinuxPackage: {args[i]} needs a value.");
        return 2;
    }
}

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("BuildLinuxPackage runs on Linux only: NativeAOT does not cross-compile to Linux, and it needs tar and dpkg-deb.");
    return 2;
}

var tools = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetFullPath("tools");
var root = Path.GetFullPath(Path.Combine(tools, ".."));
var rid = options.GetValueOrDefault("--rid", "linux-x64");
var debArch = rid switch { "linux-x64" => "amd64", "linux-arm64" => "arm64", _ => null };
if (debArch is null)
{
    Console.Error.WriteLine($"BuildLinuxPackage: --rid is linux-x64 or linux-arm64, not {rid}.");
    return 2;
}
var dotnet = options.GetValueOrDefault("--dotnet", "dotnet");
var version = options.TryGetValue("--version", out var v) ? v : "0.0.0-" + Capture("git", "-C", root, "rev-parse", "--short", "HEAD").Trim();
var output = Path.GetFullPath(options.GetValueOrDefault("--output", Path.Combine(root, "artifacts")));
var icon = Path.Combine(root, "src", "NksHub.Textida.App", "Assets", "textida-256.png");
var work = Path.Combine(Path.GetTempPath(), "textida-package-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(work);
try
{
    var publish = options.TryGetValue("--publish-dir", out var given) ? Path.GetFullPath(given) : Path.Combine(work, "publish");
    if (!options.ContainsKey("--publish-dir")) Run(dotnet, "publish", Path.Combine(root, "src", "NksHub.Textida.App"), "-c", "Release", "-r", rid, "-o", publish);

    var check = Path.Combine(work, "aotcheck");
    Run(dotnet, "publish", Path.Combine(root, "tools", "NksHub.Textida.AotCheck"), "-c", "Release", "-r", rid, "-o", check);
    Run(Path.Combine(check, "NksHub.Textida.AotCheck"), "--publish", publish);

    Directory.CreateDirectory(output);
    var packages = new List<string>();

    // the archive: one folder named after the package, so unpacking it never spills files into the current folder
    var name = $"Textida-{version}-{rid}";
    var folder = Path.Combine(work, "tar", name);
    CopyTree(publish, folder);
    File.WriteAllText(Path.Combine(folder, "version.txt"), version);
    File.Copy(icon, Path.Combine(folder, "textida.png"));
    WriteScript(Path.Combine(folder, "install.sh"), InstallScript);
    WriteScript(Path.Combine(folder, "uninstall.sh"), UninstallScript);
    var tar = Path.Combine(output, name + ".tar.gz");
    File.Delete(tar);
    Run("tar", "-czf", tar, "-C", Path.Combine(work, "tar"), name); // tar keeps the permission bits of the programs
    packages.Add(tar);

    if (!flags.Contains("--no-deb"))
    {
        var deb = Path.Combine(work, "deb");
        var opt = Path.Combine(deb, "opt", "textida");
        CopyTree(publish, opt);
        File.WriteAllText(Path.Combine(opt, "version.txt"), version);
        Directory.CreateDirectory(Path.Combine(deb, "usr", "bin"));
        File.CreateSymbolicLink(Path.Combine(deb, "usr", "bin", "textida"), "/opt/textida/Textida");
        Directory.CreateDirectory(Path.Combine(deb, "usr", "share", "applications"));
        File.WriteAllText(Path.Combine(deb, "usr", "share", "applications", "textida.desktop"), DesktopEntry("/opt/textida/Textida", "textida"));
        var iconDir = Path.Combine(deb, "usr", "share", "icons", "hicolor", "256x256", "apps");
        Directory.CreateDirectory(iconDir);
        File.Copy(icon, Path.Combine(iconDir, "textida.png"));
        Directory.CreateDirectory(Path.Combine(deb, "DEBIAN"));
        File.WriteAllText(Path.Combine(deb, "DEBIAN", "control"), Control(DebVersion(version), debArch, DirectorySize(deb) / 1024));
        var debFile = Path.Combine(output, $"textida_{DebVersion(version)}_{debArch}.deb");
        File.Delete(debFile);
        Run("dpkg-deb", "--root-owner-group", "--build", deb, debFile);
        packages.Add(debFile);
    }

    var record = new StringBuilder();
    record.AppendLine($"Textida {version} {rid}");
    record.AppendLine($"built {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z on {File.ReadLines("/etc/os-release").FirstOrDefault(l => l.StartsWith("PRETTY_NAME=", StringComparison.Ordinal))?[12..].Trim('"')}, {Capture("uname", "-m").Trim()}");
    record.AppendLine($"commit {Capture("git", "-C", root, "rev-parse", "--short", "HEAD").Trim()}");
    foreach (var package in packages) record.AppendLine($"SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(package)))} {Path.GetFileName(package)} ({new FileInfo(package).Length} bytes)");
    File.WriteAllText(Path.Combine(output, $"Textida-{version}-{rid}.build.txt"), record.ToString());
    foreach (var package in packages) Console.WriteLine(package);
    Console.Write(record);
    return 0;
}
finally { Directory.Delete(work, recursive: true); }

// Debian sorts "~" before the release, so 0.2.0~rc1 is older than 0.2.0; a "-" would start the Debian revision instead
static string DebVersion(string version) => version.Replace('-', '~');

static string Control(string version, string arch, long installedKiB) => $"""
    Package: textida
    Version: {version}
    Architecture: {arch}
    Maintainer: NKS Hub <dev@nks-hub.cz>
    Installed-Size: {installedKiB}
    Depends: libc6, libfontconfig1, libfreetype6, libx11-6, libice6, libsm6
    Section: editors
    Priority: optional
    Homepage: https://github.com/nks-hub/textida
    Description: Text and hex editor with a paged core for files larger than RAM
     Textida opens text and binary files of any size, edits them in place and keeps
     a local history of every file. Preview build for Linux.

    """;

static string DesktopEntry(string exec, string icon) => $"""
    [Desktop Entry]
    Type=Application
    Name=Textida
    GenericName=Text Editor
    Comment=Text and hex editor for files of any size
    Exec={exec} %F
    Icon={icon}
    Terminal=false
    Categories=Utility;TextEditor;Development;
    MimeType=text/plain;
    StartupWMClass=Textida

    """;

static void WriteScript(string path, string text)
{
    File.WriteAllText(path, text.ReplaceLineEndings("\n"));
    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
}

static long DirectorySize(string dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

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

partial class Program
{
    // per user, no root: the program in ~/.local/opt/textida, the command in ~/.local/bin, the desktop entry and the icon under ~/.local/share
    const string InstallScript = """
        #!/bin/sh
        set -eu
        here=$(cd "$(dirname "$0")" && pwd)
        data=${XDG_DATA_HOME:-$HOME/.local/share}
        target=$HOME/.local/opt/textida
        rm -rf "$target.new"
        mkdir -p "$target.new" "$HOME/.local/bin" "$data/applications" "$data/icons/hicolor/256x256/apps"
        cp -a "$here/." "$target.new/"
        rm -f "$target.new/install.sh"
        rm -rf "$target"
        mv "$target.new" "$target"
        ln -sf "$target/Textida" "$HOME/.local/bin/textida"
        cp "$here/textida.png" "$data/icons/hicolor/256x256/apps/textida.png"
        cat > "$data/applications/textida.desktop" <<EOF
        [Desktop Entry]
        Type=Application
        Name=Textida
        GenericName=Text Editor
        Comment=Text and hex editor for files of any size
        Exec=$target/Textida %F
        Icon=textida
        Terminal=false
        Categories=Utility;TextEditor;Development;
        MimeType=text/plain;
        StartupWMClass=Textida
        EOF
        command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" || true
        echo "Textida $(cat "$target/version.txt") is installed in $target; start it from the menu or with: textida"
        case ":$PATH:" in *":$HOME/.local/bin:"*) ;; *) echo "Add $HOME/.local/bin to PATH to use the textida command." ;; esac
        """;

    // removes what install.sh wrote; the settings in ~/.config stay
    const string UninstallScript = """
        #!/bin/sh
        set -eu
        data=${XDG_DATA_HOME:-$HOME/.local/share}
        rm -rf "$HOME/.local/opt/textida"
        rm -f "$HOME/.local/bin/textida" "$data/applications/textida.desktop" "$data/icons/hicolor/256x256/apps/textida.png"
        command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" || true
        echo "Textida is removed. The settings stay in the profile folder."
        """;
}
