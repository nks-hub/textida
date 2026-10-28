using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup;

/// <summary>
/// The setup window needs the native libraries of Skia and HarfBuzz, which a NativeAOT program does not contain. They travel inside TextidaSetup.exe (<see cref="PayloadKind.Natives"/>), so the user still downloads one file.
/// Before the window starts they are unpacked to a folder in the temp folder named after their content and loaded from there; Skia and HarfBuzz are told to take these modules, not whatever library
/// of the same name lies next to the program (the uninstaller runs from the folder of Textida.exe, which has libraries of its own).
/// A program without the bundle (a publish folder, the tests) uses the libraries next to it.
/// </summary>
internal static partial class NativeBundle
{
    public static string DefaultRoot => Path.Combine(Path.GetTempPath(), "TextidaSetup");

    /// <summary>Makes the libraries the program carries the ones Skia and HarfBuzz use; does nothing for a program that carries none. Returns the folder they were unpacked to, or null.</summary>
    public static string? Prepare(string? program, string? root = null)
    {
        if (program is null) return null;
        using var blob = PayloadFile.Open(program, PayloadKind.Natives);
        if (blob is null) return null;
        var folder = Extract(blob, root ?? DefaultRoot);
        SetDllDirectory(folder); // a library that is asked for by name and not through the resolver is found here too
        var loaded = new Dictionary<string, nint>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in Directory.GetFiles(folder, "*.dll"))
            if (NativeLibrary.TryLoad(library, out var handle)) loaded[Path.GetFileNameWithoutExtension(library)] = handle;

        nint Resolve(string name, Assembly assembly, DllImportSearchPath? path) => loaded.GetValueOrDefault(Path.GetFileNameWithoutExtension(name));
        NativeLibrary.SetDllImportResolver(typeof(SkiaSharp.SKCanvas).Assembly, Resolve);
        NativeLibrary.SetDllImportResolver(typeof(HarfBuzzSharp.Blob).Assembly, Resolve);
        return folder;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetDllDirectory(string path);

    /// <summary>Unpacks the libraries into a folder below <paramref name="root"/> named after the hash of the ZIP, so another version never meets these files; files that are already there with the same size are kept.</summary>
    public static string Extract(Stream zipStream, string root)
    {
        var folder = Path.Combine(root, Convert.ToHexString(SHA256.HashData(zipStream))[..16]);
        zipStream.Position = 0;
        Directory.CreateDirectory(folder);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0))
        {
            var path = Path.Combine(folder, entry.Name); // flat: the libraries have no folders, and a name from the ZIP cannot leave this one
            if (File.Exists(path) && new FileInfo(path).Length == entry.Length) continue; // already there, maybe in use by a setup that is running
            entry.ExtractToFile(path, overwrite: true);
        }
        return folder;
    }
}
