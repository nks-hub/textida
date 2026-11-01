using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.Core.Terminal;

/// <summary>A program on a Windows pseudo console (ConPTY), the way a terminal window runs it. The program joins the job object of the editor, so it does not outlive the editor, and <see cref="Dispose"/> ends its whole process tree.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ConPtyProcess : IPseudoTerminal
{
    private const uint ExtendedStartupInfoPresent = 0x00080000, CreateUnicodeEnvironment = 0x00000400;
    private const int UseStdHandles = 0x00000100;
    private static readonly IntPtr PseudoConsoleAttribute = 0x00020016;

    private readonly IntPtr _console;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly Thread _reader;
    private readonly object _gate = new();
    private IntPtr _process;
    private bool _disposed;
    private int _exitReported;

    private ConPtyProcess(IntPtr console, FileStream input, FileStream output, IntPtr process, int id)
    {
        _console = console;
        _input = input;
        _output = output;
        _process = process;
        ProcessId = id;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "conpty-reader" };
        _reader.Start();
        new Thread(WaitForExit) { IsBackground = true, Name = "conpty-wait" }.Start();
    }

    public int ProcessId { get; }
    public event Action<ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<int>? Exited;

    public bool HasExited => ExitCode is not null;

    public int? ExitCode
    {
        get
        {
            lock (_gate)
            {
                if (_process == IntPtr.Zero) return _exitCode;
                return GetExitCodeProcess(_process, out int code) && code != 259 ? code : null; // 259: still active
            }
        }
    }

    private int? _exitCode;

    internal static ConPtyProcess Start(string program, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string> environment, int columns, int rows)
    {
        string commandLine = CommandLine(program, arguments);
        IntPtr block = Marshal.StringToHGlobalUni(string.Concat(environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value + "\0")) + "\0");
        if (!CreatePipe(out var inRead, out var inWrite, IntPtr.Zero, 0) || !CreatePipe(out var outRead, out var outWrite, IntPtr.Zero, 0)) throw new IOException("Cannot create the pipes of the terminal.", new Win32Exception());
        int hr = CreatePseudoConsole(new Coord { X = (short)columns, Y = (short)rows }, inRead, outWrite, 0, out var console);
        if (hr != 0)
        {
            inRead.Dispose(); inWrite.Dispose(); outRead.Dispose(); outWrite.Dispose();
            Marshal.FreeHGlobal(block);
            throw new IOException("The pseudo console could not be created (Windows 10 version 1809 or later is needed).", new Win32Exception(hr));
        }
        IntPtr list = IntPtr.Zero;
        try
        {
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            list = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) throw new Win32Exception();
            if (!UpdateProcThreadAttribute(list, 0, PseudoConsoleAttribute, console, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
            var startup = new StartupInfoEx { StartupInfo = new StartupInfo { cb = Marshal.SizeOf<StartupInfoEx>(), dwFlags = UseStdHandles }, lpAttributeList = list };
            if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, ExtendedStartupInfoPresent | CreateUnicodeEnvironment, block, workingDirectory, ref startup, out var info))
                throw new Win32Exception();
            CloseHandle(info.hThread);
            inRead.Dispose();
            outWrite.Dispose();
            Tools.ChildJob.AssignHandle(info.hProcess);
            return new ConPtyProcess(console, new FileStream(inWrite, FileAccess.Write, 1, false), new FileStream(outRead, FileAccess.Read, 4096, false), info.hProcess, info.dwProcessId);
        }
        catch (Win32Exception e)
        {
            ClosePseudoConsole(console);
            inRead.Dispose(); inWrite.Dispose(); outRead.Dispose(); outWrite.Dispose();
            throw new IOException($"Cannot start {program}: {e.Message}", e);
        }
        finally
        {
            Marshal.FreeHGlobal(block);
            if (list != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(list);
                Marshal.FreeHGlobal(list);
            }
        }
    }

    private static string CommandLine(string program, IReadOnlyList<string> arguments)
    {
        static string Quote(string arg)
        {
            if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '"']) < 0) return arg;
            var sb = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { sb.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                sb.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return sb.Append('\\', slashes * 2).Append('"').ToString();
        }
        return string.Join(' ', new[] { Quote(program) }.Concat(arguments.Select(Quote)));
    }

    private void ReadLoop()
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                int n = _output.Read(buffer, 0, buffer.Length);
                if (n <= 0) return;
                OutputReceived?.Invoke(buffer.AsMemory(0, n));
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or UnauthorizedAccessException) { /* the console was closed */ }
    }

    private void WaitForExit()
    {
        IntPtr process;
        lock (_gate) process = _process;
        if (process == IntPtr.Zero) return;
        WaitForSingleObject(process, -1);
        lock (_gate)
        {
            if (_process == IntPtr.Zero) return;
            GetExitCodeProcess(_process, out int code);
            _exitCode = code;
        }
        if (Interlocked.Exchange(ref _exitReported, 1) == 0 && !_disposed) Exited?.Invoke(_exitCode ?? 0);
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed || data.IsEmpty) return;
        try
        {
            _input.Write(data);
            _input.Flush();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { /* the program has ended */ }
    }

    public void Resize(int columns, int rows)
    {
        if (_disposed) return;
        ResizePseudoConsole(_console, new Coord { X = (short)Math.Clamp(columns, 1, short.MaxValue), Y = (short)Math.Clamp(rows, 1, short.MaxValue) });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            using var process = Process.GetProcessById(ProcessId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception) { /* already gone */ }
        // closing the console while the output is not read can block on older Windows: the reader runs until the pipe is closed
        var close = Task.Run(() => ClosePseudoConsole(_console));
        close.Wait(TimeSpan.FromSeconds(5));
        _reader.Join(2000);
        lock (_gate)
        {
            if (_process != IntPtr.Zero)
            {
                GetExitCodeProcess(_process, out int code);
                _exitCode = code;
                CloseHandle(_process);
                _process = IntPtr.Zero;
            }
        }
        _input.Dispose();
        _output.Dispose();
    }

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
    private static extern int ResizePseudoConsole(IntPtr console, Coord size);

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

    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr process, out int code);
}
