using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace NksHub.Textida.Core.Tools;

/// <summary>
/// The Windows job object that holds the children of this process. It has the limit KILL_ON_JOB_CLOSE and nothing else closes its handle, so when this process ends by any means
/// (the Task Manager, a crash) the system ends every process of the job; what a child starts joins the job too.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class ChildJob
{
    private const uint KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;

    // never closed on purpose: closing it is what kills the children
    private static readonly nint Job = Create();

    /// <summary>Puts a started process into the job. False when it could not be (it ended already, or a job it belongs to forbids it); the process then runs as without the job.</summary>
    internal static bool Assign(Process process)
    {
        if (Job == 0) return false;
        try { return AssignProcessToJobObject(Job, process.SafeHandle); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    private static nint Create()
    {
        var job = CreateJobObjectW(0, 0);
        if (job == 0) return 0;
        var limits = new ExtendedLimits { LimitFlags = KillOnJobClose };
        return SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()) ? job : 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
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
    private static partial bool SetInformationJobObject(nint job, int informationClass, ref ExtendedLimits information, uint size);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(nint job, SafeProcessHandle process);
}
