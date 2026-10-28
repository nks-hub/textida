using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace NksHub.Textida.Install;

/// <summary>
/// Reads and writes <c>.lnk</c> files through the shell link object of Windows. The COM calls run on a thread of their own that is a single-threaded apartment,
/// because the shell link object lives there, whatever apartment the caller is in.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class ShortcutFile
{
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");

    /// <summary>Writes the shortcut, replacing a file of that name; the folder is created.</summary>
    public static void Create(string path, string target, string workingDirectory, string description)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        OnApartmentThread(() =>
        {
            var link = NewLink();
            Check(link.SetPath(target), "SetPath");
            Check(link.SetWorkingDirectory(workingDirectory), "SetWorkingDirectory");
            Check(link.SetDescription(description), "SetDescription");
            Check(link.SetIconLocation(target, 0), "SetIconLocation");
            Check(((IPersistFile)link).Save(path, true), "Save");
            return 0;
        });
    }

    /// <summary>The target of the shortcut, or null when the file is missing or is not a shortcut.</summary>
    public static unsafe string? ReadTarget(string path)
    {
        if (!File.Exists(path)) return null;
        return OnApartmentThread(() =>
        {
            var link = NewLink();
            if (((IPersistFile)link).Load(path, 0) < 0) return null;
            char* buffer = stackalloc char[1024];
            return link.GetPath(buffer, 1024, 0, 4) < 0 ? null : new string(buffer); // 4 = SLGP_RAWPATH: the stored path, not a resolved one
        });
    }

    private static IShellLinkW NewLink()
    {
        Check(CoCreateInstance(ShellLinkClass, 0, 1, ShellLinkInterface, out var instance), "CoCreateInstance");
        try { return (IShellLinkW)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance); }
        finally { Marshal.Release(instance); }
    }

    private static void Check(int hresult, string call)
    {
        if (hresult < 0) throw new IOException($"The shortcut could not be written ({call} failed with 0x{hresult:X8}).");
    }

    private static T OnApartmentThread<T>(Func<T> work)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var initialized = CoInitializeEx(0, 2) >= 0; // 2 = COINIT_APARTMENTTHREADED
            try { result = work(); }
            catch (Exception e) { error = e; }
            finally { if (initialized) CoUninitialize(); }
        });
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);

    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, nint outer, uint context, in Guid interfaceId, out nint instance);
}

/// <summary>IShellLinkW; the order of the methods is the order of the vtable, so the ones nobody calls stay.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("000214F9-0000-0000-C000-000000000046")]
internal unsafe partial interface IShellLinkW
{
    [PreserveSig] int GetPath(char* file, int max, nint findData, uint flags);
    [PreserveSig] int GetIDList(out nint idList);
    [PreserveSig] int SetIDList(nint idList);
    [PreserveSig] int GetDescription(char* name, int max);
    [PreserveSig] int SetDescription(string name);
    [PreserveSig] int GetWorkingDirectory(char* dir, int max);
    [PreserveSig] int SetWorkingDirectory(string dir);
    [PreserveSig] int GetArguments(char* args, int max);
    [PreserveSig] int SetArguments(string args);
    [PreserveSig] int GetHotkey(out ushort hotkey);
    [PreserveSig] int SetHotkey(ushort hotkey);
    [PreserveSig] int GetShowCmd(out int showCmd);
    [PreserveSig] int SetShowCmd(int showCmd);
    [PreserveSig] int GetIconLocation(char* path, int max, out int index);
    [PreserveSig] int SetIconLocation(string path, int index);
    [PreserveSig] int SetRelativePath(string path, uint reserved);
    [PreserveSig] int Resolve(nint window, uint flags);
    [PreserveSig] int SetPath(string file);
}

/// <summary>IPersistFile with the one method of IPersist in front of its own, which is how the vtable is laid out.</summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("0000010B-0000-0000-C000-000000000046")]
internal partial interface IPersistFile
{
    [PreserveSig] int GetClassID(out Guid classId);
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load(string fileName, uint mode);
    [PreserveSig] int Save(string? fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    [PreserveSig] int SaveCompleted(string fileName);
    [PreserveSig] int GetCurFile(out nint fileName);
}
