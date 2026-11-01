using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace NksHub.Textida.App.Services;

// The directory exchange keeps a runnable version at the install path even if the helper is killed.
internal sealed partial class UpdateSwap(string installDir, Action<string, string>? exchange = null)
{
    private string Receipt => installDir + ".update";
    private string Previous => installDir + ".previous";
    private string Staged => installDir + ".new";
    private string Retired => installDir + ".retired";
    internal Action<string>? Checkpoint { get; init; }
    internal Action<string>? VerifyIncoming { get; init; }

    public void Apply(bool rollback)
    {
        using var lease = new FileStream(Receipt + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverLocked();
        var incoming = rollback ? Previous : Staged;
        var version = File.ReadAllText(AppUpdater.VersionFile(incoming)).Trim();
        if (version.Length == 0 || version == AppUpdater.InstalledVersion(installDir)) throw new IOException("The incoming version must differ from the installed version.");
        if (Directory.Exists(Retired)) Directory.Delete(Retired, recursive: true);
        PreserveProfile(incoming);
        VerifyIncoming?.Invoke(incoming);
        var text = new JsonObject { ["version"] = version, ["rollback"] = rollback }.ToJsonString();
        using (var receipt = new FileStream(Receipt + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            receipt.Write(Encoding.UTF8.GetBytes(text));
            receipt.Flush(flushToDisk: true);
        }
        File.Move(Receipt + ".tmp", Receipt, overwrite: true);
        Checkpoint?.Invoke("before-exchange");
        (exchange ?? Exchange)(installDir, incoming);
        Checkpoint?.Invoke("after-exchange");
        RecoverLocked();
    }

    public void Recover()
    {
        if (!File.Exists(Receipt)) return;
        using var lease = new FileStream(Receipt + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        RecoverLocked();
    }

    private void RecoverLocked()
    {
        if (!File.Exists(Receipt)) return;
        var root = JsonNode.Parse(File.ReadAllText(Receipt)) as JsonObject ?? throw new IOException("The update receipt is invalid.");
        var expected = root["version"]?.GetValue<string>() ?? throw new IOException("The update receipt has no version.");
        var rollback = root["rollback"]?.GetValue<bool>() ?? throw new IOException("The update receipt has no operation.");
        if (AppUpdater.InstalledVersion(installDir) == expected && !rollback)
        {
            if (Directory.Exists(Staged))
            {
                if (Directory.Exists(Previous))
                {
                    if (Directory.Exists(Retired)) Directory.Delete(Retired, recursive: true);
                    Directory.Move(Previous, Retired);
                }
                Checkpoint?.Invoke("before-previous");
                Directory.Move(Staged, Previous);
            }
            if (Directory.Exists(Retired)) Directory.Delete(Retired, recursive: true);
        }
        File.Delete(Receipt);
    }

    private void PreserveProfile(string incoming)
    {
        foreach (var name in new[] { "profile", "textida.portable" })
        {
            var source = Path.Combine(installDir, name);
            var target = Path.Combine(incoming, name);
            if (Directory.Exists(source))
            {
                if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                CopyTree(source, target);
            }
            else if (File.Exists(source)) File.Copy(source, target, overwrite: true);
        }
    }

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source)) CopyTree(dir, Path.Combine(target, Path.GetFileName(dir)));
    }

    internal static void Exchange(string left, string right)
    {
        int result = OperatingSystem.IsMacOS() ? RenameSwapMac(left, right, 2)
            : OperatingSystem.IsLinux() ? RenameSwapLinux(-100, left, -100, right, 2)
            : throw new PlatformNotSupportedException();
        if (result != 0) throw new IOException("The installation folders could not be exchanged.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }

    [LibraryImport("libSystem.B.dylib", EntryPoint = "renamex_np", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameSwapMac(string left, string right, uint flags);

    [LibraryImport("libc", EntryPoint = "renameat2", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int RenameSwapLinux(int leftFd, string left, int rightFd, string right, uint flags);
}
