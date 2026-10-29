using System.Diagnostics;
using System.Xml.Linq;
using NksHub.Textida.Install;

/// <summary>
/// Checks a macOS package (<c>--package Textida.zip</c> or <c>--package Textida.dmg</c>, both made by tools/BuildMacPackage.cs): <c>Textida.app</c> with a valid <c>Info.plist</c> (identifier, executable, icon,
/// oldest system 14.0, document types, with <c>--expect-version V</c> the version), the icon file, the files of a publish (the program in <c>Contents/MacOS</c>, nothing there but code, every built-in plugin and <c>textida-mcp</c> in <c>Contents/Resources</c> where the program looks for
/// them, started on a Mac), a minimum system version of at most 14 in every Mach-O file, and, on a Mac, an ad-hoc signature that verifies. The disk image also needs its link to Applications.
/// </summary>
internal static class MacPackageChecks
{
    public const string AppFolder = "Textida.app";

    /// <summary>Checks the package that was unpacked into <paramref name="root"/>, which holds <c>Textida.app</c>.</summary>
    public static void Run(string root, string[] args, Action<bool, string> check)
    {
        var app = Path.Combine(root, AppFolder);
        check(Directory.Exists(app), "mac package: " + AppFolder);
        if (!Directory.Exists(app)) return;
        var contents = Path.Combine(app, "Contents");
        var macos = Path.Combine(contents, "MacOS");
        InfoPlist(contents, args, check);
        check(Icon(Path.Combine(contents, "Resources", MacBundle.IconName + ".icns")), "mac package: Contents/Resources/" + MacBundle.IconName + ".icns is an icon file");

        // the same files and starts as for a publish folder: the program in Contents/MacOS, every plugin and textida-mcp in Contents/Resources, where PluginServices and McpService look for them in a bundle
        var resources = Path.Combine(contents, "Resources");
        PublishChecks.Tree(macos, resources, args.Contains("--symbols"), startPrograms: OperatingSystem.IsMacOS(), check);
        if (!OperatingSystem.IsMacOS()) Console.WriteLine("mac package: the programs are not started, this is not a Mac");
        // nothing but code may stand in Contents/MacOS, or the signature of the bundle is refused
        foreach (var file in Directory.EnumerateFiles(macos, "*", SearchOption.AllDirectories))
            check(MachO.MinimumSystems(file).Count > 0, "mac package: Contents/MacOS/" + Path.GetRelativePath(macos, file) + " is not a program or a library");
        check(File.Exists(Path.Combine(resources, "version.txt")), "mac package: Contents/Resources/version.txt");

        int machO = 0;
        foreach (var file in Directory.EnumerateFiles(contents, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(contents, file);
            var systems = MachO.MinimumSystems(file);
            if (systems.Count == 0) continue;
            machO++;
            foreach (var (architecture, platform, major, minor) in systems)
                check(platform == 1 && major <= MacBundle.MinimumMajor, $"mac package: {relative} ({architecture}) asks for platform {platform}, macOS {major}.{minor}; at most macOS {MacBundle.MinimumMajor} is allowed");
        }
        check(machO >= 12, $"mac package: only {machO} Mach-O files found in Contents");

        var program = Path.Combine(macos, MacBundle.ExecutableName);
        if (!OperatingSystem.IsWindows() && File.Exists(program)) check((File.GetUnixFileMode(program) & UnixFileMode.UserExecute) != 0, "mac package: Contents/MacOS/" + MacBundle.ExecutableName + " is executable");

        if (!OperatingSystem.IsMacOS()) return;
        var (verified, verifyText) = Tool("codesign", "--verify", "--deep", "--strict", app);
        check(verified == 0, "mac package: the signature verifies: " + verifyText.Trim());
        var (_, details) = Tool("codesign", "-dv", "--verbose=2", app);
        check(details.Contains("Signature=adhoc", StringComparison.Ordinal), "mac package: the signature is ad-hoc: " + details.Trim());
    }

    /// <summary>Checks a disk image: it is attached read-only to a folder of its own, and the application in it is checked like the one in the ZIP.</summary>
    public static void RunDiskImage(string image, string[] args, Action<bool, string> check)
    {
        if (!OperatingSystem.IsMacOS())
        {
            check(false, "mac package: a disk image can be opened only on macOS");
            return;
        }
        var mount = Path.Combine(Path.GetTempPath(), "textida-dmg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mount);
        var (code, text) = Tool("hdiutil", "attach", "-readonly", "-nobrowse", "-noverify", "-noautoopen", "-mountpoint", mount, image);
        check(code == 0, "mac package: the disk image attaches: " + text.Trim());
        if (code != 0) return;
        try
        {
            check(new FileInfo(Path.Combine(mount, "Applications")).LinkTarget == "/Applications", "mac package: the disk image has a link to /Applications");
            Run(mount, args, check);
        }
        finally
        {
            Tool("hdiutil", "detach", "-force", mount);
            try { Directory.Delete(mount); }
            catch (IOException) { /* still being detached; the temporary folder is empty */ }
        }
    }

    private static void InfoPlist(string contents, string[] args, Action<bool, string> check)
    {
        var path = Path.Combine(contents, "Info.plist");
        check(File.Exists(path), "mac package: Contents/Info.plist");
        if (!File.Exists(path)) return;
        if (OperatingSystem.IsMacOS())
        {
            var (code, text) = Tool("plutil", "-lint", path);
            check(code == 0, "mac package: Info.plist is a valid property list: " + text.Trim());
        }
        Dictionary<string, string> values;
        XElement dict;
        try
        {
            dict = XDocument.Load(path).Root!.Element("dict")!;
            values = Strings(dict);
        }
        catch (Exception e) when (e is System.Xml.XmlException or NullReferenceException)
        {
            check(false, "mac package: Info.plist cannot be read: " + e.Message);
            return;
        }
        void Expect(string key, string expected) => check(values.GetValueOrDefault(key) == expected, $"mac package: Info.plist {key} is {values.GetValueOrDefault(key) ?? "missing"}, expected {expected}");
        Expect("CFBundleExecutable", MacBundle.ExecutableName);
        Expect("CFBundleIdentifier", MacBundle.BundleId);
        Expect("CFBundlePackageType", "APPL");
        Expect("CFBundleIconFile", MacBundle.IconName);
        Expect("LSMinimumSystemVersion", MacBundle.MinimumMajor + ".0");
        check(System.Text.RegularExpressions.Regex.IsMatch(values.GetValueOrDefault("CFBundleShortVersionString") ?? "", @"^\d+(\.\d+){1,2}$"), "mac package: Info.plist version is numbers and dots");
        int expected = Array.IndexOf(args, "--expect-version");
        if (expected >= 0) Expect("CFBundleShortVersionString", MacBundle.BundleVersion(args[expected + 1]));
        check(dict.Elements().SkipWhile(e => e.Value != "NSHighResolutionCapable").Skip(1).FirstOrDefault()?.Name == "true", "mac package: Info.plist NSHighResolutionCapable is true");
        check(dict.Elements().SkipWhile(e => e.Value != "CFBundleDocumentTypes").Skip(1).FirstOrDefault()?.Elements().Any() == true, "mac package: Info.plist has document types");
    }

    /// <summary>The keys of a property list dictionary whose value is a string.</summary>
    private static Dictionary<string, string> Strings(XElement dict)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var items = dict.Elements().ToList();
        for (int i = 0; i + 1 < items.Count; i += 2)
            if (items[i].Name == "key" && items[i + 1].Name == "string") values[items[i].Value] = items[i + 1].Value;
        return values;
    }

    /// <summary>An ICNS file: "icns", the total length, then entries that fill it exactly, with the sizes of the icon (16 to 1024 and the @2x ones).</summary>
    private static bool Icon(string path)
    {
        if (!File.Exists(path)) return false;
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 16 || bytes[0] != 'i' || bytes[1] != 'c' || bytes[2] != 'n' || bytes[3] != 's' || ReadBigEndian(bytes, 4) != bytes.Length) return false;
        var types = new List<string>();
        for (int at = 8; at + 8 <= bytes.Length;)
        {
            int length = ReadBigEndian(bytes, at + 4);
            if (length < 8 || at + length > bytes.Length) return false;
            types.Add(System.Text.Encoding.ASCII.GetString(bytes, at, 4));
            at += length;
        }
        return new[] { "icp4", "icp5", "icp6", "ic07", "ic08", "ic09", "ic10", "ic11", "ic12", "ic13", "ic14" }.All(types.Contains);
    }

    private static int ReadBigEndian(byte[] bytes, int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];

    private static (int Code, string Text) Tool(string program, params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(program, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.Result + errors.Result);
    }
}

/// <summary>Reads the minimum system version a Mach-O file asks for, in each of its architectures (a fat file holds several).</summary>
internal static class MachO
{
    private const uint ThinMagic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint LcBuildVersion = 0x32;
    private const uint LcVersionMinMacOs = 0x24;

    /// <summary>The architecture, the platform (1 is macOS) and the oldest system of each architecture of the file; empty when the file is no Mach-O.</summary>
    public static IReadOnlyList<(string Architecture, uint Platform, int Major, int Minor)> MinimumSystems(string path)
    {
        var found = new List<(string, uint, int, int)>();
        try
        {
            using var file = File.OpenRead(path);
            if (file.Length < 32) return found;
            Span<byte> head = stackalloc byte[8];
            file.ReadExactly(head);
            uint big = (uint)((head[0] << 24) | (head[1] << 16) | (head[2] << 8) | head[3]);
            if (big == FatMagic)
            {
                int count = (head[4] << 24) | (head[5] << 16) | (head[6] << 8) | head[7];
                if (count is < 1 or > 8) return found;
                Span<byte> arch = stackalloc byte[20];
                var offsets = new List<long>();
                for (int i = 0; i < count; i++)
                {
                    file.ReadExactly(arch);
                    offsets.Add((uint)((arch[8] << 24) | (arch[9] << 16) | (arch[10] << 8) | arch[11]));
                }
                foreach (var offset in offsets) Thin(file, offset, found);
            }
            else if (BitConverter.ToUInt32(head) == ThinMagic64) Thin(file, 0, found);
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or UnauthorizedAccessException) { /* not a file to look into */ }
        return found;
    }

    private static void Thin(FileStream file, long offset, List<(string, uint, int, int)> found)
    {
        var header = new byte[32];
        file.Position = offset;
        file.ReadExactly(header);
        if (BitConverter.ToUInt32(header, 0) != ThinMagic64) return;
        uint cpu = BitConverter.ToUInt32(header, 4);
        string architecture = cpu switch { 0x0100000C => "arm64", 0x01000007 => "x86_64", _ => "cpu " + cpu.ToString("X") };
        uint commands = BitConverter.ToUInt32(header, 16);
        long at = offset + 32;
        for (uint i = 0; i < commands; i++)
        {
            var command = new byte[24];
            file.Position = at;
            int read = file.Read(command, 0, command.Length);
            if (read < 8) return;
            uint kind = BitConverter.ToUInt32(command, 0), size = BitConverter.ToUInt32(command, 4);
            if (kind == LcBuildVersion && read >= 16)
            {
                uint minos = BitConverter.ToUInt32(command, 12);
                found.Add((architecture, BitConverter.ToUInt32(command, 8), (int)(minos >> 16), (int)((minos >> 8) & 0xFF)));
                return;
            }
            if (kind == LcVersionMinMacOs && read >= 12)
            {
                uint version = BitConverter.ToUInt32(command, 8);
                found.Add((architecture, 1, (int)(version >> 16), (int)((version >> 8) & 0xFF)));
                return;
            }
            if (size < 8) return;
            at += size;
        }
    }
}
