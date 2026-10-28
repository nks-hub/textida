using System.Diagnostics;
using System.IO.Compression;
using NksHub.Textida.Install;

/// <summary>
/// Checks TextidaSetup.exe (<c>--setup EXE</c>): a native image for the Windows GUI with its resources, the package inside it (with <c>--expect-version V</c> the version it announces), the native libraries of the window,
/// and the copy without the package that becomes the uninstaller. With <c>--run-setup</c> it also starts the program with <c>--check</c>, which opens no window and writes nothing, and expects it to report the package.
/// </summary>
internal static class SetupChecks
{
    public static void Run(string[] args, Action<bool, string> check)
    {
        int at = Array.IndexOf(args, "--setup");
        if (at < 0) return;
        var setup = args[at + 1];
        check(File.Exists(setup), "setup: " + setup + " exists");
        if (!File.Exists(setup)) return;

        var (native, gui, resources) = Inspect(setup);
        check(native, "setup: is a native image, not a managed program");
        check(gui, "setup: is a Windows GUI program");
        check(resources, "setup: carries resources (icon, manifest)");

        using (var package = PayloadFile.Open(setup))
        {
            check(package is not null, "setup: carries the package");
            if (package is not null)
            {
                using var zip = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
                foreach (var file in new[] { "Textida.exe", "Setup.ps1", "version.txt" }) check(zip.GetEntry(file) is not null, "setup: package has " + file);
                var version = zip.GetEntry("version.txt") is { } entry ? new StreamReader(entry.Open()).ReadToEnd().Trim() : "";
                int expected = Array.IndexOf(args, "--expect-version");
                if (expected >= 0) check(version == args[expected + 1], $"setup: package version {version} is {args[expected + 1]}");
            }
        }

        using (var natives = PayloadFile.Open(setup, PayloadKind.Natives))
        {
            check(natives is not null, "setup: carries the native libraries");
            if (natives is not null)
            {
                using var zip = new ZipArchive(natives, ZipArchiveMode.Read, leaveOpen: true);
                foreach (var file in new[] { "libSkiaSharp.dll", "libHarfBuzzSharp.dll" }) check(zip.GetEntry(file) is not null, "setup: natives have " + file);
            }
        }

        var slim = Path.Combine(Path.GetTempPath(), "textida-setup-slim-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            PayloadFile.CopyWithoutPayload(setup, slim);
            check(!PayloadFile.HasPayload(slim) && PayloadFile.HasPayload(slim, PayloadKind.Natives), "setup: the uninstaller copy has no package and keeps the native libraries");
            check(new FileInfo(slim).Length < new FileInfo(setup).Length, "setup: the uninstaller copy is smaller");
        }
        finally { File.Delete(slim); }

        if (!args.Contains("--run-setup")) return;
        using var process = Process.Start(new ProcessStartInfo(setup, "--check") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        bool exited = process.WaitForExit(60_000);
        if (!exited) process.Kill(true);
        check(exited && process.ExitCode == 0 && output.Result.Contains("package "), $"setup: --check reports the package (exit {(exited ? process.ExitCode : -1)}) {output.Result.Trim()} {errors.Result.Trim()}");
    }

    /// <summary>Reads the PE headers: no CLR header means a native image, subsystem 2 is a Windows GUI program, a resource table holds the icon and the manifest.</summary>
    private static (bool Native, bool Gui, bool Resources) Inspect(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        file.Position = 0x3C;
        long pe = reader.ReadInt32();
        file.Position = pe;
        if (reader.ReadUInt32() != 0x00004550) return (false, false, false); // "PE\0\0"
        long optional = pe + 24;
        file.Position = optional;
        if (reader.ReadUInt16() != 0x20B) return (false, false, false); // PE32+
        file.Position = optional + 68;
        bool gui = reader.ReadUInt16() == 2;
        file.Position = optional + 112 + 2 * 8; // data directory 2: resources
        bool resources = reader.ReadUInt32() != 0;
        file.Position = optional + 112 + 14 * 8; // data directory 14: CLR header
        bool native = reader.ReadUInt32() == 0;
        return (native, gui, resources);
    }
}
