using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NksHub.Textida.App.Tests.RealAgents;

/// <summary>
/// Microsoft Word driven through COM, invisible, for the <c>RealWord</c> tests. It starts its own Word and quits it; it refuses to start when a Word of the user is running, because COM
/// would hand out that one and quitting it would close the documents of the user. The Word process is killed by its id if it stays after <c>Quit</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WordSession : IDisposable
{
    private readonly int _processId;
    private bool _disposed;

    private WordSession(dynamic application, int processId)
    {
        Application = application;
        _processId = processId;
    }

    public dynamic Application { get; }

    /// <summary>Why a test cannot use Word on this machine (not installed, or the user has it open); null when it can.</summary>
    public static string? WhyNot()
    {
        if (!OperatingSystem.IsWindows()) return "Word is driven through COM, which exists on Windows only.";
        if (Type.GetTypeFromProgID("Word.Application") is null) return "Microsoft Word is not installed (no Word.Application COM class).";
        if (Process.GetProcessesByName("WINWORD").Length > 0) return "Word is running; the test does not touch a Word the user has open.";
        return null;
    }

    public static WordSession Start()
    {
        dynamic application = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
        try
        {
            application.Visible = false;
            application.DisplayAlerts = 0; // wdAlertsNone
            // none was running before (WhyNot), so the one process of Word is ours
            using var started = Process.GetProcessesByName("WINWORD").OrderByDescending(p => p.StartTime).FirstOrDefault();
            return new WordSession(application, started?.Id ?? 0);
        }
        catch
        {
            try { application.Quit(0); } catch (COMException) { }
            throw;
        }
    }

    public string Version => (string)Application.Version;

    public dynamic NewDocument() => Application.Documents.Add();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            for (int i = (int)Application.Documents.Count; i >= 1; i--) Application.Documents.Item(i).Close(0); // wdDoNotSaveChanges
            Application.Quit(0);
        }
        catch (COMException) { /* Word is gone or busy: the process is ended below */ }
        try { Marshal.FinalReleaseComObject(Application); }
        catch (ArgumentException) { }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        if (_processId == 0) return;
        try
        {
            using var process = Process.GetProcessById(_processId);
            if (!process.WaitForExit(10_000)) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { /* already ended */ }
    }
}
