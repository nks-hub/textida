using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Input;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.App.Startup;

internal sealed partial class PagedMeasure
{
    private async Task RunJsonAsync()
    {
        Log("start", ("os", RuntimeInformation.OSDescription), ("arch", RuntimeInformation.OSArchitecture.ToString()),
            ("size", new FileInfo(_file).Length), ("backendWanted", "json"), ("cpus", Environment.ProcessorCount));
        byte[] original;
        using (var file = File.OpenRead(_file)) original = SHA256.HashData(file);
        await Frames(3);
        await Task.Delay(3000);
        Settle();
        Snapshot("baseline");
        app.Workbench.Set("files.largeFileThreshold", JsonValue.Create(1));
        long start = Stopwatch.GetTimestamp();
        _tab = window.OpenFile(_file, null);
        _backend = "json";
        double openMs = Ms(start);
        window.SetJsonPretty(_tab, true);
        var view = window.ActiveJson ?? throw new InvalidOperationException("The formatted JSON view did not open");
        await Frames(2);
        if (view.RenderedAt < start) throw new InvalidOperationException("The formatted JSON view did not draw a frame");
        samples.Append("json-open", _backend,
            [new(start, (view.RenderedAt - start) * 1000.0 / Stopwatch.Frequency, Ms(start), openMs, view.RenderMs)]);
        Log("json-open", ("openCallMs", openMs), ("drawnMs", (view.RenderedAt - start) * 1000.0 / Stopwatch.Frequency),
            ("frameMs", Ms(start)), ("viewportBytes", view.Outline!.BytesRead), ("lines", view.Outline.State.Lines));
        Log("window", ("hwnd", window.TryGetPlatformHandle()?.Handle.ToInt64()), ("title", window.Title),
            ("visible", window.IsVisible), ("active", window.IsActive), ("opacity", window.Opacity));
        Snapshot("json shown");
        if (!await window.SaveAsync(_tab)) throw new IOException("Saving the unchanged JSON failed");
        using (var file = File.OpenRead(_file))
            if (!original.AsSpan().SequenceEqual(SHA256.HashData(file))) throw new IOException("Saving the unchanged JSON changed its bytes");
        Log("json-save-unchanged", ("hashMatches", true));
        var wait = Stopwatch.StartNew();
        while (view.Outline.State.Lines < 10_000 && !view.Outline.State.Complete && wait.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(5);
        var lines = view.Outline.GetLines(0, 512);
        var value = lines.FirstOrDefault(l => l.Kind == JsonLineKind.Value && l.ValueKind == JsonTokenKind.String);
        if (value.ValueKind != JsonTokenKind.String) throw new InvalidOperationException("The measurement needs a string value near the start of the JSON");
        view.Focus();
        int step = 0;
        if (!_quick)
        {
            await Series("json-scroll", _keys, () => Press(step++ % 40 < 30 ? Key.PageDown : Key.PageUp));
            var container = lines.FirstOrDefault(l => l.Number > 0 && l.Kind == JsonLineKind.Open);
            if (container.Kind != JsonLineKind.Open) throw new InvalidOperationException("The measurement needs a nested object or array near the start of the JSON");
            await Series("json-fold", _keys, () =>
            {
                if (!view.ToggleFold(container.Number)) throw new InvalidOperationException("The container could not be folded");
            });
            if (view.Folds.IsFolded(container.Number)) view.ToggleFold(container.Number);
            await Series("json-key", _keys, async () =>
            {
                if (!await view.GoToKeyAsync("n")) throw new InvalidOperationException("The next key was not found");
                var line = view.Outline.GetLine(view.CaretLine) ?? throw new InvalidOperationException("The key has no formatted line");
                if (view.Outline.PathOf(line) is not { } path || !path.EndsWith(".n", StringComparison.Ordinal))
                    throw new InvalidOperationException("The jump reached an unexpected JSON path");
            });
        }
        var rendered = view.Outline.Render(value);
        var span = rendered.Spans.First(s => s.Kind == JsonTokenKind.String);
        view.MoveCaret(value.Number, span.Column + 1);
        long at = view.SourceOffset;
        int edits = _quick ? 1 : _keys;
        await Series("json-type", edits, () => Type("x"));
        if (view.SourceOffset != at + edits) throw new InvalidOperationException("Typing did not advance the source offset by the inserted characters");
        byte[] expected = new byte[edits];
        expected.AsSpan().Fill((byte)'x');
        if (!_tab.Doc.Doc.ReadBytes(new(at, at + edits)).AsSpan().SequenceEqual(expected)) throw new InvalidOperationException("Typing changed unexpected bytes");
        int osEdits = 0;
        if (Env("TEXTIDA_MEASURE_OSINPUT") == "1")
        {
            await OsInput();
            osEdits = _keys;
            byte[] throughSystem = new byte[osEdits];
            throughSystem.AsSpan().Fill((byte)'k');
            if (view.SourceOffset != at + edits + osEdits || !_tab.Doc.Doc.ReadBytes(new(at + edits, at + edits + osEdits)).AsSpan().SequenceEqual(throughSystem))
                throw new InvalidOperationException("System input changed unexpected bytes or did not reach the formatted view");
        }
        var primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        int undos = 0;
        while (_tab.Doc.Doc.IsModified && undos++ <= edits + osEdits) Press(Key.Z, primary);
        if (_tab.Doc.Doc.IsModified) throw new InvalidOperationException("Undo did not restore the saved JSON");
        await Frames(2);
        Log("json-edit", ("inserted", edits), ("osInserted", osEdits), ("undoSteps", undos), ("undoClean", true),
            ("scannedBytes", view.Outline.ScannedBytes), ("indexComplete", view.Outline.State.Complete));
        Snapshot("json edited and undone");
        Done();
    }
}
