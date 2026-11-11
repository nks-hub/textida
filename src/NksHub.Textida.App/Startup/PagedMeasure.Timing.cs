using System.Runtime.InteropServices;

namespace NksHub.Textida.App.Startup;

internal sealed partial class PagedMeasure
{
    private bool measureTimerPeriod;

    private void BeginMeasureTimer()
    {
        if (Env("TEXTIDA_MEASURE_TIMER_1MS") != "1") return;
        if (!OperatingSystem.IsWindows() || Env("TEXTIDA_MEASURE_PRESENT") != "external"
            || Env("TEXTIDA_MEASURE_COMPOSITION") != "1")
            throw new InvalidOperationException("The timer experiment requires Windows external capture and batch tracing");
        uint status = TimeBeginPeriod(1);
        if (status != 0) throw new InvalidOperationException($"timeBeginPeriod(1) failed: {status}");
        measureTimerPeriod = true;
        Log("timer-period", ("requestedMs", 1), ("status", status), ("scope", "measurement process only; balanced before shutdown"));
    }

    private void EndMeasureTimer()
    {
        if (!measureTimerPeriod) return;
        measureTimerPeriod = false;
        uint status = TimeEndPeriod(1);
        Log("timer-period-end", ("requestedMs", 1), ("status", status));
        if (status != 0) throw new InvalidOperationException($"timeEndPeriod(1) failed: {status}");
    }

    [LibraryImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static partial uint TimeBeginPeriod(uint period);

    [LibraryImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static partial uint TimeEndPeriod(uint period);
}
