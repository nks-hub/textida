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
        if (OperatingSystem.IsWindows()) RunWindows("--elevated-install", tempPath, target, sha256);
        else if (OperatingSystem.IsMacOS()) RunMac(MacInstallScript(tempPath, target, sha256));
        else if (OperatingSystem.IsLinux()) RunLinux("--elevated-install", tempPath, target, sha256);
        else throw new ElevationException("This system has no way to ask for permission.");
    }

    public void Read(string source, string destination, long maxBytes)
    {
        if (OperatingSystem.IsWindows()) RunWindows("--elevated-read", source, destination, maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        else if (OperatingSystem.IsMacOS()) RunMac(MacReadScript(source, destination, maxBytes));
        else if (OperatingSystem.IsLinux()) RunLinux("--elevated-read", source, destination, maxBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        else throw new ElevationException("This system has no way to ask for permission.");
    }

    private static void RunWindows(string verb, params string[] arguments)
    {
        string result = Path.Combine(Path.GetTempPath(), "textida-elevated-" + Guid.NewGuid().ToString("N") + ".result");
        var info = new ProcessStartInfo(Environment.ProcessPath ?? throw new ElevationException("The program of the editor is not known.")) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        if (Path.GetFileNameWithoutExtension(info.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)) info.ArgumentList.Add(Environment.GetCommandLineArgs()[0]); // run from the assembly, not from the single-file program
        info.ArgumentList.Add(verb);
        foreach (string a in arguments) info.ArgumentList.Add(a);
        info.ArgumentList.Add(result);
        try
        {
            using var process = Process.Start(info) ?? throw new ElevationException("The helper did not start.");
            if (!process.WaitForExit(Patience)) throw new ElevationException("The helper did not finish in time.");
            string answer = File.Exists(result) ? File.ReadAllText(result) : "";
            if (process.ExitCode != 0 || answer != "ok") throw new ElevationException(answer.Length > 0 ? answer : $"The helper ended with code {process.ExitCode}.");
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) { throw new ElevationCancelledException(); } // ERROR_CANCELLED: the user said no to UAC
        catch (Win32Exception e) { throw new ElevationException(e.Message); }
        finally { try { File.Delete(result); } catch (IOException) { /* in the temporary folder */ } }
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
        string result = Path.Combine(Path.GetTempPath(), "textida-elevated-" + Guid.NewGuid().ToString("N") + ".result");
        File.WriteAllText(result, ""); // made by the user, so the user can delete it after the helper wrote into it as root
        var info = new ProcessStartInfo(pkexec) { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string a in LinuxArguments(program, Environment.GetCommandLineArgs()[0], verb, arguments, result)) info.ArgumentList.Add(a);
        try
        {
            using var process = Process.Start(info) ?? throw new ElevationException("pkexec did not start.");
            string errors = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(Patience)) { try { process.Kill(); } catch (InvalidOperationException) { } throw new ElevationException("The authorization did not finish in time."); }
            if (process.ExitCode == 126) throw new ElevationCancelledException(); // pkexec: the user dismissed the dialog
            string answer = File.ReadAllText(result);
            if (process.ExitCode != 0 || answer != "ok") throw new ElevationException(answer.Length > 0 ? answer : errors.Trim().Length > 0 ? errors.Trim() : $"pkexec ended with code {process.ExitCode}.");
        }
        catch (Win32Exception e) { throw new ElevationException(e.Message); }
        finally { try { File.Delete(result); } catch (IOException) { /* in the temporary folder */ } }
    }

    // ---- macOS: the script keeps every path out of the code of the shell by passing it through "quoted form of" ----

    private static string AppleString(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    internal static string MacInstallScript(string temp, string target, string sha256) =>
        $"set src to {AppleString(temp)}\nset dst to {AppleString(target)}\nset want to {AppleString(sha256.ToLowerInvariant())}\n" +
        "do shell script \"test \\\"$(/usr/bin/shasum -a 256 \" & quoted form of src & \" | /usr/bin/awk '{print $1}')\\\" = \" & quoted form of want & \" && /bin/cp \" & quoted form of src & \" \" & quoted form of dst with administrator privileges";

    internal static string MacReadScript(string source, string destination, long maxBytes) =>
        $"set src to {AppleString(source)}\nset dst to {AppleString(destination)}\nset who to {AppleString(Environment.UserName)}\n" +
        $"do shell script \"test $(/usr/bin/stat -f %z \" & quoted form of src & \") -le {maxBytes} && /bin/cp \" & quoted form of src & \" \" & quoted form of dst & \" && /usr/sbin/chown \" & quoted form of who & \" \" & quoted form of dst with administrator privileges";

    private static void RunMac(string script)
    {
        var info = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (string line in script.Split('\n')) { info.ArgumentList.Add("-e"); info.ArgumentList.Add(line); }
        using var process = Process.Start(info) ?? throw new ElevationException("osascript did not start.");
        string errors = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(Patience)) { try { process.Kill(); } catch (InvalidOperationException) { } throw new ElevationException("The authorization did not finish in time."); }
        if (process.ExitCode == 0) return;
        if (errors.Contains("-128", StringComparison.Ordinal)) throw new ElevationCancelledException(); // errAECoercionFail / "User canceled"
        throw new ElevationException(errors.Trim().Length > 0 ? errors.Trim() : $"osascript ended with code {process.ExitCode}.");
    }
}

/// <summary>
/// The job of the elevated process: it checks what it is asked, does one copy and says how it went. It runs in the same program file as the editor (<c>--elevated-install</c>, <c>--elevated-read</c>), takes no input but its arguments
/// and opens no window. The write keeps the file (its owner, rights and links) by overwriting it where it is, so it is not atomic: the bytes are all in memory and the hash is checked before the first one is written.
/// </summary>
internal static partial class ElevatedHelper
{
    public const long MaxWriteBytes = 512L << 20;

    /// <summary>Exit code 0 and the word <c>ok</c> in the result file, else code 1 and the reason there.</summary>
    public static int Run(string[] args)
    {
        string? resultFile = args.Length > 1 ? args[^1] : null;
        try
        {
            switch (args)
            {
                case ["--elevated-install", var temp, var target, var hash, _]: Install(temp, target, hash); break;
                case ["--elevated-read", var source, var destination, var max, _] when long.TryParse(max, out long limit): Read(source, destination, limit); break;
                default: throw new ElevationException("The helper was called with arguments it does not know.");
            }
            Report(resultFile, "ok");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Report(resultFile, e.Message);
            return 1;
        }
    }

    private static void Report(string? resultFile, string text)
    {
        if (resultFile is null || !Path.IsPathRooted(resultFile)) return;
        try { File.WriteAllText(resultFile, text, new UTF8Encoding(false)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the exit code still tells */ }
    }

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
        GiveToCaller(destination);
    }

    /// <summary>Under pkexec the new file belongs to root; polkit names the user in PKEXEC_UID, and the copy is handed to them so the editor can read and delete it.</summary>
    private static void GiveToCaller(string path)
    {
        if (OperatingSystem.IsWindows() || !uint.TryParse(Environment.GetEnvironmentVariable("PKEXEC_UID"), out uint uid)) return;
        if (chown(path, uid, uint.MaxValue) != 0) throw new ElevationException("The copy could not be handed to the user: error " + Marshal.GetLastPInvokeError());
    }

    [LibraryImport("libc", EntryPoint = "chown", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int chown(string path, uint owner, uint group);
}

/// <summary>Hands the file a save wrote to the elevation, for the one save the user confirmed.</summary>
internal sealed class ElevatedWriter(IElevation elevation) : IProtectedWriter
{
    public void Install(string tempPath, string target, string sha256) => elevation.Install(tempPath, target, sha256);
}
