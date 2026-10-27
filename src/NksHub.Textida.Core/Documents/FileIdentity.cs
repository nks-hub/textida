using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.Core.Documents;

/// <summary>What the OS calls the file itself (volume and file index), so a file replaced by another with equal size and time is told apart.</summary>
internal static partial class FileIdentity
{
    /// <summary>Null when the file cannot be opened or the platform gives no identity (only Windows does here).</summary>
    public static string? TryGet(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out var info) ? $"{info.VolumeSerialNumber:x}:{info.FileIndexHigh:x}{info.FileIndexLow:x8}" : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation info);
}
