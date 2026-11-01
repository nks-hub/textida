using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NksHub.Textida.Core.Tools;

/// <summary>How much of the system a plugin process is kept away from (PZ.10.1). <see cref="Contained"/> is for the plugins that start programs or use the network themselves; <see cref="Strict"/> for the rest.</summary>
public enum ProcessSandboxLevel
{
    /// <summary>The process runs as any child of the editor does.</summary>
    Off,
    /// <summary>Windows: the process may not use the clipboard, change the display or the system parameters, switch desktops, exit Windows or reach windows of other processes. macOS: no listening sockets.</summary>
    Contained,
    /// <summary>As <see cref="Contained"/>, and the process may not start another program; on macOS it also may not open outgoing connections.</summary>
    Strict,
}

/// <summary>
/// An operating system limit on the process of a plugin. On Windows the process joins a job object of its own (inside the job that ends all children with the editor) with UI restrictions and, for
/// <see cref="ProcessSandboxLevel.Strict"/>, an active process limit of one. On macOS the program is started through <c>sandbox-exec</c> with a profile. Neither limits the files the process reads; that needs an
/// AppContainer or an App Sandbox with entitlements, which a program started by path cannot get. Nothing here throws: a limit that cannot be set leaves the process as it was, and the caller is told.
/// shortcut: the job is assigned after the process started, so a program the plugin starts in that first instant is outside the limit; upgrade to a suspended start (CreateProcess) if that matters.
/// </summary>
public static partial class ProcessSandbox
{
    /// <summary>True where this editor can set a limit at all (Windows and macOS).</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || (OperatingSystem.IsMacOS() && File.Exists(MacProgram));

    internal const string MacProgram = "/usr/bin/sandbox-exec";

    /// <summary>The sandbox profile of macOS for a level; null for <see cref="ProcessSandboxLevel.Off"/>.</summary>
    public static string? MacProfile(ProcessSandboxLevel level) => level switch
    {
        ProcessSandboxLevel.Contained => "(version 1)(allow default)(deny network-bind)(deny network-inbound)",
        ProcessSandboxLevel.Strict => "(version 1)(allow default)(deny network-bind)(deny network-inbound)(deny network-outbound)(deny process-fork)",
        _ => null,
    };

    /// <summary>Before the start: on macOS makes the start info run the program through <c>sandbox-exec</c>. Returns whether it did.</summary>
    public static bool Wrap(ProcessStartInfo info, ProcessSandboxLevel level)
    {
        if (!OperatingSystem.IsMacOS() || MacProfile(level) is not { } profile || !File.Exists(MacProgram)) return false;
        var arguments = info.ArgumentList.ToArray();
        string program = info.FileName;
        info.FileName = MacProgram;
        info.ArgumentList.Clear();
        info.ArgumentList.Add("-p");
        info.ArgumentList.Add(profile);
        info.ArgumentList.Add(program);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return true;
    }

    /// <summary>After the start: on Windows puts the process into a job object with the limits of the level. Returns whether the limits are set.</summary>
    public static bool Apply(Process process, ProcessSandboxLevel level) =>
        OperatingSystem.IsWindows() && level != ProcessSandboxLevel.Off && Job.Limit(process, level);

    [SupportedOSPlatform("windows")]
    private static partial class Job
    {
        private const uint ActiveProcessLimit = 0x8;
        private const int ExtendedLimitInformation = 9;
        private const int UiRestrictionsInformation = 4;
        private const uint AllUiLimits = 0xFF; // handles, both directions of the clipboard, system parameters, display settings, global atoms, desktops, exit windows

        internal static bool Limit(Process process, ProcessSandboxLevel level)
        {
            nint job = CreateJobObjectW(0, 0);
            if (job == 0) return false;
            try
            {
                var ui = new UiRestrictions { Class = AllUiLimits };
                if (!SetInformationJobObject(job, UiRestrictionsInformation, ref ui, (uint)Marshal.SizeOf<UiRestrictions>())) return false;
                if (level == ProcessSandboxLevel.Strict)
                {
                    var limits = new ExtendedLimits { LimitFlags = ActiveProcessLimit, ActiveProcessLimitCount = 1 };
                    if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) return false;
                }
                return AssignProcessToJobObject(job, process.SafeHandle);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; } // it ended already
            finally { CloseHandle(job); } // the limits stay as long as a process is in the job; no kill-on-close here, the job of the editor's children does that
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct UiRestrictions
        {
            public uint Class;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimitCount;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        [LibraryImport("kernel32.dll")]
        private static partial nint CreateJobObjectW(nint attributes, nint name);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(nint job, int informationClass, ref UiRestrictions information, uint size);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetInformationJobObject(nint job, int informationClass, ref ExtendedLimits information, uint size);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AssignProcessToJobObject(nint job, Microsoft.Win32.SafeHandles.SafeProcessHandle process);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(nint handle);
    }
}
