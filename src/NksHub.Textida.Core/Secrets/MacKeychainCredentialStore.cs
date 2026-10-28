using System.Runtime.InteropServices;
using System.Text;

namespace NksHub.Textida.Core.Secrets;

/// <summary>
/// macOS Keychain: generic passwords of the default (login) keychain, service "Textida", the key as the account name. It calls the keychain functions of the Security framework
/// (<c>SecKeychainFindGenericPassword</c> and the functions around it); they are marked deprecated by Apple but take plain C strings, so no CoreFoundation dictionary has to be built.
/// The first read of an item may make macOS ask the user for permission; a refusal is reported as an <see cref="IOException"/>. When the framework or a function is missing,
/// <see cref="CreateOrUnavailable"/> and every call report <see cref="CredentialStoreUnavailableException"/> and no secret is kept anywhere else.
/// </summary>
public sealed partial class MacKeychainCredentialStore : ICredentialStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly byte[] ServiceName = "Textida"u8.ToArray();

    private const int ErrSecSuccess = 0;
    private const int ErrSecUserCanceled = -128;
    private const int ErrSecNotAvailable = -25291;
    private const int ErrSecNoSuchKeychain = -25294;
    private const int ErrSecItemNotFound = -25300;
    private const int ErrSecInteractionNotAllowed = -25308;

    [LibraryImport(Security)]
    private static partial int SecKeychainFindGenericPassword(IntPtr keychainOrArray, uint serviceNameLength, ReadOnlySpan<byte> serviceName, uint accountNameLength,
        ReadOnlySpan<byte> accountName, out uint passwordLength, out IntPtr passwordData, out IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainAddGenericPassword(IntPtr keychain, uint serviceNameLength, ReadOnlySpan<byte> serviceName, uint accountNameLength,
        ReadOnlySpan<byte> accountName, uint passwordLength, ReadOnlySpan<byte> passwordData, IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attributeList, uint length, ReadOnlySpan<byte> data);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemDelete(IntPtr itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemFreeContent(IntPtr attributeList, IntPtr data);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);

    /// <summary>The Keychain store when the framework can be loaded here, otherwise a store that says why it cannot keep anything.</summary>
    /// <param name="canLoad">Tells whether a native library loads; a test replaces it.</param>
    internal static ICredentialStore CreateOrUnavailable(Func<string, bool>? canLoad = null)
    {
        canLoad ??= CredentialStores.CanLoad;
        return canLoad(Security) && canLoad(CoreFoundation)
            ? new MacKeychainCredentialStore()
            : new UnavailableCredentialStore("The macOS Keychain could not be loaded (Security.framework is missing), so passwords cannot be saved. Enter the password for each connection.");
    }

    public string? Get(string key) => Guarded(() =>
    {
        int status = Find(key, out var data, out var item, out var bytes);
        if (status == ErrSecItemNotFound) return null;
        Check(status, "read");
        try { return Encoding.UTF8.GetString(bytes!); }
        finally
        {
            Array.Clear(bytes!);
            Release(data, item);
        }
    });

    public void Set(string key, string value) => Guarded<object?>(() =>
    {
        byte[] password = Encoding.UTF8.GetBytes(value);
        try
        {
            int status = Find(key, out var data, out var item, out var existing);
            if (existing is not null) Array.Clear(existing);
            if (status == ErrSecItemNotFound)
            {
                byte[] account = AccountOf(key);
                Check(SecKeychainAddGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)account.Length, account, (uint)password.Length, password, IntPtr.Zero), "write");
                return null;
            }
            Check(status, "write");
            try { Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password), "write"); }
            finally { Release(data, item); }
            return null;
        }
        finally { Array.Clear(password); }
    });

    public bool Delete(string key) => Guarded(() =>
    {
        int status = Find(key, out var data, out var item, out var existing);
        if (existing is not null) Array.Clear(existing);
        if (status == ErrSecItemNotFound) return false;
        Check(status, "delete");
        try
        {
            Check(SecKeychainItemDelete(item), "delete");
            return true;
        }
        finally { Release(data, item); }
    });

    /// <summary>Looks the item up. On success the caller owns <paramref name="data"/> and <paramref name="item"/> and hands them to <see cref="Release"/>.</summary>
    private static int Find(string key, out IntPtr data, out IntPtr item, out byte[]? bytes)
    {
        byte[] account = AccountOf(key);
        int status = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)account.Length, account, out uint length, out data, out item);
        bytes = null;
        if (status == ErrSecSuccess)
        {
            bytes = new byte[length];
            if (length > 0) Marshal.Copy(data, bytes, 0, (int)length);
        }
        return status;
    }

    private static void Release(IntPtr data, IntPtr item)
    {
        if (data != IntPtr.Zero) _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
        if (item != IntPtr.Zero) CFRelease(item);
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] AccountOf(string key) =>
        key.Length == 0 ? throw new ArgumentException("A credential needs a key.", nameof(key)) : Utf8(key);

    private static void Check(int status, string action)
    {
        switch (status)
        {
            case ErrSecSuccess:
                return;
            case ErrSecNotAvailable or ErrSecNoSuchKeychain or ErrSecInteractionNotAllowed:
                throw new CredentialStoreUnavailableException($"The macOS Keychain is not available here ({action} failed, status {status}), so passwords cannot be saved. Enter the password for each connection.");
            case ErrSecUserCanceled:
                throw new IOException($"The Keychain {action} was cancelled.");
            default:
                throw new IOException($"The Keychain {action} failed (status {status}).");
        }
    }

    private static T Guarded<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new CredentialStoreUnavailableException("The macOS Keychain could not be used (" + e.Message + "), so passwords cannot be saved. Enter the password for each connection.");
        }
    }
}
