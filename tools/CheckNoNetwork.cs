// Runs a program (the published, NativeAOT Textida with --smoke) and fails when its process tree opens a socket to a non-loopback address.
// The in-process NetWatch cannot do this: NativeAOT has no startup hooks. This watcher stays outside and asks the operating system.
//
//   dotnet run tools/CheckNoNetwork.cs -- <program> [args]     run the program under the watch (fresh profile, so no update check)
//   dotnet run tools/CheckNoNetwork.cs -- --selftest           prove that the watcher sees a remote connect and ignores a loopback one
//
// Windows: GetExtendedTcpTable / GetExtendedUdpTable (owning PID) plus Toolhelp snapshots for the process tree, every 2 ms.
// Linux:   /proc/net/{tcp,tcp6,udp,udp6} joined to the socket inodes in /proc/<pid>/fd for the tree, every 2 ms.
// macOS:   `lsof -nP -i -a -p <pids>` for the tree found with `ps`, back to back (one lsof run takes tens of milliseconds).
// Loopback endpoints, listening sockets and unconnected UDP sockets are allowed. Anything else fails with the PID, process name and endpoint.
//
// shortcut: this is polling. A connection that opens and closes between two polls is missed (a refused or instantly failing connect, a one-packet UDP send on Linux/macOS),
// and on Windows the UDP table has no remote address, so only a UDP socket bound to a concrete non-loopback address is reported. Upgrade to ETW (Windows), eBPF (Linux) or a network namespace without a route if this is ever not enough.
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

if (args.Length == 0)
{
    Console.Error.WriteLine("CheckNoNetwork: usage: <program> [args] | --selftest");
    return 2;
}
if (args[0] == "--child") return Child(args[1]);
if (args[0] == "--selftest") return SelfTest();

var profile = Path.Combine(Path.GetTempPath(), "textida-nonet-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(profile);
try
{
    var (code, hits) = Watch(args[0], args[1..], new Dictionary<string, string> { ["TEXTIDA_PROFILE"] = profile });
    if (hits.Count > 0)
    {
        foreach (var h in hits) Console.Error.WriteLine($"CheckNoNetwork: FAIL pid {h.Pid} {h.Name} {h.Proto} {h.Endpoint}");
        return 1;
    }
    if (code != 0)
    {
        Console.Error.WriteLine($"CheckNoNetwork: the program exited with {code}.");
        return 3;
    }
    Console.WriteLine("CheckNoNetwork: no non-loopback socket seen.");
    return 0;
}
finally
{
    try { Directory.Delete(profile, true); } catch (IOException) { }
}

static int SelfTest()
{
    var self = Environment.ProcessPath!;
    var dll = System.Reflection.Assembly.GetEntryAssembly()!.Location;
    string[] Launch(string mode) => Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? [dll, "--child", mode] : ["--child", mode];

    var (c1, loop) = Watch(self, Launch("loopback"), []);
    Console.WriteLine($"selftest loopback child: exit {c1}, reported {loop.Count}");
    var (c2, remote) = Watch(self, Launch("remote"), []);
    Console.WriteLine($"selftest remote child:   exit {c2}, reported {remote.Count}");
    foreach (var h in remote) Console.WriteLine($"  reported: pid {h.Pid} {h.Name} {h.Proto} {h.Endpoint}");

    var ok = c1 == 0 && loop.Count == 0 && remote.Any(h => h.Endpoint.StartsWith("192.0.2.1:9", StringComparison.Ordinal));
    Console.WriteLine(ok ? "selftest OK" : "selftest FAILED");
    return ok ? 0 : 1;
}

// The child of the self-test: a loopback connect that stays open for 1.5 s, or a connect to a TEST-NET address that times out.
static int Child(string mode)
{
    if (mode == "loopback")
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        using var c = new TcpClient();
        c.Connect(IPAddress.Loopback, ((IPEndPoint)l.LocalEndpoint).Port);
        Thread.Sleep(1500);
        return 0;
    }
    using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    var task = s.ConnectAsync(IPAddress.Parse("192.0.2.1"), 9);
    try { task.Wait(3000); } catch (AggregateException) { }
    return 0;
}

static (int ExitCode, List<Hit> Hits) Watch(string program, IEnumerable<string> programArgs, Dictionary<string, string> env)
{
    var info = new ProcessStartInfo(program) { UseShellExecute = false };
    foreach (var a in programArgs) info.ArgumentList.Add(a);
    foreach (var (k, v) in env) info.Environment[k] = v;

    var hits = new Dictionary<string, Hit>();
    var tree = new HashSet<int>();
    var names = new Dictionary<int, string>();
    IPlatform os = OperatingSystem.IsWindows() ? new WindowsPlatform() : OperatingSystem.IsMacOS() ? new MacPlatform() : new LinuxPlatform();
    using var process = Process.Start(info) ?? throw new InvalidOperationException($"Cannot start {program}.");
    tree.Add(process.Id);
    names[process.Id] = Path.GetFileName(program);

    void Poll()
    {
        os.GrowTree(tree, names);
        foreach (var c in os.Connections(tree))
        {
            var key = $"{c.Pid} {c.Proto} {c.Endpoint}";
            if (!hits.ContainsKey(key)) hits[key] = new Hit(c.Pid, names.GetValueOrDefault(c.Pid, "?"), c.Proto, c.Endpoint);
        }
    }

    while (!process.HasExited)
    {
        Poll();
        os.Pause();
    }
    process.WaitForExit();
    return (process.ExitCode, [.. hits.Values]);
}

record Hit(int Pid, string Name, string Proto, string Endpoint);

record Conn(int Pid, string Proto, string Endpoint);

static class Net
{
    /// <summary>True for an address that may be contacted: loopback, or no remote at all.</summary>
    public static bool Allowed(IPAddress a)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        return IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any);
    }

    public static string Format(IPAddress a, int port) => a.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{a}]:{port}" : $"{a}:{port}";
}

interface IPlatform
{
    /// <summary>Adds every child of a process in <paramref name="tree"/> to the tree and remembers the names.</summary>
    void GrowTree(HashSet<int> tree, Dictionary<int, string> names);

    /// <summary>The connections of the tree that go to a non-loopback endpoint.</summary>
    IEnumerable<Conn> Connections(HashSet<int> tree);

    void Pause();
}

static class TreeMath
{
    public static void Grow(HashSet<int> tree, Dictionary<int, int> parentOf, Dictionary<int, string> nameOf, Dictionary<int, string> names)
    {
        bool grew;
        do
        {
            grew = false;
            foreach (var (pid, parent) in parentOf)
                if (!tree.Contains(pid) && tree.Contains(parent))
                {
                    tree.Add(pid);
                    grew = true;
                }
        } while (grew);
        foreach (var pid in tree) if (nameOf.TryGetValue(pid, out var n)) names[pid] = n;
    }
}

sealed class WindowsPlatform : IPlatform
{
    [DllImport("iphlpapi.dll")] static extern int GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, int reserved);
    [DllImport("iphlpapi.dll")] static extern int GetExtendedUdpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, int reserved);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId, Threads, ParentProcessId;
        public int PriClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }

    private IntPtr _buffer = Marshal.AllocHGlobal(1 << 16);
    private int _size = 1 << 16;

    public WindowsPlatform() => timeBeginPeriod(1);

    public void Pause() => Thread.Sleep(2);

    public void GrowTree(HashSet<int> tree, Dictionary<int, string> names)
    {
        var parentOf = new Dictionary<int, int>();
        var nameOf = new Dictionary<int, string>();
        var snap = CreateToolhelp32Snapshot(2, 0);
        if (snap == new IntPtr(-1)) return;
        try
        {
            var e = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (var ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e))
            {
                parentOf[(int)e.ProcessId] = (int)e.ParentProcessId;
                nameOf[(int)e.ProcessId] = e.ExeFile;
            }
        }
        finally { CloseHandle(snap); }
        TreeMath.Grow(tree, parentOf, nameOf, names);
    }

    // Reads one table into the shared buffer and returns the row count; rows start at offset 4.
    private int Read(bool tcp, int family)
    {
        for (var i = 0; i < 5; i++)
        {
            var size = _size;
            var rc = tcp ? GetExtendedTcpTable(_buffer, ref size, false, family, 5, 0) : GetExtendedUdpTable(_buffer, ref size, false, family, 1, 0);
            if (rc == 0) return Marshal.ReadInt32(_buffer);
            if (rc != 122) return 0;
            _buffer = Marshal.ReAllocHGlobal(_buffer, size + 4096);
            _size = size + 4096;
        }
        return 0;
    }

    private static int Port(int v) => ((v & 0xFF) << 8) | ((v >> 8) & 0xFF);

    private IPAddress V6(int offset)
    {
        var b = new byte[16];
        Marshal.Copy(_buffer + offset, b, 0, 16);
        return new IPAddress(b);
    }

    public IEnumerable<Conn> Connections(HashSet<int> tree)
    {
        var found = new List<Conn>();
        var n = Read(true, 2);
        for (var i = 0; i < n; i++)
        {
            var row = 4 + i * 24;
            var pid = Marshal.ReadInt32(_buffer, row + 20);
            if (!tree.Contains(pid) || Marshal.ReadInt32(_buffer, row) == 2) continue;
            var remote = new IPAddress((uint)Marshal.ReadInt32(_buffer, row + 12));
            if (!Net.Allowed(remote)) found.Add(new Conn(pid, "tcp", Net.Format(remote, Port(Marshal.ReadInt32(_buffer, row + 16)))));
        }
        n = Read(true, 23);
        for (var i = 0; i < n; i++)
        {
            var row = 4 + i * 56;
            var pid = Marshal.ReadInt32(_buffer, row + 52);
            if (!tree.Contains(pid) || Marshal.ReadInt32(_buffer, row + 48) == 2) continue;
            var remote = V6(row + 24);
            if (!Net.Allowed(remote)) found.Add(new Conn(pid, "tcp6", Net.Format(remote, Port(Marshal.ReadInt32(_buffer, row + 44)))));
        }
        n = Read(false, 2);
        for (var i = 0; i < n; i++)
        {
            var row = 4 + i * 12;
            var pid = Marshal.ReadInt32(_buffer, row + 8);
            var local = new IPAddress((uint)Marshal.ReadInt32(_buffer, row));
            if (tree.Contains(pid) && !Net.Allowed(local)) found.Add(new Conn(pid, "udp", "bound " + Net.Format(local, Port(Marshal.ReadInt32(_buffer, row + 4)))));
        }
        n = Read(false, 23);
        for (var i = 0; i < n; i++)
        {
            var row = 4 + i * 28;
            var pid = Marshal.ReadInt32(_buffer, row + 24);
            var local = V6(row);
            if (tree.Contains(pid) && !Net.Allowed(local)) found.Add(new Conn(pid, "udp6", "bound " + Net.Format(local, Port(Marshal.ReadInt32(_buffer, row + 20)))));
        }
        return found;
    }
}

sealed class LinuxPlatform : IPlatform
{
    public void Pause() => Thread.Sleep(2);

    public void GrowTree(HashSet<int> tree, Dictionary<int, string> names)
    {
        var parentOf = new Dictionary<int, int>();
        var nameOf = new Dictionary<int, string>();
        foreach (var dir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(dir), out var pid)) continue;
            try
            {
                var stat = File.ReadAllText(dir + "/stat");
                var open = stat.IndexOf('(');
                var close = stat.LastIndexOf(')');
                var rest = stat[(close + 2)..].Split(' ');
                parentOf[pid] = int.Parse(rest[1]);
                nameOf[pid] = stat[(open + 1)..close];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // the process ended meanwhile
        }
        TreeMath.Grow(tree, parentOf, nameOf, names);
    }

    private static IPAddress Parse(string hex)
    {
        var bytes = new List<byte>();
        for (var i = 0; i < hex.Length; i += 8) bytes.AddRange(BitConverter.GetBytes(Convert.ToUInt32(hex.Substring(i, 8), 16)));
        return new IPAddress([.. bytes]);
    }

    public IEnumerable<Conn> Connections(HashSet<int> tree)
    {
        var owner = new Dictionary<string, int>();
        foreach (var pid in tree)
        {
            try
            {
                foreach (var fd in Directory.EnumerateFileSystemEntries($"/proc/{pid}/fd"))
                    if (new FileInfo(fd).LinkTarget is { } t && t.StartsWith("socket:[", StringComparison.Ordinal)) owner[t[8..^1]] = pid;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        var found = new List<Conn>();
        // shortcut: reads the tables of this network namespace; a child that unshares its own namespace is not seen
        foreach (var proto in new[] { "tcp", "tcp6", "udp", "udp6" })
        {
            string[] lines;
            try { lines = File.ReadAllLines("/proc/net/" + proto); }
            catch (IOException) { continue; }
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (f.Length < 10 || !owner.TryGetValue(f[9], out var pid)) continue;
                if (proto.StartsWith("tcp", StringComparison.Ordinal) && f[3] == "0A") continue; // LISTEN
                var remote = f[2].Split(':');
                var addr = Parse(remote[0]);
                if (!Net.Allowed(addr)) found.Add(new Conn(pid, proto, Net.Format(addr, Convert.ToInt32(remote[1], 16))));
            }
        }
        return found;
    }
}

sealed class MacPlatform : IPlatform
{
    public void Pause() { } // one lsof run is the interval

    private static string Run(string file, params string[] args)
    {
        var info = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    public void GrowTree(HashSet<int> tree, Dictionary<int, string> names)
    {
        var parentOf = new Dictionary<int, int>();
        var nameOf = new Dictionary<int, string>();
        foreach (var line in Run("ps", "-axo", "pid=,ppid=,comm=").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 3) continue;
            parentOf[int.Parse(f[0])] = int.Parse(f[1]);
            nameOf[int.Parse(f[0])] = Path.GetFileName(f[2].Trim());
        }
        TreeMath.Grow(tree, parentOf, nameOf, names);
    }

    public IEnumerable<Conn> Connections(HashSet<int> tree)
    {
        var found = new List<Conn>();
        var pid = 0;
        var proto = "";
        foreach (var line in Run("lsof", "-nP", "-i", "-a", "-p", string.Join(',', tree), "-FpPn").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line[0] == 'p') pid = int.Parse(line[1..]);
            else if (line[0] == 'P') proto = line[1..].ToLowerInvariant();
            else if (line[0] == 'n' && line.IndexOf("->", StringComparison.Ordinal) is var arrow and >= 0)
            {
                var remote = line[(arrow + 2)..];
                var colon = remote.LastIndexOf(':');
                if (colon > 0 && IPAddress.TryParse(remote[..colon].Trim('[', ']'), out var addr) && !Net.Allowed(addr))
                    found.Add(new Conn(pid, proto, remote));
            }
        }
        return found;
    }
}
