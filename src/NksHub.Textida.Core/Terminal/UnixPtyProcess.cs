using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace NksHub.Textida.Core.Terminal;

/// <summary>
/// A program on a Unix pty (macOS and Linux). <c>forkpty</c> makes the pty, forks and makes the child its session leader with the pty as its terminal; the child does nothing but <c>chdir</c> and <c>execve</c> with arguments and environment
/// that were prepared before the fork, because a managed process must not run anything else in a forked child. The process group is ended with the terminal: <see cref="Dispose"/> sends it SIGHUP, then SIGKILL after a second.
/// </summary>
[UnsupportedOSPlatform("windows")]
internal sealed class UnixPtyProcess : IPseudoTerminal
{
    private const int SigHup = 1, SigKill = 9;
    private static readonly nuint WindowSizeRequest = OperatingSystem.IsMacOS() ? 0x80087467u : 0x5414u; // TIOCSWINSZ

    private readonly int _master;
    private readonly int _pid;
    private readonly Thread _reader;
    private readonly object _gate = new();
    private bool _disposed;
    private int? _exitCode;
    private int _exitReported;

    private UnixPtyProcess(int master, int pid)
    {
        _master = master;
        _pid = pid;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "pty-reader" };
        _reader.Start();
        new Thread(WaitForExit) { IsBackground = true, Name = "pty-wait" }.Start();
    }

    public int ProcessId => _pid;
    public event Action<ReadOnlyMemory<byte>>? OutputReceived;
    public event Action<int>? Exited;
    public bool HasExited => ExitCode is not null;

    public int? ExitCode
    {
        get
        {
            lock (_gate) return _exitCode;
        }
    }

    internal static UnixPtyProcess Start(string program, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string> environment, int columns, int rows)
    {
        // everything the child needs is made here, before the fork
        var argv = Pointers([program, .. arguments]);
        var envp = Pointers(environment.Select(p => p.Key + "=" + p.Value));
        IntPtr path = Marshal.StringToCoTaskMemUTF8(program), cwd = Marshal.StringToCoTaskMemUTF8(workingDirectory);
        var size = new WindowSize { Rows = (ushort)rows, Columns = (ushort)columns };
        try
        {
            int pid = ForkPty(out int master, IntPtr.Zero, IntPtr.Zero, ref size);
            if (pid < 0) throw new IOException("forkpty failed (errno " + Marshal.GetLastPInvokeError() + ").");
            if (pid == 0)
            {
                // the child: no allocation, no managed work
                ChangeDirectory(cwd);
                ExecuteVe(path, argv, envp);
                Exit(127);
            }
            return new UnixPtyProcess(master, pid);
        }
        finally
        {
            Free(argv);
            Free(envp);
            Marshal.FreeCoTaskMem(path);
            Marshal.FreeCoTaskMem(cwd);
        }
    }

    private static IntPtr Pointers(IEnumerable<string> strings)
    {
        var list = strings.ToList();
        IntPtr array = Marshal.AllocCoTaskMem((list.Count + 1) * IntPtr.Size);
        for (int i = 0; i < list.Count; i++) Marshal.WriteIntPtr(array, i * IntPtr.Size, Marshal.StringToCoTaskMemUTF8(list[i]));
        Marshal.WriteIntPtr(array, list.Count * IntPtr.Size, IntPtr.Zero);
        return array;
    }

    private static void Free(IntPtr array)
    {
        for (int i = 0; Marshal.ReadIntPtr(array, i * IntPtr.Size) is var item && item != IntPtr.Zero; i++) Marshal.FreeCoTaskMem(item);
        Marshal.FreeCoTaskMem(array);
    }

    private unsafe void ReadLoop()
    {
        var buffer = new byte[16 * 1024];
        while (true)
        {
            nint n;
            fixed (byte* p = buffer) n = Read(_master, p, (nuint)buffer.Length);
            if (n < 0 && Marshal.GetLastPInvokeError() == 4) continue; // EINTR
            if (n <= 0) return; // EIO: the last holder of the terminal has gone
            OutputReceived?.Invoke(buffer.AsMemory(0, (int)n));
        }
    }

    private void WaitForExit()
    {
        int status;
        while (WaitPid(_pid, out status, 0) < 0 && Marshal.GetLastPInvokeError() == 4) { }
        int code = (status & 0x7F) == 0 ? (status >> 8) & 0xFF : 128 + (status & 0x7F);
        lock (_gate) _exitCode = code;
        if (Interlocked.Exchange(ref _exitReported, 1) == 0 && !_disposed) Exited?.Invoke(code);
    }

    public unsafe void Write(ReadOnlySpan<byte> data)
    {
        if (_disposed) return;
        fixed (byte* p = data)
        {
            int done = 0;
            while (done < data.Length)
            {
                nint n = WriteFd(_master, p + done, (nuint)(data.Length - done));
                if (n < 0 && Marshal.GetLastPInvokeError() == 4) continue;
                if (n <= 0) return;
                done += (int)n;
            }
        }
    }

    public void Resize(int columns, int rows)
    {
        if (_disposed) return;
        var size = new WindowSize { Rows = (ushort)Math.Clamp(rows, 1, ushort.MaxValue), Columns = (ushort)Math.Clamp(columns, 1, ushort.MaxValue) };
        // on Apple silicon the third argument of the variadic ioctl travels on the stack, after the eight register arguments
        if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64) IoctlApple(_master, WindowSizeRequest, 0, 0, 0, 0, 0, 0, ref size);
        else Ioctl(_master, WindowSizeRequest, ref size);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Kill(-_pid, SigHup);
        Close(_master);
        for (int i = 0; i < 20 && !HasExited; i++) Thread.Sleep(50);
        if (!HasExited)
        {
            Kill(-_pid, SigKill);
            Kill(_pid, SigKill);
        }
        _reader.Join(2000);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowSize { public ushort Rows, Columns, XPixel, YPixel; }

    [DllImport("libc", EntryPoint = "forkpty", SetLastError = true)]
    private static extern int ForkPty(out int master, IntPtr name, IntPtr termios, ref WindowSize size);

    [DllImport("libc", EntryPoint = "chdir")]
    private static extern int ChangeDirectory(IntPtr path);

    [DllImport("libc", EntryPoint = "execve")]
    private static extern int ExecuteVe(IntPtr path, IntPtr argv, IntPtr envp);

    [DllImport("libc", EntryPoint = "_exit")]
    private static extern void Exit(int code);

    [DllImport("libc", EntryPoint = "read", SetLastError = true)]
    private static extern unsafe nint Read(int fd, byte* buffer, nuint count);

    [DllImport("libc", EntryPoint = "write", SetLastError = true)]
    private static extern unsafe nint WriteFd(int fd, byte* buffer, nuint count);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int Close(int fd);

    [DllImport("libc", EntryPoint = "kill")]
    private static extern int Kill(int pid, int signal);

    [DllImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static extern int WaitPid(int pid, out int status, int options);

    [DllImport("libc", EntryPoint = "ioctl")]
    private static extern int Ioctl(int fd, nuint request, ref WindowSize size);

    [DllImport("libc", EntryPoint = "ioctl")]
    private static extern int IoctlApple(int fd, nuint request, nint a2, nint a3, nint a4, nint a5, nint a6, nint a7, ref WindowSize size);
}
