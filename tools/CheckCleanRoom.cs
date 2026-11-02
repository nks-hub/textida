// Fails when the repository or a release package carries anything from the reverse-engineering folder re/ or from the PSPad distribution:
// no tracked path may lie under re/, and no tracked file or file inside a package (.zip, .tar.gz) may have the SHA-256 of a PSPad file.
// tools/pspad-dist.sha256 holds only the hashes; --update makes it from the original archives in re/dl, which stay on the developer's computer.
//
//   dotnet run tools/CheckCleanRoom.cs                  check the tracked files
//   dotnet run tools/CheckCleanRoom.cs -- --scan DIR    also check every .zip and .tar.gz under DIR
//   dotnet run tools/CheckCleanRoom.cs -- --update      rebuild the hash list from re/dl/*.zip
//   dotnet run tools/CheckCleanRoom.cs -- --selftest
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

// tiny files (empty files, one-line configs) match other projects by chance; such a match proves nothing
const int MinBytes = 64;

var tools = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Path.GetFullPath("tools");
var root = Path.GetFullPath(Path.Combine(tools, ".."));
var hashFile = Path.Combine(tools, "pspad-dist.sha256");

if (args.Contains("--update")) return Update();
if (args.Contains("--selftest")) return SelfTest();

var scan = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--scan" && i + 1 < args.Length) scan.Add(args[++i]);
    else
    {
        Console.Error.WriteLine($"CheckCleanRoom: unknown argument {args[i]}.");
        return 2;
    }
}
var problems = Check(root, LoadHashes(), scan);
foreach (var p in problems) Console.Error.WriteLine(p);
Console.WriteLine($"CheckCleanRoom: {problems.Count} problems.");
return problems.Count == 0 ? 0 : 1;

static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

Dictionary<string, string> LoadHashes()
{
    var known = new Dictionary<string, string>();
    foreach (var line in File.ReadLines(hashFile))
    {
        if (line.Length == 0 || line.StartsWith('#')) continue;
        int space = line.IndexOf(' ');
        known[line[..space]] = line[(space + 1)..].Trim();
    }
    return known;
}

int Update()
{
    var archives = Directory.Exists(Path.Combine(root, "re", "dl")) ? Directory.GetFiles(Path.Combine(root, "re", "dl"), "*.zip").Order(StringComparer.Ordinal).ToArray() : [];
    if (archives.Length == 0)
    {
        Console.Error.WriteLine("CheckCleanRoom: no archives in re/dl.");
        return 2;
    }
    var known = new Dictionary<string, string>();
    foreach (var archive in archives)
    {
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries.Where(e => e.Length >= MinBytes && !e.FullName.EndsWith('/')))
            known.TryAdd(Sha(Read(entry.Open())), $"{Path.GetFileName(archive)}:{entry.FullName.Replace('\\', '/')}");
    }
    var text = new StringBuilder("# SHA-256 of files from the PSPad distribution; made by tools/CheckCleanRoom.cs --update\n");
    foreach (var (digest, name) in known.OrderBy(kv => kv.Value, StringComparer.Ordinal)) text.Append(digest).Append(' ').Append(name).Append('\n');
    File.WriteAllText(hashFile, text.ToString());
    Console.WriteLine($"CheckCleanRoom: {known.Count} hashes from {archives.Length} archives.");
    return 0;
}

static byte[] Read(Stream stream)
{
    using (stream)
    {
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

static List<string> TrackedFiles(string repo)
{
    var git = Process.Start(new ProcessStartInfo("git", ["-C", repo, "ls-files", "-z"]) { RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8 })!;
    var output = git.StandardOutput.ReadToEnd();
    git.WaitForExit();
    if (git.ExitCode != 0) throw new InvalidOperationException($"git ls-files failed with {git.ExitCode}");
    return [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries)];
}

static IEnumerable<(string Name, byte[] Data)> PackageMembers(string path)
{
    var file = Path.GetFileName(path);
    if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
    {
        using var zip = ZipFile.OpenRead(path);
        foreach (var entry in zip.Entries.Where(e => !e.FullName.EndsWith('/')))
            yield return ($"{file}:{entry.FullName}", Read(entry.Open()));
    }
    else if (file.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
    {
        using var tar = new TarReader(new GZipStream(File.OpenRead(path), CompressionMode.Decompress));
        while (tar.GetNextEntry() is { } entry)
            if (entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.DataStream is { } data)
                yield return ($"{file}:{entry.Name}", Read(data));
    }
}

static List<string> Check(string repo, Dictionary<string, string> known, IEnumerable<string> scan)
{
    var problems = new List<string>();
    foreach (var rel in TrackedFiles(repo))
    {
        if (rel == "re" || rel.StartsWith("re/", StringComparison.Ordinal))
        {
            problems.Add($"tracked path under re/: {rel}");
            continue;
        }
        var path = Path.Combine(repo, rel);
        if (!File.Exists(path)) continue; // deleted in the working tree, or a submodule
        var data = File.ReadAllBytes(path);
        if (data.Length >= MinBytes && known.TryGetValue(Sha(data), out var origin)) problems.Add($"{rel} is {origin}");
    }
    foreach (var folder in scan)
        foreach (var package in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            foreach (var (name, data) in PackageMembers(package))
                if (data.Length >= MinBytes && known.TryGetValue(Sha(data), out var origin)) problems.Add($"{name} is {origin}");
    return problems;
}

static int SelfTest()
{
    var repo = Directory.CreateTempSubdirectory("clean-room-").FullName;
    try
    {
        var foreign = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("PSPad configuration that must never be shipped\n", 4)));
        var tiny = "[x]\n"u8.ToArray();
        Process.Start("git", ["init", "-q", repo])!.WaitForExit();
        File.WriteAllBytes(Path.Combine(repo, "ok.txt"), Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("own text ", 20))));
        File.WriteAllBytes(Path.Combine(repo, "tiny.ini"), tiny);
        File.WriteAllBytes(Path.Combine(repo, "copied.ini"), foreign);
        Directory.CreateDirectory(Path.Combine(repo, "re"));
        File.WriteAllText(Path.Combine(repo, "re", "note.txt"), "x");
        Process.Start("git", ["-C", repo, "-c", "core.autocrlf=false", "add", "-A"])!.WaitForExit();

        var dist = Directory.CreateDirectory(Path.Combine(repo, "dist")).FullName;
        using (var zip = ZipFile.Open(Path.Combine(dist, "app.zip"), ZipArchiveMode.Create))
        {
            using (var s = zip.CreateEntry("app/own.dll").Open()) s.Write(new byte[100]);
            using (var s = zip.CreateEntry("app/copied.ini").Open()) s.Write(foreign);
        }
        using (var gz = new GZipStream(File.Create(Path.Combine(dist, "app.tar.gz")), CompressionLevel.Fastest))
        using (var tar = new TarWriter(gz))
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "app/copied.ini") { DataStream = new MemoryStream(foreign) });

        var known = new Dictionary<string, string> { [Sha(foreign)] = "pspad.zip:copied.ini", [Sha(tiny)] = "pspad.zip:tiny.ini" };
        var got = Check(repo, known, [dist]).Order(StringComparer.Ordinal).ToList();
        string[] want =
        [
            "app.tar.gz:app/copied.ini is pspad.zip:copied.ini",
            "app.zip:app/copied.ini is pspad.zip:copied.ini",
            "copied.ini is pspad.zip:copied.ini",
            "tracked path under re/: re/note.txt",
        ];
        if (!got.SequenceEqual(want))
        {
            Console.Error.WriteLine("CheckCleanRoom: selftest failed:\n  " + string.Join("\n  ", got));
            return 1;
        }
        Console.WriteLine("CheckCleanRoom: selftest ok.");
        return 0;
    }
    finally
    {
        foreach (var f in Directory.EnumerateFiles(repo, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); // git marks its objects read-only
        Directory.Delete(repo, true);
    }
}
