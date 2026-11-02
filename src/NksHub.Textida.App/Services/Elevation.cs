using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Services;

/// <summary>The elevated helper did not do what it was asked: <see cref="IOException.Message"/> says what it found.</summary>
internal class ElevationException(string message) : IOException(message);

/// <summary>The user answered the question of the system (UAC, the authorization window of macOS, the dialog of polkit) with no.</summary>
internal sealed class ElevationCancelledException() : ElevationException("The system asked for permission and it was not given.");

/// <summary>
/// How the editor reaches a place it has no right to (PZ.4.2). The editor itself never runs elevated: one small job, the copy of one file, runs in a helper process that the system lets the user allow. No file
/// or folder is named in advance as protected: a job is tried as the user, and when the system says no, the user is asked whether the helper may do it.
/// </summary>
internal interface IElevation
{
    /// <summary>False where the system has nothing the editor can ask the user's permission with.</summary>
    bool IsAvailable { get; }

    /// <summary>Puts the file <paramref name="tempPath"/> (whose SHA-256 in hex is <paramref name="sha256"/>) at <paramref name="target"/>.</summary>
    void Install(string tempPath, string target, string sha256);

    /// <summary>Copies the file <paramref name="source"/>, which the user cannot read, to <paramref name="destination"/> in a place the user can; refuses a file over <paramref name="maxBytes"/>.</summary>
    void Read(string source, string destination, long maxBytes);
}

/// <summary>
/// The elevation of the system: on Windows the program itself started again with the <c>runas</c> verb (the UAC prompt names it), on macOS <c>osascript</c> with administrator privileges (the system authorization window), on Linux the program itself started again through <c>pkexec</c> (polkit asks).
/// The job has the same code on both: <see cref="ElevatedHelper"/>.
/// </summary>
internal sealed class SystemElevation : IElevation
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    // on Linux the answer is given when the helper is asked for: a missing pkexec is then told to the user as the reason
    public bool IsAvailable => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    /// <summary>The pkexec program of polkit, or null when this system has none.</summary>
    internal static string? FindPkexec() => new[] { "/usr/bin/pkexec", "/bin/pkexec", "/usr/local/bin/pkexec" }.FirstOrDefault(File.Exists);

    public void Install(string tempPath, string target, string sha256)
    {
        if (OperatingSystem.IsWindows()) RunWindows("--elevated-install", null, tempPath, target, sha256);
        else if (OperatingSystem.IsMacOS()) RunMacHelper("--elevated-install", tempPath, target, sha256);
        else if (OperatingSystem.IsLinux()) RunLinux("--elevated-install", tempPath, target, sha256);
        else throw new ElevationException("This system has no way to ask for permission.");
    }

    public void Read(string source, string destination, long maxBytes)
    {
        string max = maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (OperatingSystem.IsWindows()) { RunWindows("--elevated-read", destination, source, max); return; }
        if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux()) throw new ElevationException("This system has no way to ask for permission.");

        // root writes the copy only into a folder it makes itself in the shared temporary folder, then hands it over; the user copies it on from there
        string name = "textida-read-" + Guid.NewGuid().ToString("N"), folder = Path.Combine(ElevatedHelper.SharedTemp, name);
        try { source = File.ResolveLinkTarget(source, true)?.FullName ?? source; } // the helper opens no link, so a link of the user's is followed here
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the helper says what it finds */ }
        try
        {
            string[] job = [source, name, max, ElevatedHelper.getuid().ToString(System.Globalization.CultureInfo.InvariantCulture)];
            if (OperatingSystem.IsMacOS()) RunMacHelper("--elevated-read-as", job);
            else RunLinux("--elevated-read-as", job);
            File.Copy(Path.Combine(folder, ElevatedHelper.CopyName), destination);
        }
        finally { try { Directory.Delete(folder, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* in the temporary folder */ } }
    }

    /// <summary>
    /// UAC: runas starts the helper through the shell, so it can hand back neither its output nor a handle, only its exit code. The answer (and the copy of a read) is written by the helper into a new
    /// folder it makes itself in the Temp folder of Windows, which only administrators may write into and the user may read and delete (<see cref="ElevatedHelper.MakeBox"/>); nothing elevated is written in a folder of the user.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RunWindows(string verb, string? copyTo, params string[] arguments)
    {
        string name = "textida-" + Guid.NewGuid().ToString("N"), box = Path.Combine(ElevatedHelper.WindowsTemp, name);
        var info = new ProcessStartInfo(Environment.ProcessPath ?? throw new ElevationException("The program of the editor is not known.")) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]); // run from the assembly, not from the single-file program
        info.ArgumentList.Add(verb);
        foreach (string a in arguments) info.ArgumentList.Add(a);
        info.ArgumentList.Add($"box:{System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value}:{name}");
        try
        {
            using var process = Process.Start(info) ?? throw new ElevationException("The helper did not start.");
            if (!process.WaitForExit(Patience)) throw new ElevationException("The helper did not finish in time.");
            string result = Path.Combine(box, ElevatedHelper.ResultName);
            string answer = File.Exists(result) ? File.ReadAllText(result) : "";
            if (process.ExitCode != 0 || answer != "ok") throw new ElevationException(answer.Length > 0 ? answer : $"The helper ended with code {process.ExitCode}.");
            if (copyTo is not null) File.Copy(Path.Combine(box, ElevatedHelper.CopyName), copyTo);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { throw new ElevationCancelledException(); } // ERROR_CANCELLED: the user said no to UAC
        catch (Win32Exception e) { throw new ElevationException(e.Message); }
        finally { try { Directory.Delete(box, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* in the Temp folder of Windows */ } }
    }

    // ---- Linux: pkexec starts the program of the editor again with the job; polkit shows its own dialog ----

    /// <summary>The command line pkexec gets for a job: the program of the editor (through dotnet when it runs from an assembly), the verb, the arguments and the result file.</summary>
    internal static List<string> LinuxArguments(string program, string? assembly, string verb, string[] arguments, string result)
    {
        var line = new List<string> { program };
        if (assembly is not null && Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) line.Add(assembly);
        line.Add(verb);
        line.AddRange(arguments);
        line.Add(result);
        return line;
    }

    private static void RunLinux(string verb, params string[] arguments)
    {
        string pkexec = FindPkexec() ?? throw new ElevationException("pkexec (polkit) is not installed, so there is nothing to ask the system for permission with. Install polkit, or open the file as root another way.");
        string program = Environment.ProcessPath ?? throw new ElevationException("The program of the editor is not known.");
        // the answer comes on the standard output ("-"): a result file in a folder of the user could be swapped for a link that root then writes through
        var info = new ProcessStartInfo(pkexec) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (string a in LinuxArguments(program, Environment.GetCommandLineArgs()[0], verb, arguments, "-")) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info) ?? throw new ElevationException("pkexec did not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            string errors = process.StandardError.ReadToEnd().Trim();
            if (!process.WaitForExit(Patience)) { try { process.Kill(); } catch (InvalidOperationException) { } throw new ElevationException("The authorization did not finish in time."); }
            if (process.ExitCode == 126) throw new ElevationCancelledException(); // pkexec: the user dismissed the dialog
            string answer = output.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0 || answer != "ok") throw new ElevationException(errors.Length > 0 ? errors : answer.Length > 0 ? answer : $"pkexec ended with code {process.ExitCode}.");
        }
        catch (Win32Exception e) { throw new ElevationException(e.Message); }
    }

    // ---- macOS: the script keeps every path out of the code of the shell by passing it through "quoted form of" ----

    private static string AppleString(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Runs the command line <paramref name="line"/> (the program of the editor with a helper job, <see cref="LinuxArguments"/>) as root: the helper reads the staged file once into memory and checks
    /// its hash there, so the file the user can write cannot change between the check and the copy.
    /// </summary>
    internal static string MacHelperScript(IReadOnlyList<string> line) =>
        string.Concat(line.Select((a, i) => $"set a{i} to {AppleString(a)}\n")) +
        "do shell script " + string.Join(" & \" \" & ", line.Select((_, i) => $"quoted form of a{i}")) + " with administrator privileges";

    private static void RunMacHelper(string verb, params string[] arguments)
    {
        string program = Environment.ProcessPath ?? throw new ElevationException("The program of the editor is not known.");
        // "-": the helper answers on its standard output, which do shell script returns; no result file that root would write through a link
        string answer = RunMac(MacHelperScript(LinuxArguments(program, Environment.GetCommandLineArgs()[0], verb, arguments, "-"))).Trim();
        if (answer != "ok") throw new ElevationException(answer.Length > 0 ? answer : "The helper did not say how the job went.");
    }

    /// <summary>Runs the script and gives its standard output; a failed shell command comes back as "execution error: (its standard error) (code)", of which the reason is kept.</summary>
    private static string RunMac(string script)
    {
        var info = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (string line in script.Split('\n')) { info.ArgumentList.Add("-e"); info.ArgumentList.Add(line); }
        using var process = Process.Start(info) ?? throw new ElevationException("osascript did not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        string errors = process.StandardError.ReadToEnd().Trim();
        if (!process.WaitForExit(Patience)) { try { process.Kill(); } catch (InvalidOperationException) { } throw new ElevationException("The authorization did not finish in time."); }
        if (process.ExitCode == 0) return output.GetAwaiter().GetResult();
        if (errors.Contains("-128", StringComparison.Ordinal)) throw new ElevationCancelledException(); // errAECoercionFail / "User canceled"
        if (System.Text.RegularExpressions.Regex.Match(errors, @"execution error: (.+) \(-?\d+\)$", System.Text.RegularExpressions.RegexOptions.Singleline) is { Success: true } m) errors = m.Groups[1].Value;
        throw new ElevationException(errors.Length > 0 ? errors : $"osascript ended with code {process.ExitCode}.");
    }
}

/// <summary>
/// The job of the elevated process: it checks what it is asked, does one copy and says how it went. It runs in the same program file as the editor (<c>--elevated-install</c>, <c>--elevated-read</c> on Windows, <c>--elevated-read-as</c> on macOS and Linux), takes no input but its arguments
/// and opens no window. The write keeps the file (its owner, rights and links) by overwriting it where it is, so it is not atomic: the bytes are all in memory and the hash is checked before the first one is written.
/// </summary>
internal static partial class ElevatedHelper
{
    public const long MaxWriteBytes = 512L << 20;

    /// <summary>The file name of the copy in the folder of <see cref="ReadAs"/>.</summary>
    public const string CopyName = "copy";

    /// <summary>The temporary folder of the system on macOS and Linux: root's, sticky, so no user can rename or replace what root makes in it.</summary>
    public static string SharedTemp => OperatingSystem.IsMacOS() ? "/private/tmp" : "/tmp";

    /// <summary>The file name of the answer in the folder of <see cref="MakeBox"/>.</summary>
    public const string ResultName = "result";

    /// <summary>The Temp folder of Windows: only the system may rename or replace it, and what an administrator makes in it gets the rights it is made with.</summary>
    public static string WindowsTemp => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");

    /// <summary>
    /// Exit code 0 and the word <c>ok</c>, else code 1 and the reason. The last argument says where: <c>-</c> is the standard output (ok) and error (the reason), on macOS and Linux;
    /// <c>box:&lt;SID of the user&gt;:&lt;name&gt;</c> is the file <see cref="ResultName"/> in a new folder of that name in <see cref="WindowsTemp"/> (<see cref="MakeBox"/>), on Windows, where UAC leaves no other way back.
    /// </summary>
    public static int Run(string[] args, TextWriter? output = null, TextWriter? errors = null)
    {
        string? channel = args.Length > 1 ? args[^1] : null;
        string? box = null;
        try
        {
            if (OperatingSystem.IsWindows() && channel is not null && channel.StartsWith("box:", StringComparison.Ordinal)) box = OpenBox(channel);
            switch (args)
            {
                case ["--elevated-install", var temp, var target, var hash, _]: Install(temp, target, hash); break;
                case ["--elevated-read", var source, var max, _] when box is not null && long.TryParse(max, out long limit): Read(source, Path.Combine(box, CopyName), limit); break;
                case ["--elevated-read-as", var source, var name, var max, var user, _] when long.TryParse(max, out long limit) && uint.TryParse(user, out uint uid):
                    ReadAs(source, Path.Combine(SharedTemp, PlainName(name)), limit, uid);
                    break;
                default: throw new ElevationException("The helper was called with arguments it does not know.");
            }
            Report(channel, box, "ok", output ?? Console.Out);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Report(channel, box, e.Message, errors ?? Console.Error);
            return 1;
        }
    }

    private static string PlainName(string name) =>
        name.Length == 0 || name != Path.GetFileName(name) || name is "." or ".." ? throw new ElevationException("The name of the folder for the copy is not a plain name.") : name;

    private static void Report(string? channel, string? box, string text, TextWriter stream)
    {
        if (channel == "-") { stream.Write(text); stream.Flush(); return; }
        if (box is null) return; // no box, no file: the exit code still tells
        try { WriteNew(Path.Combine(box, ResultName), Encoding.UTF8.GetBytes(text)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the exit code still tells */ }
    }

    /// <summary>Writes a new file; anything already at <paramref name="path"/>, a link included, is refused (CREATE_NEW, O_CREAT|O_EXCL), never written through.</summary>
    internal static void WriteNew(string path, byte[] bytes)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(true);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string OpenBox(string channel)
    {
        string[] parts = channel.Split(':');
        if (parts.Length != 3) throw new ElevationException("The helper was called with arguments it does not know.");
        var user = new System.Security.Principal.SecurityIdentifier(parts[1]);
        string box = Path.Combine(WindowsTemp, PlainName(parts[2]));
        MakeBox(box, user, new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, null));
        return box;
    }

    /// <summary>
    /// Makes the new folder <paramref name="folder"/> with its rights set at creation: <paramref name="writer"/> (the administrators) and the system may do anything in it, <paramref name="reader"/> (the user) may only read and delete.
    /// Anything already in its place, a junction or a link included, is refused (CreateDirectory does not follow the last name), and the folder made is checked not to be a reparse point.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static void MakeBox(string folder, System.Security.Principal.SecurityIdentifier reader, System.Security.Principal.SecurityIdentifier writer)
    {
        const System.Security.AccessControl.InheritanceFlags All = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;
        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        foreach (var (who, rights) in new[]
                 {
                     (new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.LocalSystemSid, null), System.Security.AccessControl.FileSystemRights.FullControl),
                     (writer, System.Security.AccessControl.FileSystemRights.FullControl),
                     (reader, System.Security.AccessControl.FileSystemRights.ReadAndExecute | System.Security.AccessControl.FileSystemRights.Delete),
                     // OWNER RIGHTS: the owner gets only this, not the implied right to change the rights, should the creator's own account (the user's, under split UAC) become the owner
                     (new System.Security.Principal.SecurityIdentifier("S-1-3-4"), System.Security.AccessControl.FileSystemRights.ReadAndExecute | System.Security.AccessControl.FileSystemRights.Delete),
                 })
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(who, rights, All, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        byte[] descriptor = security.GetSecurityDescriptorBinaryForm();
        nint memory = Marshal.AllocHGlobal(descriptor.Length);
        try
        {
            Marshal.Copy(descriptor, 0, memory, descriptor.Length);
            var attributes = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = memory };
            if (!CreateDirectoryW(folder, attributes)) throw new ElevationException($"The folder for the answer could not be made (error {Marshal.GetLastPInvokeError()}); something may already be in its place.");
        }
        finally { Marshal.FreeHGlobal(memory); }
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new ElevationException("The folder for the answer is a link.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint Descriptor;
        public int InheritHandle;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateDirectoryW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateDirectoryW(string path, in SecurityAttributes attributes);

    private static void RequireFile(string path, string what)
    {
        if (!Path.IsPathRooted(path)) throw new ElevationException($"{what} is not a full path.");
        if (Directory.Exists(path)) throw new ElevationException($"{what} is a folder.");
    }

    /// <summary>Writes the bytes of <paramref name="tempPath"/> over <paramref name="target"/> (created when it does not exist) when their SHA-256 is <paramref name="sha256"/>.</summary>
    public static void Install(string tempPath, string target, string sha256)
    {
        RequireFile(tempPath, "The staged file");
        RequireFile(target, "The target");
        if (new FileInfo(tempPath).Length > MaxWriteBytes) throw new ElevationException("The file is larger than the helper writes.");
        byte[] bytes = File.ReadAllBytes(tempPath);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), sha256, StringComparison.OrdinalIgnoreCase)) throw new ElevationException("The staged file is not the one the editor wrote (its hash differs).");
        bool exists = File.Exists(target);
        using var file = new FileStream(target, exists ? FileMode.Open : FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        if (exists) file.SetLength(0);
        file.Write(bytes);
        file.Flush(true);
    }

    /// <summary>Copies <paramref name="source"/> to the new file <paramref name="destination"/> when it is at most <paramref name="maxBytes"/> long.</summary>
    public static void Read(string source, string destination, long maxBytes)
    {
        RequireFile(source, "The source");
        RequireFile(destination, "The destination");
        if (new FileInfo(source).Length > maxBytes) throw new ElevationException($"The file is larger than {maxBytes} bytes.");
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
        output.Flush();
    }

    /// <summary>
    /// The read as root on macOS and Linux: reads <paramref name="source"/> (not a link) into memory, makes the new folder <paramref name="folder"/> (0700; anything already there, a link included, is refused),
    /// writes the copy into it as a new file (0600) and hands the file, then the folder, to <paramref name="uid"/>. Root writes only in a folder it has just made and nobody else can enter.
    /// </summary>
    public static string ReadAs(string source, string folder, long maxBytes, uint uid)
    {
        RequireFile(source, "The source");
        if (!Path.IsPathRooted(folder)) throw new ElevationException("The folder for the copy is not a full path.");
        byte[] bytes = ReadNoFollow(source, maxBytes);
        if (mkdir(folder, 0b111_000_000) != 0) throw new ElevationException($"The folder for the copy could not be made (error {Marshal.GetLastPInvokeError()}); something may already be in its place.");
        string copy = Path.Combine(folder, CopyName);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var output = new FileStream(copy, options))
        {
            output.Write(bytes);
            output.Flush(true);
            if (fchown(output.SafeFileHandle, uid, uint.MaxValue) != 0) throw new ElevationException("The copy could not be handed to the user: error " + Marshal.GetLastPInvokeError());
        }
        // last: once the folder is the user's, root does nothing more in it
        if (lchown(folder, uid, uint.MaxValue) != 0) throw new ElevationException("The folder of the copy could not be handed to the user: error " + Marshal.GetLastPInvokeError());
        return copy;
    }

    private static byte[] ReadNoFollow(string source, long maxBytes)
    {
        // O_RDONLY | O_NOFOLLOW: a link in the place of the file is refused (ELOOP) instead of read
        int noFollow = OperatingSystem.IsMacOS() ? 0x100 : RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm ? 0x8000 : 0x20000;
        int fd = open(source, noFollow);
        if (fd < 0) throw new ElevationException($"The source could not be opened (error {Marshal.GetLastPInvokeError()}); a link is not followed.");
        using var input = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, true), FileAccess.Read, 1);
        using var memory = new MemoryStream();
        byte[] chunk = new byte[81920];
        for (int n; (n = input.Read(chunk)) > 0;)
        {
            memory.Write(chunk, 0, n);
            if (memory.Length > maxBytes) throw new ElevationException($"The file is larger than {maxBytes} bytes.");
        }
        return memory.ToArray();
    }

    [LibraryImport("libc", EntryPoint = "getuid")]
    internal static partial uint getuid();

    [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "mkdir", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int mkdir(string path, uint mode);

    [LibraryImport("libc", EntryPoint = "fchown", SetLastError = true)]
    private static partial int fchown(Microsoft.Win32.SafeHandles.SafeFileHandle fd, uint owner, uint group);

    [LibraryImport("libc", EntryPoint = "lchown", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int lchown(string path, uint owner, uint group);
}

/// <summary>Hands the file a save wrote to the elevation, for the one save the user confirmed.</summary>
internal sealed class ElevatedWriter(IElevation elevation) : IProtectedWriter
{
    public void Install(string tempPath, string target, string sha256) => elevation.Install(tempPath, target, sha256);
}
