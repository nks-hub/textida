using System.IO.Compression;
using NksHub.Textida.Install;
using NksHub.Textida.Setup;

namespace NksHub.Textida.Setup.Tests;

/// <summary>The ZIPs inside the setup program: the package and the native libraries of the window, and how the libraries are unpacked.</summary>
public class NativeBundleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "textida-native-test-" + Guid.NewGuid().ToString("N"));

    public NativeBundleTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private string Write(string name, byte[] content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string ZipOf(string name, params (string Entry, string Content)[] files)
    {
        var path = Path.Combine(_root, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var (entry, content) in files)
                using (var writer = new StreamWriter(zip.CreateEntry(entry).Open())) writer.Write(content);
        return path;
    }

    private static string Entries(Stream zip)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return string.Join(",", archive.Entries.Select(e => e.FullName).Order());
    }

    // ---- two kinds of ZIP in one program ----

    [Fact]
    public void A_program_carries_a_package_and_the_native_libraries_and_each_is_read_back()
    {
        var program = Write("program.exe", "PROGRAM"u8.ToArray());
        var withNatives = Path.Combine(_root, "step1.exe");
        PayloadFile.Append(program, ZipOf("natives.zip", ("libSkiaSharp.dll", "skia"), ("libHarfBuzzSharp.dll", "hb")), withNatives, PayloadKind.Natives);
        var setup = Path.Combine(_root, "setup.exe");
        PayloadFile.Append(withNatives, ZipOf("package.zip", ("Textida.exe", "app")), setup);

        using (var package = PayloadFile.Open(setup)!) Assert.Equal("Textida.exe", Entries(package));
        using (var natives = PayloadFile.Open(setup, PayloadKind.Natives)!) Assert.Equal("libHarfBuzzSharp.dll,libSkiaSharp.dll", Entries(natives));
        Assert.StartsWith("PROGRAM", File.ReadAllText(setup));
    }

    [Fact]
    public void The_uninstaller_copy_loses_the_package_and_keeps_the_native_libraries_it_needs_to_show_its_window()
    {
        var withNatives = Path.Combine(_root, "step1.exe");
        PayloadFile.Append(Write("program.exe", "PROGRAM"u8.ToArray()), ZipOf("natives.zip", ("libSkiaSharp.dll", "skia")), withNatives, PayloadKind.Natives);
        var setup = Path.Combine(_root, "setup.exe");
        PayloadFile.Append(withNatives, ZipOf("package.zip", ("Textida.exe", "app")), setup);
        var slim = Path.Combine(_root, "slim.exe");

        PayloadFile.CopyWithoutPayload(setup, slim);

        Assert.False(PayloadFile.HasPayload(slim));
        Assert.True(PayloadFile.HasPayload(slim, PayloadKind.Natives));
        Assert.Equal(File.ReadAllBytes(withNatives), File.ReadAllBytes(slim));
    }

    [Fact]
    public void Appending_one_kind_again_replaces_that_kind_and_keeps_the_other()
    {
        var program = Write("program.exe", "PROGRAM"u8.ToArray());
        var one = Path.Combine(_root, "one.exe");
        PayloadFile.Append(program, ZipOf("n1.zip", ("a.dll", "1")), one, PayloadKind.Natives);
        var two = Path.Combine(_root, "two.exe");
        PayloadFile.Append(one, ZipOf("p1.zip", ("old.txt", "1")), two);
        var three = Path.Combine(_root, "three.exe");
        PayloadFile.Append(two, ZipOf("p2.zip", ("new.txt", "2")), three);
        var four = Path.Combine(_root, "four.exe");
        PayloadFile.Append(three, ZipOf("n2.zip", ("b.dll", "2")), four, PayloadKind.Natives);

        using (var package = PayloadFile.Open(four)!) Assert.Equal("new.txt", Entries(package));
        using (var natives = PayloadFile.Open(four, PayloadKind.Natives)!) Assert.Equal("b.dll", Entries(natives));
        Assert.StartsWith("PROGRAM", File.ReadAllText(four));
    }

    [Fact]
    public void A_program_without_a_kind_gives_null_for_it()
    {
        var onlyNatives = Path.Combine(_root, "n.exe");
        PayloadFile.Append(Write("program.exe", "PROGRAM"u8.ToArray()), ZipOf("n.zip", ("a.dll", "1")), onlyNatives, PayloadKind.Natives);

        Assert.Null(PayloadFile.Open(onlyNatives));
        Assert.False(PayloadFile.HasPayload(onlyNatives));
        Assert.NotNull(PayloadFile.Open(onlyNatives, PayloadKind.Natives));
    }

    // ---- unpacking ----

    [Fact]
    public void The_libraries_are_unpacked_to_a_folder_named_after_their_content_and_left_alone_the_second_time()
    {
        var zip = ZipOf("natives.zip", ("libSkiaSharp.dll", "skia bytes"), ("libHarfBuzzSharp.dll", "hb bytes"));
        var target = Path.Combine(_root, "unpacked");

        string first, second;
        using (var stream = File.OpenRead(zip)) first = NativeBundle.Extract(stream, target);
        var written = File.GetLastWriteTimeUtc(Path.Combine(first, "libSkiaSharp.dll"));
        Thread.Sleep(30);
        using (var stream = File.OpenRead(zip)) second = NativeBundle.Extract(stream, target);

        Assert.Equal(first, second);
        Assert.StartsWith(target, first);
        Assert.Equal("skia bytes", File.ReadAllText(Path.Combine(first, "libSkiaSharp.dll")));
        Assert.Equal("hb bytes", File.ReadAllText(Path.Combine(first, "libHarfBuzzSharp.dll")));
        Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(first, "libSkiaSharp.dll")));
    }

    [Fact]
    public void Another_version_of_the_libraries_goes_to_another_folder()
    {
        string a, b;
        using (var stream = File.OpenRead(ZipOf("a.zip", ("libSkiaSharp.dll", "version 1")))) a = NativeBundle.Extract(stream, Path.Combine(_root, "unpacked"));
        using (var stream = File.OpenRead(ZipOf("b.zip", ("libSkiaSharp.dll", "version 2")))) b = NativeBundle.Extract(stream, Path.Combine(_root, "unpacked"));

        Assert.NotEqual(a, b);
        Assert.Equal("version 1", File.ReadAllText(Path.Combine(a, "libSkiaSharp.dll")));
        Assert.Equal("version 2", File.ReadAllText(Path.Combine(b, "libSkiaSharp.dll")));
    }

    [Fact]
    public void An_entry_name_with_folders_is_flattened_and_cannot_leave_the_folder()
    {
        string folder;
        using (var stream = File.OpenRead(ZipOf("evil.zip", ("..\\..\\escaped.dll", "x")))) folder = NativeBundle.Extract(stream, Path.Combine(_root, "unpacked"));

        Assert.True(File.Exists(Path.Combine(folder, "escaped.dll")));
        Assert.False(File.Exists(Path.Combine(_root, "escaped.dll")));
    }

    [Fact]
    public void A_program_without_the_bundle_uses_what_is_next_to_it()
    {
        Assert.Null(NativeBundle.Prepare(Write("plain.exe", "PROGRAM"u8.ToArray()), Path.Combine(_root, "unpacked")));
        Assert.Null(NativeBundle.Prepare(null));
        Assert.False(Directory.Exists(Path.Combine(_root, "unpacked")));
    }
}
