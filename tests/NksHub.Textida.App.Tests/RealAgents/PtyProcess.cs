using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.App.Tests.RealAgents;

/// <summary>
/// A program on a Windows pseudo console (ConPTY), the way a terminal window runs it: what it draws is collected as text, what the test types goes to its input.
/// It is for agents that only work interactively; nothing here touches the keyboard or the clipboard of the desktop.
/// </summary>
internal sealed partial class PtyProcess : IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000, CreateUnicodeEnvironment = 0x00000400;
    private const int UseStdHandles = 0x00000100;
    private static readonly IntPtr PseudoConsoleAttribute = 0x00020016;

    private readonly IntPtr _console;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly StringBuilder _raw = new();
    private readonly object _gate = new();
    private readonly Thread _reader;
    private IntPtr _processHandle;
    private bool _disposed;

    private PtyProcess(IntPtr console, FileStream input, FileStream output, IntPtr processHandle, int id)
    {
        _console = console;
        _input = input;
        _output = output;
        _processHandle = processHandle;
        Id = id;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty-reader" };
        _reader.Start();
    }

    public int Id { get; }

    public bool HasExited => _processHandle == IntPtr.Zero || WaitForSingleObject(_processHandle, 0) == 0;

    /// <summary>Everything the program drew so far, with the escape sequences removed.</summary>
    public string Text
    {
        get
        {
            string raw;
            lock (_gate) raw = _raw.ToString();
            return Plain(raw);
        }
    }

    /// <summary>Starts <paramref name="commandLine"/> (program in quotes, then arguments) in <paramref name="workingDirectory"/> on a console of <paramref name="columns"/> by <paramref name="rows"/>.</summary>
    public static PtyProcess Start(string commandLine, string workingDirectory, IReadOnlyDictionary<string, string>? environment = null, short columns = 140, short rows = 50)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("ConPTY exists on Windows only.");
        // the environment block: name=value, each ended by a NUL, sorted, and one more NUL at the end
        IntPtr block = environment is null ? IntPtr.Zero
            : Marshal.StringToHGlobalUni(string.Concat(environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value + "\0")) + "\0");
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0) || !CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        int hr = CreatePseudoConsole(new Coord { X = columns, Y = rows }, inRead, outWrite, 0, out var console);
        if (hr != 0) throw new Win32Exception(hr, "CreatePseudoConsole failed");
        IntPtr list = IntPtr.Zero;
        try
        {
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            list = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(list, 0, PseudoConsoleAttribute, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            // the flag with all three handles left empty keeps the program from inheriting the standard handles of this process; without it the program writes to them and not to the console
            var startup = new StartupInfoEx { StartupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfoEx>(), dwFlags = UseStdHandles }, lpAttributeList = list };
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfoPresent | CreateUnicodeEnvironment, block, workingDirectory, ref startup, out var info))
                throw new Win32Exception();
            CloseHandle(info.hThread);
            inRead.Dispose();
            outWrite.Dispose();
            return new PtyProcess(console, new FileStream(inWrite, FileAccess.Write, 1, false), new FileStream(outRead, FileAccess.Read, 4096, false), info.hProcess, info.dwProcessId);
        }
        catch
        {
            ClosePseudoConsole(console);
            inRead.Dispose();
            inWrite.Dispose();
            outRead.Dispose();
            outWrite.Dispose();
            throw;
        }
        finally
        {
            if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
            if (list != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(list);
                Marshal.FreeHGlobal(list);
            }
        }
    }

    private void ReadLoop()
    {
        var buffer = new byte[4096];
        var decoder = new UTF8Encoding(false).GetDecoder();
        var chars = new char[8192];
        try
        {
            while (true)
            {
                int n = _output.Read(buffer, 0, buffer.Length);
                if (n <= 0) { ReaderEnd = "eof"; return; }
                BytesRead += n;
                int c = decoder.GetChars(buffer, 0, n, chars, 0);
                lock (_gate) _raw.Append(chars, 0, c);
            }
        }
        catch (Exception e) { ReaderEnd = e.ToString(); }
    }

    public long BytesRead { get; private set; }

    /// <summary>Everything the program wrote, escape sequences included, with the escape character shown as \e.</summary>
    public string Raw
    {
        get
        {
            lock (_gate) return _raw.ToString().Replace("\u001b", "\\e");
        }
    }

    public string? ReaderEnd { get; private set; }

    /// <summary>Types the text. A line break is sent as Enter, so send Enter on its own (<see cref="Enter"/>) after text the program should take as typed, not pasted.</summary>
    public void Type(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _input.Write(bytes, 0, bytes.Length);
        _input.Flush();
    }

    public void Enter() => Type("\r");

    /// <summary>Waits until the plain text satisfies <paramref name="condition"/>; false when <paramref name="timeout"/> passes or the program ends first.</summary>
    public async Task<bool> WaitForAsync(Func<string, bool> condition, TimeSpan timeout)
    {
        for (var sw = Stopwatch.StartNew(); sw.Elapsed < timeout;)
        {
            if (condition(Text)) return true;
            if (HasExited) return condition(Text);
            await Task.Delay(100);
        }
        return false;
    }

    /// <summary>Ends the program and every process it started; the pseudo console goes with it.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            using var process = Process.GetProcessById(Id);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception) { /* already gone */ }
        ClosePseudoConsole(_console);
        if (_processHandle != IntPtr.Zero) CloseHandle(_processHandle);
        _processHandle = IntPtr.Zero;
        _reader.Join(2000);
        _input.Dispose();
        _output.Dispose();
    }

    // The pseudo console draws a screen; a cursor move to the right stands for the spaces the program printed.
    private static string Plain(string raw)
    {
        raw = ForwardMove().Replace(raw, " ");
        raw = Osc().Replace(raw, "");
        raw = Csi().Replace(raw, "");
        return raw.Replace("\r", "");
    }

    [GeneratedRegex("\u001b\\[\\d*C")]
    private static partial Regex ForwardMove();

    [GeneratedRegex("\u001b\\][^\u0007\u001b]*(\u0007|\u001b\\\\)")]
    private static partial Regex Osc();

    [GeneratedRegex("\u001b(\\[[0-9;?<>=!]*[ -/]*[@-~]|[()][A-Za-z0-9]|[=>78M])")]
    private static partial Regex Csi();

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord { public short X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        public int cb;
        public IntPtr lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo StartupInfo; public IntPtr lpAttributeList; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, int size);

    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr console);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags,
        IntPtr environment, string? currentDirectory, ref StartupInfoEx startup, out ProcessInformation information);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern int WaitForSingleObject(IntPtr handle, int milliseconds);
}
