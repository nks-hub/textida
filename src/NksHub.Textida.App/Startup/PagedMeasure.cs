using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Startup;

/// <summary>
/// Measurement mode of the large-file path (P1.2.9, P1.2.2): <c>textida --smoke</c> with <c>TEXTIDA_MEASURE=&lt;log&gt;</c> opens
/// <c>TEXTIDA_MEASURE_FILE</c> in the real window, edits it through the keyboard events of the focused view and writes one JSON line per
/// measurement. <c>TEXTIDA_MEASURE_BACKEND</c> is <c>paged</c> or <c>text</c> (forces AvaloniaEdit), <c>TEXTIDA_MEASURE_QUICK=1</c> stops after the
/// first edit, <c>TEXTIDA_MEASURE_SAVE=1</c> saves the edited file, <c>TEXTIDA_MEASURE_OSINPUT=1</c> also sends keys through the operating system.
/// </summary>
internal sealed partial class PagedMeasure(App app, MainWindow window, IClassicDesktopStyleApplicationLifetime desktop)
{
    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    public static bool Requested => Env("TEXTIDA_MEASURE") is not null;

    private readonly string _log = Env("TEXTIDA_MEASURE")!;
    private readonly MeasureSamples samples = new(Env("TEXTIDA_MEASURE")!);
    private readonly string _file = Env("TEXTIDA_MEASURE_FILE") ?? throw new InvalidOperationException("TEXTIDA_MEASURE_FILE is not set");
    private readonly bool _paged = (Env("TEXTIDA_MEASURE_BACKEND") ?? "paged") == "paged";
    private readonly bool _quick = Env("TEXTIDA_MEASURE_QUICK") == "1";
    private readonly int _keys = int.Parse(Env("TEXTIDA_MEASURE_KEYS") ?? "300", CultureInfo.InvariantCulture);
    private string _backend = "none";
    private DocTab? _tab;

    public async Task RunAsync()
    {
        var watchdog = Task.Delay(TimeSpan.FromMinutes(20)).ContinueWith(_ => Fail("timeout after 20 min"), TaskScheduler.Default);
        try
        {
            BeginMeasureTimer();
            if (Env("TEXTIDA_MEASURE_PRESENT") is "1" or "external" && Env("TEXTIDA_MEASURE_BACKEND") != "json")
                throw new NotSupportedException("Desktop sampling is available for the JSON measurement backend");
            Log("samples", ("runId", samples.RunId), ("csv", Path.GetFileName(samples.Path)),
                ("qpcFrequency", Stopwatch.Frequency), ("timing", "draw-complete"),
                ("desktopRequested", Env("TEXTIDA_MEASURE_PRESENT") is "1" or "external"),
                ("desktopMode", Env("TEXTIDA_MEASURE_PRESENT") ?? "none"),
                ("commit", Services.BuildStamp.Commit), ("runtime", RuntimeInformation.FrameworkDescription));
            if (Env("TEXTIDA_MEASURE_BACKEND") == "json") { await RunJsonAsync(); return; }
            Log("start", ("os", RuntimeInformation.OSDescription), ("arch", RuntimeInformation.OSArchitecture.ToString()), ("file", _file),
                ("size", new FileInfo(_file).Length), ("backendWanted", _paged ? "paged" : "text"), ("cpus", Environment.ProcessorCount));
            await Frames(3);
            await Task.Delay(3000);
            Settle();
            Snapshot("baseline");

            app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(_paged ? 1 : 2048));
            long t0 = Stopwatch.GetTimestamp();
            _tab = window.OpenFile(_file, null);
            _backend = _tab.Doc.IsLarge ? "paged" : "avaloniaedit";
            double openCall = Ms(t0);
            Snapshot("opened");
            if (_tab.Paged is { } pv)
            {
                long sampled = Stopwatch.GetTimestamp();
                while (!pv.Index.Complete)
                {
                    await Task.Delay(5);
                    if (Ms(sampled) < 2000) continue;
                    Memory("indexing", "none"); // private commit and working set while the index is built (P3.1.9)
                    sampled = Stopwatch.GetTimestamp();
                }
            }
            double indexed = Ms(t0);
            await Frames(2);
            double shown = Ms(t0);
            Log("open", ("backend", _backend), ("openCallMs", openCall), ("indexedMs", indexed), ("firstFrameMs", shown), ("lines", _tab.Paged?.Index.Lines ?? _tab.Editor!.Document.LineCount));
            Snapshot("opened+indexed");

            if (_tab.Paged is { } through)
            {
                // a fast pass over the whole file: what the block cache keeps afterwards (P3.1.9)
                long length = _tab.Doc.Doc.ByteLength;
                int step = 0;
                await Series("jumpThrough", 200, () => through.GoToOffset(length * ++step / 200));
                through.GoToOffset(0);
                await Frames(2);
                Snapshot("jumped");
            }

            _tab.View.Focus();
            await Frames(2);
            var first = await Measure(() => Type("a"));
            samples.Append("firstEdit", _backend, [new(first.Start, first.Ui, first.Frame, first.Handler, first.Draw, first.Tick, Desktop: first.Desktop)]);
            Log("firstEdit", ("backend", _backend), ("openToEditedMs", shown + (first.Ui >= 0 ? first.Ui : first.Frame)), ("openToEditedFrameMs", shown + first.Frame),
                ("uiMs", first.Ui), ("frameMs", first.Frame), ("handlerMs", first.Handler), ("drawMs", first.Draw));
            Snapshot("first edit");
            if (_quick)
            {
                Done();
                return;
            }

            await Series("type", _keys, () => Type("x"));
            Snapshot("typed");
            await Series("enter", 30, () => Press(Key.Enter));
            await Series("backspace", 30, () => Press(Key.Back));
            await Series("left", 60, () => Press(Key.Left));
            await Series("down", 60, () => Press(Key.Down));
            await Series("up", 60, () => Press(Key.Up));
            await Series("pagedown", 60, () => Press(Key.PageDown));
            await Series("pageup", 30, () => Press(Key.PageUp));
            Snapshot("moved");
            var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            await Series("undo", 40, () => Press(Key.Z, primary));
            await Series("redo", 20, () => Press(Key.Z, primary | KeyModifiers.Shift));
            Snapshot("undone");

            // far from the start: the edit splits the original and the char map is read in the middle of the file
            long lines = _tab.Paged?.Index.Lines ?? _tab.Editor!.Document.LineCount;
            _tab.GoToLine((int)(lines / 2));
            await Frames(2);
            await Series("typeMiddle", 100, () => Type("m"));
            await Series("typeMiddleEnter", 20, () => Press(Key.Enter));
            Snapshot("middle");

            if (Env("TEXTIDA_MEASURE_OSINPUT") == "1") await OsInput();

            if (Env("TEXTIDA_MEASURE_SAVE") == "1") await SaveAsync();
            Done();
        }
        catch (Exception e)
        {
            try { EndMeasureTimer(); }
            catch (Exception timerError) { Log("timer-period-error", ("message", timerError.ToString())); }
            try { await WriteCompositionTraceAsync(); }
            catch (Exception traceError) { Log("composition-trace-error", ("message", traceError.ToString())); }
            Log("error", ("message", e.ToString()));
            Fail(e.Message);
        }
        finally
        {
            GC.KeepAlive(watchdog);
        }
    }

    private void Done()
    {
        EndMeasureTimer();
        Snapshot("end");
        Log("done");
        desktop.Shutdown(0);
    }

    private async Task SaveAsync()
    {
        var doc = _tab!.Doc.Doc;
        long length = doc.ByteLength;
        string expected = HashDocument(doc);
        long t0 = Stopwatch.GetTimestamp();
        bool saved = await window.SaveAsync(_tab);
        double saveMs = Ms(t0);
        Snapshot("saved");
        long onDisk = new FileInfo(_file).Length;
        string actual = Convert.ToHexString(SHA256.HashData(File.OpenRead(_file)));
        double reindex = 0;
        if (_tab.Paged is { } pv)
        {
            while (!pv.Index.Complete) await Task.Delay(5);
            reindex = Ms(t0) - saveMs;
        }
        Log("save", ("ok", saved), ("saveMs", saveMs), ("reindexMs", reindex), ("lengthMatches", onDisk == length), ("hashMatches", actual == expected));
        Snapshot("saved+indexed");
    }

    private static string HashDocument(Core.Documents.Document doc)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        for (long at = 0; at < doc.ByteLength;)
        {
            int n = doc.Read(at, buffer);
            hash.AppendData(buffer, 0, n);
            at += n;
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // ---- input ----

    private Control Target => FocusTarget();

    private Control FocusTarget() => window.FocusManager?.GetFocusedElement() as Control ?? (_tab!.Surface?.TextArea as Control ?? _tab.View);

    private void Type(string text) =>
        Target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = Target });

    private void Press(Key key, KeyModifiers mods = KeyModifiers.None)
    {
        var target = Target;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target, PhysicalKey = Enum.TryParse<PhysicalKey>(key.ToString(), out var p) ? p : PhysicalKey.None });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = mods, Source = target });
    }

    /// <summary>
    /// Runs one input action. <c>Ui</c> is the time until the paged view had drawn the new state, <c>Frame</c> the time until two animation frames passed,
    /// <c>Handler</c> the time the input handlers took and <c>Draw</c> the time of the draw itself: the work on the UI thread without waiting for the frame clock.
    /// </summary>
    private Task<(double Ui, double Frame, double Handler, double Draw, double Tick, long Start, DesktopFrame? Desktop)> Measure(Action action)
        => Measure(() => { action(); return Task.CompletedTask; });

    private async Task<(double Ui, double Frame, double Handler, double Draw, double Tick, long Start, DesktopFrame? Desktop)> Measure(Func<Task> action)
    {
        var paged = _tab!.Paged;
        var observation = desktopProbe?.Begin();
        long t0 = Stopwatch.GetTimestamp();
        await action();
        double handler = Ms(t0);
        desktopProbe?.Release();
        await Frames(1);
        double tick = Ms(t0);
        double ui = -1, draw = -1;
        long renderedAt = window.ActiveJson?.RenderedAt ?? paged?.RenderedAt ?? 0;
        if (renderedAt >= t0)
        {
            ui = (renderedAt - t0) * 1000.0 / Stopwatch.Frequency;
            draw = window.ActiveJson?.RenderMs ?? paged?.RenderMs ?? -1;
        }
        await Frames(1);
        double frame = Ms(t0);
        DesktopFrame? seen = observation is null ? null : await observation;
        return (ui, frame, handler, draw, tick, t0, seen);
    }

    private Task Series(string name, int count, Action action)
        => Series(name, count, () => { action(); return Task.CompletedTask; });

    private async Task Series(string name, int count, Func<Task> action)
    {
        List<double> ui = [], frame = [], work = [], lastDraw = [], desktop = [];
        List<MeasureSample> rows = new(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                long start = Stopwatch.GetTimestamp();
                var jsonView = window.ActiveJson;
                bool traceSample = i < 3 || i >= count / 2 && i < count / 2 + 3 || i >= count - 3;
                List<(long Start, long End, long Revision, double Scroll)>? draws = jsonView is not null && Env("TEXTIDA_MEASURE_TRACE") == "1" && traceSample ? [] : null;
                void Record(long begin, long end) => draws!.Add((begin, end, jsonView!.Document.Doc.Revision, jsonView.HorizontalScroll));
                if (draws is not null) jsonView!.Drawn += Record;
                (double u, double f, double h, double d, double tick, long startedAt, DesktopFrame? seen) result;
                try { result = await Measure(action); }
                finally { if (draws is not null) jsonView!.Drawn -= Record; }
                var (u, f, h, d, tick, startedAt, seen) = result;
                if (draws is not null)
                {
                    Log("json-frame-trace", ("name", name), ("sample", i), ("handlerMs", h), ("firstTickMs", tick),
                        ("paints", string.Join(";", draws.Select(p => string.Create(CultureInfo.InvariantCulture,
                            $"{(p.Start - start) * 1000.0 / Stopwatch.Frequency:0.00}-{(p.End - start) * 1000.0 / Stopwatch.Frequency:0.00} rev={p.Revision} scroll={p.Scroll:0.0}")))));
                }
                if (u >= 0) { ui.Add(u); work.Add(h + d); }
                double? last = window.ActiveJson is { } json && json.LastRenderedAt >= startedAt
                    ? (json.LastRenderedAt - startedAt) * 1000.0 / Stopwatch.Frequency : null;
                if (last is { } value) lastDraw.Add(value);
                frame.Add(f);
                rows.Add(new(startedAt, u, f, h, d, tick, last, Desktop: seen));
                RequireDesktop(seen);
                if (seen?.Since(startedAt) is { } observedMs) desktop.Add(observedMs);
            }
        }
        finally { samples.Append(name, _backend, rows); }
        Log("series", ("name", name), ("backend", _backend), ("n", count), ("uiN", ui.Count), ("work", Pct(work)),
            ("ui", Pct(ui)), ("lastDraw", Pct(lastDraw)), ("frame", Pct(frame)), ("desktopN", desktop.Count), ("desktop", Pct(desktop)));
    }

    private static string Pct(List<double> v)
    {
        if (v.Count == 0) return "";
        v.Sort();
        double P(double q) => v[Math.Min(v.Count - 1, (int)Math.Ceiling(q * v.Count) - 1)];
        return string.Create(CultureInfo.InvariantCulture, $"p50={P(.5):0.0} p95={P(.95):0.0} p99={P(.99):0.0} max={v[^1]:0.0}");
    }

    private Task Frames(int count)
    {
        var tcs = new TaskCompletionSource();
        void Step(int left) => window.RequestAnimationFrame(_ => { if (left <= 1) tcs.TrySetResult(); else Step(left - 1); });
        Step(count);
        return tcs.Task;
    }

    /// <summary>Keys through the operating system into the focused window: the real keyboard path below Avalonia.</summary>
    private async Task OsInput()
    {
        var paged = _tab!.Paged;
        var json = window.ActiveJson;
        if (paged is null && json is null) return;
        (json as Control ?? _tab.View).Focus();
        nint hwnd = OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } handle ? handle.Handle : 0;
        if (hwnd != 0) OsKeys.Focus(hwnd);
        await Frames(2);
        var ui = new List<double>();
        var desktop = new List<double>();
        var paths = new Dictionary<string, int>();
        int delivered = 0;
        int count = json is null ? 40 : _keys;
        List<MeasureSample> rows = new(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                if (json is not null && !window.IsActive) throw new InvalidOperationException("The formatted JSON window lost focus during system input");
                long before = _tab.Doc.Doc.Revision;
                var observation = desktopProbe?.Begin(before + 1);
                long t0 = Stopwatch.GetTimestamp();
                bool sent = OsKeys.SendChar('k', hwnd);
                paths[OsKeys.LastPath] = paths.GetValueOrDefault(OsKeys.LastPath) + 1;
                for (int w = 0; w < 200 && _tab.Doc.Doc.Revision == before; w++) await Task.Delay(5);
                if (_tab.Doc.Doc.Revision == before)
                {
                    DesktopFrame? failed = observation is null ? null : await observation;
                    rows.Add(new(t0, InputPath: OsKeys.LastPath, Sent: sent, Delivered: false, Desktop: failed));
                    Log("osInput", ("path", OsKeys.LastPath), ("sent", sent), ("delivered", false));
                    return;
                }
                await Frames(2);
                delivered++;
                double frameMs = Ms(t0);
                long drawn = json?.RenderedAt ?? paged!.RenderedAt;
                double? elapsed = drawn >= t0 ? (drawn - t0) * 1000.0 / Stopwatch.Frequency : null;
                if (elapsed is { } value) ui.Add(value);
                DesktopFrame? seen = observation is null ? null : await observation;
                rows.Add(new(t0, elapsed, FrameMs: frameMs, InputPath: OsKeys.LastPath, Sent: sent, Delivered: true, Desktop: seen));
                RequireDesktop(seen);
                if (seen?.Since(t0) is { } observedMs) desktop.Add(observedMs);
            }
        }
        finally { samples.Append("osInput", _backend, rows); }
        if (json is not null && ui.Count != count) throw new InvalidOperationException("Some JSON system input has no rendered frame");
        Log("osInput", ("backend", _backend), ("path", OsKeys.LastPath), ("paths", string.Join(",", paths.Select(p => p.Key + "=" + p.Value))),
            ("delivered", delivered), ("uiN", ui.Count), ("ui", Pct(ui)), ("desktopN", desktop.Count), ("desktop", Pct(desktop)));
    }

    // ---- memory ----

    private static void Settle()
    {
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);
    }

    /// <summary>Logs the memory twice: as it is, and after a forced collection (what the process keeps, without garbage that has not been collected yet).</summary>
    private void Snapshot(string moment)
    {
        Memory(moment, "none");
        if (moment == "opened") return;
        Settle();
        Memory(moment, "forced");
    }

    private void Memory(string moment, string gc)
    {
        using var p = Process.GetCurrentProcess();
        double Mib(long b) => b / 1048576.0;
        var (footprint, peakFootprint) = OperatingSystem.IsMacOS() ? OsKeys.MacFootprint() : (0L, 0L);
        var pv = _tab?.Paged;
        Log("mem", ("moment", moment), ("gc", gc), ("backend", _backend), ("indexing", pv is null ? "n/a" : pv.Index.Complete ? "complete" : "running"),
            ("privateMiB", Mib(OperatingSystem.IsMacOS() ? footprint : p.PrivateMemorySize64)), ("workingSetMiB", Mib(p.WorkingSet64)),
            ("peakWorkingSetMiB", Mib(p.PeakWorkingSet64)), ("peakCommitMiB", Mib(OperatingSystem.IsMacOS() ? peakFootprint : p.PeakPagedMemorySize64)),
            ("gcHeapMiB", Mib(GC.GetTotalMemory(false))), ("gcCommittedMiB", Mib(GC.GetGCMemoryInfo().TotalCommittedBytes)),
            ("mappedMiB", Mib(pv?.MappedResidentBytes ?? 0)));
    }

    // ---- log ----

    private static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

    private void Log(string kind, params (string Key, object Value)[] fields)
    {
        var sb = new StringBuilder("{\"t\":\"").Append(kind).Append('"');
        foreach (var (key, value) in fields)
        {
            sb.Append(",\"").Append(key).Append("\":");
            switch (value)
            {
                case string s: sb.Append('"').Append(s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)).Append('"'); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(d.ToString("0.###", CultureInfo.InvariantCulture)); break;
                default: sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
            }
        }
        File.AppendAllText(_log, sb.Append("}\n").ToString());
    }

    private void Fail(string reason)
    {
        Console.Error.WriteLine("MEASURE_FAIL " + reason);
        Console.Error.Flush();
        Environment.Exit(1);
    }
}

/// <summary>Operating system calls of the measurement mode: keys through the input queue and the footprint of a macOS process.</summary>
internal static partial class OsKeys
{
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Vk;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
    }

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, ref Input input, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);

    /// <summary>How the last key reached the window: SendInput (system input queue), WM_CHAR (posted to the window), CGEventPost (macOS).</summary>
    public static string LastPath { get; private set; } = "";

    [LibraryImport("libproc.dylib")]
    private static partial int proc_pid_rusage(int pid, int flavor, ref ulong buffer);

    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static partial nint CGEventCreateKeyboardEvent(nint source, ushort key, [MarshalAs(UnmanagedType.Bool)] bool down);

    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static partial void CGEventKeyboardSetUnicodeString(nint ev, nint length, ref ushort text);

    [LibraryImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static partial void CGEventPost(uint tap, nint ev);

    public static void Focus(nint hwnd) => SetForegroundWindow(hwnd);

    /// <summary>
    /// One typed character through the operating system. Windows: the input queue first; a session that has no input desktop (disconnected
    /// remote session) refuses that, then the character is posted to the window as WM_CHAR, which its window procedure handles like typed text.
    /// </summary>
    public static bool SendChar(char c, nint hwnd)
    {
        if (OperatingSystem.IsWindows())
        {
            var down = new Input { Type = 1, Scan = c, Flags = 4 }; // KEYEVENTF_UNICODE
            var up = new Input { Type = 1, Scan = c, Flags = 4 | 2 }; // | KEYEVENTF_KEYUP
            if (SendInput(1, ref down, 40) == 1 && SendInput(1, ref up, 40) == 1) { LastPath = "SendInput"; return true; }
            LastPath = "WM_CHAR";
            return hwnd != 0 && PostMessage(hwnd, 0x0102, c, 0);
        }
        if (OperatingSystem.IsMacOS())
        {
            LastPath = "CGEventPost";
            ushort unit = c;
            foreach (bool isDown in new[] { true, false })
            {
                nint ev = CGEventCreateKeyboardEvent(0, 0, isDown);
                if (ev == 0) return false;
                CGEventKeyboardSetUnicodeString(ev, 1, ref unit);
                CGEventPost(0, ev); // kCGHIDEventTap
            }
            return true;
        }
        return false;
    }

    /// <summary>Physical footprint of this process and its lifetime peak, what Activity Monitor calls Memory.</summary>
    public static (long Current, long Peak) MacFootprint()
    {
        var buffer = new ulong[64];
        return proc_pid_rusage(Environment.ProcessId, 4, ref buffer[0]) == 0 ? ((long)buffer[9], (long)buffer[30]) : (0, 0);
    }
}
