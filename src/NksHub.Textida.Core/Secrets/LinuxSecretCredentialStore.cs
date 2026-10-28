using System.Runtime.InteropServices;

namespace NksHub.Textida.Core.Secrets;

/// <summary>
/// Linux: the Secret Service (GNOME Keyring, KWallet and others) through libsecret. An item has one attribute, <c>key</c>, and the secret is the password; storing under an existing
/// key replaces it. libsecret declares its calls with a variable argument list; they are declared here with the fixed arguments the calls use, which is how the 64-bit Linux
/// conventions pass them. When libsecret is not installed, <see cref="CreateOrUnavailable"/> returns a store that says so; when no Secret Service answers (a session without a
/// keyring) every call reports <see cref="CredentialStoreUnavailableException"/>. No secret is ever kept in a file instead.
/// </summary>
public sealed partial class LinuxSecretCredentialStore : ICredentialStore
{
    private const string Libsecret = "libsecret-1.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private const string SchemaName = "org.nkshub.Textida.Password";
    private const string AttributeName = "key";
    private const string Collection = "default";

    // struct _SecretSchema on a 64-bit system: name (8), flags (4, padded to 8), 32 attributes of name (8) and type (4, padded to 8), reserved int (4, padded to 8), 7 reserved pointers (56).
    private const int SchemaSize = 8 + 8 + 32 * 16 + 8 + 7 * 8;
    private const int FirstAttributeOffset = 16;
    private const int ErrorMessageOffset = 8; // struct GError: domain (4), code (4), message (pointer)

    [LibraryImport(Libsecret, EntryPoint = "secret_password_store_sync", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PasswordStore(IntPtr schema, string collection, string label, string password, IntPtr cancellable, ref IntPtr error,
        string attributeName, string attributeValue, IntPtr terminator);

    [LibraryImport(Libsecret, EntryPoint = "secret_password_lookup_sync", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr PasswordLookup(IntPtr schema, IntPtr cancellable, ref IntPtr error, string attributeName, string attributeValue, IntPtr terminator);

    [LibraryImport(Libsecret, EntryPoint = "secret_password_clear_sync", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PasswordClear(IntPtr schema, IntPtr cancellable, ref IntPtr error, string attributeName, string attributeValue, IntPtr terminator);

    [LibraryImport(Libsecret, EntryPoint = "secret_password_free")]
    private static partial void PasswordFree(IntPtr password);

    [LibraryImport(Glib, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(IntPtr error);

    private static readonly Lazy<IntPtr> Schema = new(BuildSchema);

    /// <summary>The schema lives as long as the process; libsecret keeps no copy, so it is built once from unmanaged memory.</summary>
    private static IntPtr BuildSchema()
    {
        IntPtr schema;
        unsafe { schema = (IntPtr)NativeMemory.AllocZeroed(SchemaSize); }
        Marshal.WriteIntPtr(schema, 0, Marshal.StringToCoTaskMemUTF8(SchemaName));
        Marshal.WriteInt32(schema, 8, 0); // SECRET_SCHEMA_NONE
        Marshal.WriteIntPtr(schema, FirstAttributeOffset, Marshal.StringToCoTaskMemUTF8(AttributeName));
        Marshal.WriteInt32(schema, FirstAttributeOffset + 8, 0); // SECRET_SCHEMA_ATTRIBUTE_STRING; the next attribute stays zero and ends the list
        return schema;
    }

    /// <summary>The Secret Service store when libsecret loads here, otherwise a store that says why it cannot keep anything.</summary>
    /// <param name="canLoad">Tells whether a native library loads; a test replaces it.</param>
    internal static ICredentialStore CreateOrUnavailable(Func<string, bool>? canLoad = null)
    {
        canLoad ??= CredentialStores.CanLoad;
        if (IntPtr.Size != 8) return new UnavailableCredentialStore("The Linux secret service is supported on 64-bit systems only, so passwords cannot be saved. Enter the password for each connection.");
        return canLoad(Libsecret) && canLoad(Glib)
            ? new LinuxSecretCredentialStore()
            : new UnavailableCredentialStore("The Linux secret service could not be reached: libsecret (libsecret-1-0) is not installed, so passwords cannot be saved. Install it together with a keyring such as GNOME Keyring or KWallet, or enter the password for each connection.");
    }

    public string? Get(string key) => Guarded(() =>
    {
        IntPtr error = IntPtr.Zero;
        IntPtr found = PasswordLookup(Schema.Value, IntPtr.Zero, ref error, AttributeName, Named(key), IntPtr.Zero);
        ThrowIfFailed(error);
        if (found == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUTF8(found); }
        finally { PasswordFree(found); }
    });

    public void Set(string key, string value) => Guarded<object?>(() =>
    {
        IntPtr error = IntPtr.Zero;
        int stored = PasswordStore(Schema.Value, Collection, "Textida: " + key, value, IntPtr.Zero, ref error, AttributeName, Named(key), IntPtr.Zero);
        ThrowIfFailed(error);
        if (stored == 0) throw new IOException("The secret service did not store the password.");
        return null;
    });

    public bool Delete(string key) => Guarded(() =>
    {
        IntPtr error = IntPtr.Zero;
        int removed = PasswordClear(Schema.Value, IntPtr.Zero, ref error, AttributeName, Named(key), IntPtr.Zero);
        ThrowIfFailed(error);
        return removed != 0;
    });

    private static string Named(string key) => key.Length == 0 ? throw new ArgumentException("A credential needs a key.", nameof(key)) : key;

    /// <summary>A GError means the service could not be reached or the keyring stayed locked; both leave the caller without a place to keep the secret.</summary>
    private static void ThrowIfFailed(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        string message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, ErrorMessageOffset)) ?? "unknown error";
        ErrorFree(error);
        throw new CredentialStoreUnavailableException($"The Linux secret service refused the request ({message}), so passwords cannot be saved. Enter the password for each connection.");
    }

    private static T Guarded<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new CredentialStoreUnavailableException("The Linux secret service could not be used (" + e.Message + "), so passwords cannot be saved. Enter the password for each connection.");
        }
    }
}
