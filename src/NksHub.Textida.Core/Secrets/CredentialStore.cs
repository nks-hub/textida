using System.Runtime.InteropServices;

namespace NksHub.Textida.Core.Secrets;

/// <summary>Where passwords and tokens live: the credential store of the operating system, never a settings file. Keys are free text such as <c>textida:remote:abc</c>.</summary>
public interface ICredentialStore
{
    string? Get(string key);
    void Set(string key, string value);
    bool Delete(string key);
}

/// <summary>Thrown when the platform has no credential store this build can use; the caller reports it instead of keeping the secret somewhere weaker.</summary>
public sealed class CredentialStoreUnavailableException(string message) : Exception(message);

public static class CredentialStores
{
    /// <summary>The store of the current platform. Windows Credential Manager is implemented; macOS Keychain and the Linux secret service are not, and the returned store throws <see cref="CredentialStoreUnavailableException"/>.</summary>
    public static ICredentialStore ForCurrentPlatform() =>
        OperatingSystem.IsWindows() ? new WindowsCredentialStore() : new UnavailableCredentialStore();
}

internal sealed class UnavailableCredentialStore : ICredentialStore
{
    private static CredentialStoreUnavailableException Fail() =>
        new("This platform has no supported credential store yet, so passwords cannot be saved. Enter the password for each connection.");

    public string? Get(string key) => throw Fail();
    public void Set(string key, string value) => throw Fail();
    public bool Delete(string key) => throw Fail();
}

/// <summary>Windows Credential Manager (generic credentials, persisted for the logged-in user).</summary>
public sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const uint TypeGeneric = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint BlobSize;
        public IntPtr Blob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(ref Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(IntPtr buffer);

    public string? Get(string key)
    {
        if (!CredRead(key, TypeGeneric, 0, out var ptr))
        {
            int error = Marshal.GetLastWin32Error();
            return error == ErrorNotFound ? null : throw new IOException($"Credential Manager read failed (error {error}).");
        }
        try
        {
            var c = Marshal.PtrToStructure<Credential>(ptr);
            return Marshal.PtrToStringUni(c.Blob, (int)c.BlobSize / 2);
        }
        finally { CredFree(ptr); }
    }

    public void Set(string key, string value)
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocCoTaskMem(Math.Max(bytes.Length, 1));
        var target = Marshal.StringToCoTaskMemUni(key);
        var user = Marshal.StringToCoTaskMemUni("Textida");
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var c = new Credential
            {
                Type = TypeGeneric, TargetName = target, UserName = user, Blob = blob, BlobSize = (uint)bytes.Length, Persist = PersistLocalMachine,
            };
            if (!CredWrite(ref c, 0)) throw new IOException($"Credential Manager write failed (error {Marshal.GetLastWin32Error()}).");
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.FreeCoTaskMem(blob);
            Marshal.FreeCoTaskMem(target);
            Marshal.FreeCoTaskMem(user);
        }
    }

    public bool Delete(string key)
    {
        if (CredDelete(key, TypeGeneric, 0)) return true;
        int error = Marshal.GetLastWin32Error();
        return error == ErrorNotFound ? false : throw new IOException($"Credential Manager delete failed (error {error}).");
    }
}
