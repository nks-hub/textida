using System.Runtime.InteropServices;
using NksHub.Textida.Accessibility.MacOS;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Moves a file or a folder to the system recycle bin (Windows) or the Trash (macOS), where the user can put it back. It never deletes permanently:
/// when the system cannot recycle the item the operation fails and the item stays where it was.
/// </summary>
internal static unsafe partial class RecycleBin
{
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    /// <summary>Throws <see cref="FileOperationException"/> with the reason when the item could not be recycled.</summary>
    public static void Move(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileOperationException(FileFailure.NotFound, path, path);
        if (OperatingSystem.IsWindows()) MoveWindows(path);
        else if (OperatingSystem.IsMacOS()) MoveMac(path);
        else throw new FileOperationException(FileFailure.Unsupported, path, path);
    }

    // ---- Windows: SHFileOperation with FOF_ALLOWUNDO ----

    private const uint FoDelete = 3;
    private const ushort Silent = 0x4, NoConfirmation = 0x10, AllowUndo = 0x40, NoErrorUi = 0x400, WantNukeWarning = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public nint Hwnd;
        public uint Function;
        public nint From;
        public nint To;
        public ushort Flags;
        public int AnyOperationsAborted;
        public nint NameMappings;
        public nint ProgressTitle;
    }

    [LibraryImport("shell32.dll", EntryPoint = "SHFileOperationW")]
    private static partial int ShFileOperation(ref ShFileOpStruct operation);

    private static void MoveWindows(string path)
    {
        // the list of names ends with two null characters; StringToHGlobalUni adds the second one
        nint from = Marshal.StringToHGlobalUni(path + "\0");
        try
        {
            var op = new ShFileOpStruct { Function = FoDelete, From = from, Flags = (ushort)(AllowUndo | NoConfirmation | NoErrorUi | Silent | WantNukeWarning) };
            int code = ShFileOperation(ref op);
            if (code == 0 && op.AnyOperationsAborted == 0 && !File.Exists(path) && !Directory.Exists(path)) return;
            throw new FileOperationException(ClassifyShell(code), path, $"0x{code:X}");
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    /// <summary>The shell reports its own DE_* codes next to Win32 errors.</summary>
    private static FileFailure ClassifyShell(int code) => code switch
    {
        0x20 or 0x21 or 0x78 => FileFailure.InUse, // sharing and lock violations, DE_SHARING
        0x05 or 0x74 or 0x75 or 0x76 or 0x77 => FileFailure.AccessDenied,
        0x02 or 0x03 or 0x7C or 0x402 => FileFailure.NotFound,
        0x70 => FileFailure.DiskFull,
        0 => FileFailure.InUse, // aborted by the user or the system: the item is still there
        _ => FileFailure.Other,
    };

    // ---- macOS: NSFileManager trashItemAtURL ----

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static void MoveMac(string path)
    {
        ObjC.LoadFramework("Foundation");
        nint pool = ObjC.Msg(ObjC.Msg(ObjC.Class("NSAutoreleasePool"), "alloc"), "init");
        try
        {
            nint manager = ObjC.Msg(ObjC.Class("NSFileManager"), "defaultManager");
            nint url = ObjC.Msg(ObjC.Class("NSURL"), "fileURLWithPath:", ObjC.NewString(path));
            nint error = 0;
            nint result = ObjC.Send(manager, ObjC.Sel("trashItemAtURL:resultingItemURL:error:"), url, 0, (nint)(&error));
            if ((result & 0xFF) != 0) return;
            string reason = error == 0 ? path : ObjC.ToManaged(ObjC.Msg(error, "localizedDescription"));
            throw new FileOperationException(File.Exists(path) || Directory.Exists(path) ? FileFailure.Other : FileFailure.NotFound, path, reason);
        }
        finally
        {
            ObjC.Msg(pool, "drain");
        }
    }
}
