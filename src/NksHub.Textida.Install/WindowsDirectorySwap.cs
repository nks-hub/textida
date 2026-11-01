using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.Install;

// MoveFileTransactedW includes directory children; without TxF a killed helper could leave the installed executable unreachable.
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsDirectorySwap(string installDir)
{
    internal Action<string>? Checkpoint { get; init; }

    public void Apply(string incoming, bool rollback)
    {
        var previous = installDir + ".previous";
        var retired = installDir + ".retired";
        RemoveRetired(retired);
        using var transaction = CreateTransaction(0, 0, 0, 0, 0, 0, "Textida installation switch");
        if (transaction.IsInvalid) throw Failure("create the directory transaction");

        bool committed = false;
        try
        {
            if (!rollback && Directory.Exists(previous)) Move(transaction, previous, retired);
            var aside = installDir + ".aside";
            Move(transaction, installDir, aside);
            Checkpoint?.Invoke("after-current-move");
            Move(transaction, incoming, installDir);
            Checkpoint?.Invoke("after-incoming-move");
            Move(transaction, aside, previous);
            Checkpoint?.Invoke("before-commit");
            if (!CommitTransaction(transaction)) throw Failure("commit the directory transaction");
            committed = true;
        }
        finally
        {
            // Closing the last handle also aborts TxF; the explicit rollback completes before the caller restarts Textida.
            if (!committed) RollbackTransaction(transaction);
        }
        RemoveRetired(retired);
    }

    private static void Move(SafeFileHandle transaction, string source, string destination)
    {
        if (!MoveFileTransacted(source, destination, 0, 0, 0, transaction)) throw Failure("move an installation directory atomically");
    }

    private static IOException Failure(string operation) => new(
        $"Windows could not {operation}. The installation was kept; this update requires an NTFS volume with transaction support.",
        new Win32Exception(Marshal.GetLastPInvokeError()));

    private static void RemoveRetired(string retired)
    {
        try { if (Directory.Exists(retired)) Directory.Delete(retired, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* A committed switch stays valid; a later update retries the retired folder. */ }
    }

    [LibraryImport("KtmW32.dll", EntryPoint = "CreateTransaction", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateTransaction(nint attributes, nint unitOfWork, uint options, uint isolationLevel, uint isolationFlags, uint timeout, string description);

    [LibraryImport("Kernel32.dll", EntryPoint = "MoveFileTransactedW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileTransacted(string source, string destination, nint progress, nint data, uint flags, SafeFileHandle transaction);

    [LibraryImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CommitTransaction(SafeFileHandle transaction);

    [LibraryImport("KtmW32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RollbackTransaction(SafeFileHandle transaction);
}
