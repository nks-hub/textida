using Avalonia;

namespace NksHub.Textida.App.Startup;

internal static class MeasureRendering
{
    internal static AppBuilder Configure(AppBuilder builder)
    {
        if (!OperatingSystem.IsWindows() || !PagedMeasure.Requested) return builder;
        string? mode = Environment.GetEnvironmentVariable("TEXTIDA_MEASURE_WIN32_COMPOSITION");
        if (string.IsNullOrEmpty(mode)) return builder;
        if (Environment.GetEnvironmentVariable("TEXTIDA_MEASURE_PRESENT") != "external"
            || Environment.GetEnvironmentVariable("TEXTIDA_MEASURE_COMPOSITION") != "1")
            throw new InvalidOperationException("A forced composition mode requires external frame capture and batch tracing");
        var composition = mode switch
        {
            "winui" => Win32CompositionMode.WinUIComposition,
            "direct" => Win32CompositionMode.DirectComposition,
            _ => throw new ArgumentException("Unknown measurement composition mode", nameof(mode))
        };
        // A failed requested backend must not silently become a different benchmark.
        return builder.With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.AngleEgl], CompositionMode = [composition] });
    }
}
