using System.Globalization;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Hex;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The tools of the hex view that work on the selection (P4.10): copy, paste and export as, fill and operations, checksums and hashes.
/// Each tool is a command that opens a menu or a prompt; the work itself is in the Core (<see cref="ByteFormats"/>, <see cref="HexOperations"/>,
/// <see cref="Checksums"/>), so these methods only ask, call and report.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>A menu entry of a hex tool: its text and what it does.</summary>
    internal readonly record struct HexToolItem(string Header, Func<Task> Run);

    private const int MaxClipboardChars = 0x3FFFFFDF; // the longest string .NET holds

    private static string FormatName(ByteFormat format) => Loc.Get(format switch
    {
        ByteFormat.HexSpaced => K.HexFormatHexSpaced,
        ByteFormat.HexPlain => K.HexFormatHexPlain,
        ByteFormat.CArray => K.HexFormatC,
        ByteFormat.CSharp => K.HexFormatCSharp,
        ByteFormat.Java => K.HexFormatJava,
        ByteFormat.Python => K.HexFormatPython,
        ByteFormat.Rust => K.HexFormatRust,
        ByteFormat.Go => K.HexFormatGo,
        ByteFormat.JavaScript => K.HexFormatJavaScript,
        ByteFormat.Pascal => K.HexFormatPascal,
        ByteFormat.Base64 => K.HexFormatBase64,
        ByteFormat.IntelHex => K.HexFormatIntelHex,
        ByteFormat.SRecord => K.HexFormatSRecord,
        _ => K.HexFormatHexDump,
    });

    private static string KindName(ByteTextKind kind) => Loc.Get(kind switch
    {
        ByteTextKind.Hex => K.HexPasteAsHex,
        ByteTextKind.Base64 => K.HexPasteAsBase64,
        _ => K.HexPasteAsArray,
    });

    private MenuFlyout? ShowHexToolMenu(IEnumerable<HexToolItem> items)
    {
        var flyout = new MenuFlyout();
        foreach (var item in items)
        {
            var entry = new MenuItem { Header = item.Header };
            var run = item.Run;
            entry.Click += (_, _) => _ = run();
            flyout.Items.Add(entry);
        }
        flyout.ShowAt(_hexRowItem);
        return flyout;
    }

    internal CommandOutcome HexToolMenu(Func<HexView, IReadOnlyList<HexToolItem>> items)
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        ShowHexToolMenu(items(hex));
        return CommandOutcome.Handled;
    }

    // ---- copy, paste and export as ----

    internal IReadOnlyList<HexToolItem> HexCopyAsItems(HexView hex) =>
        [.. ByteFormats.All.Select(f => new HexToolItem(FormatName(f), () => HexCopyAsAsync(hex, f)))];

    internal IReadOnlyList<HexToolItem> HexPasteAsItems(HexView hex) =>
        [.. new[] { ByteTextKind.Hex, ByteTextKind.Base64, ByteTextKind.CArray }.Select(k => new HexToolItem(KindName(k), () => HexPasteAsAsync(hex, k)))];

    internal IReadOnlyList<HexToolItem> HexExportItems(HexView hex) =>
        [new HexToolItem(Loc.Get(K.HexExportBinary), () => HexExportAsync(hex, null)), .. ByteFormats.All.Select(f => new HexToolItem(FormatName(f), () => HexExportAsync(hex, f)))];

    /// <summary>Text for the clipboard grows in a builder; one that would not fit a string stops here instead of exhausting memory.</summary>
    private sealed class ClipboardWriter : TextWriter
    {
        private readonly StringBuilder _text = new();
        public override Encoding Encoding => Encoding.Unicode;
        public override string ToString() => _text.ToString();

        private void Room(int more)
        {
            if ((long)_text.Length + more > MaxClipboardChars) throw new TextTooLargeException();
        }

        public override void Write(char value) { Room(1); _text.Append(value); }
        public override void Write(string? value) { Room(value?.Length ?? 0); _text.Append(value); }
        public override void Write(char[] buffer, int index, int count) { Room(count); _text.Append(buffer, index, count); }
        public override void Write(ReadOnlySpan<char> buffer) { Room(buffer.Length); _text.Append(buffer); }
    }

    private sealed class TextTooLargeException : Exception;

    /// <summary>Copies the selected bytes to the clipboard as text in the form.</summary>
    internal async Task HexCopyAsAsync(HexView hex, ByteFormat format)
    {
        var editor = hex.Editor;
        if (!editor.HasSelection)
        {
            SetStatusMessage(Loc.Get(K.HexNeedsSelection));
            return;
        }
        var range = editor.Selection;
        var snapshot = editor.Document.GetSnapshot();
        var cts = hex.Job = new CancellationTokenSource();
        string text;
        try
        {
            text = await Task.Run(() =>
            {
                var writer = new ClipboardWriter();
                ByteFormats.Write(writer, format, snapshot, range, range.Start, cts.Token);
                return writer.ToString();
            }, CancellationToken.None);
        }
        catch (ArgumentOutOfRangeException)
        {
            SetStatusMessage(Loc.Get(K.HexFormatAddress, ("format", FormatName(format))));
            return;
        }
        catch (TextTooLargeException)
        {
            SetStatusMessage(Loc.Get(K.HexCopyAsTooLarge));
            return;
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(Loc.Get(K.HexExportCancelled));
            return;
        }
        catch (IOException)
        {
            hex.FileChangedBehind();
            SetStatusMessage(Loc.Get(K.HexFindStale));
            return;
        }
        finally
        {
            hex.Job = null;
            cts.Dispose();
        }
        if (Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(text);
        SetStatusMessage(Loc.Get(K.HexCopyAsDone, ("bytes", range.Length), ("format", FormatName(format))));
    }

    /// <summary>Reads the clipboard text as the kind and pastes the bytes like a paste of bytes does.</summary>
    internal async Task HexPasteAsAsync(HexView hex, ByteTextKind kind)
    {
        if (Clipboard is not { } clipboard) return;
        string? text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatusMessage(Loc.Get(K.HexPasteEmpty));
            return;
        }
        if (!ByteFormats.TryParse(kind, text, out var bytes, out string reason))
        {
            SetStatusMessage(Loc.Get(K.HexPasteAsInvalid, ("kind", KindName(kind)), ("reason", reason)));
            return;
        }
        await hex.PasteBytesAsync(bytes);
    }

    /// <summary>Writes the selected bytes to a file the user picks: as they are (<paramref name="format"/> null) or as text in a form. Runs in the background; Esc cancels.</summary>
    internal async Task HexExportAsync(HexView hex, ByteFormat? format)
    {
        var editor = hex.Editor;
        if (!editor.HasSelection)
        {
            SetStatusMessage(Loc.Get(K.HexNeedsSelection));
            return;
        }
        var range = editor.Selection;
        string stem = Path.GetFileNameWithoutExtension(editor.Document.FilePath ?? "data");
        string? path = await _app.Dialogs.PickSaveFileAsync(this, Loc.Get(K.HexExportTitle), stem + (format is { } f ? ByteFormats.Extension(f) : ".bin"));
        if (path is null) return;
        var snapshot = editor.Document.GetSnapshot();
        var cts = hex.Job = new CancellationTokenSource();
        SetStatusMessage(Loc.Get(K.HexExportRunning, ("bytes", range.Length)));
        try
        {
            await Task.Run(() =>
            {
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
                if (format is { } form)
                {
                    using var writer = new StreamWriter(file, new UTF8Encoding(false), 1 << 16) { NewLine = "\n" };
                    ByteFormats.Write(writer, form, snapshot, range, range.Start, cts.Token);
                    return;
                }
                var buffer = new byte[1 << 20];
                for (long at = range.Start; at < range.End;)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    int n = snapshot.Read(at, buffer.AsSpan(0, (int)Math.Min(buffer.Length, range.End - at)));
                    if (n == 0) throw new EndOfStreamException();
                    file.Write(buffer, 0, n);
                    at += n;
                }
            }, CancellationToken.None);
            SetStatusMessage(Loc.Get(K.HexExportDone, ("bytes", range.Length), ("name", Path.GetFileName(path))));
        }
        catch (OperationCanceledException)
        {
            FileSwap.TryDelete(path);
            SetStatusMessage(Loc.Get(K.HexExportCancelled));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            FileSwap.TryDelete(path);
            SetStatusMessage(Loc.Get(K.HexExportFailed, ("reason", e is ArgumentOutOfRangeException ? Loc.Get(K.HexFormatAddress, ("format", FormatName(format ?? ByteFormat.IntelHex))) : e.Message)));
        }
        finally
        {
            hex.Job = null;
            cts.Dispose();
        }
    }

    // ---- fill ----

    internal IReadOnlyList<HexToolItem> HexFillItems(HexView hex) =>
    [
        new(Loc.Get(K.HexFillMenuPattern) + "...", () => HexFillPatternAsync(hex)),
        new(Loc.Get(K.HexFillMenuRepeat) + "...", () => HexFillRepeatAsync(hex)),
        new(Loc.Get(K.HexFillMenuRandom), () => { HexFillRandom(hex); return Task.CompletedTask; }),
        new(Loc.Get(K.HexFillMenuSequence) + "...", () => HexFillSequenceAsync(hex)),
    ];

    /// <summary>
    /// A pattern as typed in the fill prompt: hex bytes or text in quotes (in the charset of the character column), optionally followed by
    /// <c>*</c> and a count (<c>00 * 4K</c>, the same sizes as a jump). The problem is a sentence part for the invalid-pattern message.
    /// </summary>
    internal static (byte[] Pattern, long? Count, string? Problem) ParseFillQuery(HexView hex, string query)
    {
        string q = query.Trim();
        int star = q.StartsWith('"') ? q.IndexOf('*', Math.Max(1, q.LastIndexOf('"'))) : q.IndexOf('*');
        string patternText = star >= 0 ? q[..star].Trim() : q;
        byte[] pattern;
        if (patternText.Length >= 2 && patternText[0] == '"' && patternText[^1] == '"')
        {
            try { pattern = hex.Charset.Codec.Encode(patternText[1..^1]); }
            catch (UnrepresentableCharactersException e) { return ([], null, e.Message); }
        }
        else if (!HexEditor.TryParseHexText(patternText, out pattern)) return ([], null, patternText.Length == 0 ? query : patternText);
        if (pattern.Length == 0) return ([], null, query);
        if (star < 0) return (pattern, null, null);
        var doc = hex.Editor.Document;
        try
        {
            long count = OffsetExpression.Evaluate(q[(star + 1)..].Trim(), new OffsetContext(doc.ByteLength, hex.Caret, hex.Caret, null));
            return count > 0 ? (pattern, count, null) : ([], null, q[(star + 1)..].Trim());
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentOutOfRangeException or DivideByZeroException)
        {
            return ([], null, e.Message);
        }
    }

    private static string FillPreview(HexView hex, string text)
    {
        var (pattern, count, problem) = ParseFillQuery(hex, text);
        if (problem is not null) return Loc.Get(K.HexFillInvalid, ("reason", problem));
        long bytes = hex.Editor.HasSelection ? hex.Editor.Selection.Length : count ?? 0;
        return bytes == 0 ? Loc.Get(K.HexFillNeedsCount) : Loc.Get(K.HexFillPreview, ("bytes", bytes), ("length", pattern.Length));
    }

    internal async Task HexFillPatternAsync(HexView hex)
    {
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HexFillPatternTitle), Loc.Get(K.HexFillPatternLabel), "00", text => FillPreview(hex, text));
        if (answer is null) return;
        var (pattern, count, problem) = ParseFillQuery(hex, answer);
        if (problem is not null)
        {
            SetStatusMessage(Loc.Get(K.HexFillInvalid, ("reason", problem)));
            return;
        }
        if (!hex.Editor.HasSelection && count is null)
        {
            SetStatusMessage(Loc.Get(K.HexFillNeedsCount));
            return;
        }
        long bytes = hex.Editor.HasSelection ? hex.Editor.Selection.Length : count!.Value;
        FinishFill(hex, hex.Editor.Fill(pattern, count ?? 0), bytes);
    }

    private void FinishFill(HexView hex, HexResult result, long bytes)
    {
        hex.Do(result);
        if (result.Applied) SetStatusMessage(Loc.Get(K.HexFillDone, ("bytes", bytes)));
    }

    internal async Task HexFillRepeatAsync(HexView hex)
    {
        if (!hex.Editor.HasSelection)
        {
            SetStatusMessage(Loc.Get(K.HexNeedsSelection));
            return;
        }
        long length = hex.Editor.Selection.Length;
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HexFillMenuRepeat), Loc.Get(K.HexFillRepeatLabel), "1",
            text => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0
                ? Loc.Get(K.HexFillRepeatPreview, ("bytes", length * n))
                : Loc.Get(K.HexFillInvalid, ("reason", text)));
        if (answer is null) return;
        if (!int.TryParse(answer.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int copies) || copies < 1)
        {
            SetStatusMessage(Loc.Get(K.HexFillInvalid, ("reason", answer)));
            return;
        }
        FinishFill(hex, hex.Editor.RepeatSelection(copies), length * copies);
    }

    internal void HexFillRandom(HexView hex)
    {
        if (!hex.Editor.HasSelection)
        {
            SetStatusMessage(Loc.Get(K.HexNeedsSelection));
            return;
        }
        long length = hex.Editor.Selection.Length;
        FinishFill(hex, hex.Editor.FillRandom(), length);
    }

    /// <summary>The numbers of the sequence prompt: start, step, size in bits and an optional byte order.</summary>
    internal static bool TryParseSequence(string text, out long start, out long step, out int width, out bool bigEndian)
    {
        (start, step, width, bigEndian) = (0, 0, 0, false);
        string[] parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 3 or > 4 || !TryParseLong(parts[0], out start) || !TryParseLong(parts[1], out step)) return false;
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int bits) || bits is not (8 or 16 or 32 or 64)) return false;
        width = bits / 8;
        if (parts.Length == 4)
        {
            if (parts[3].Equals("be", StringComparison.OrdinalIgnoreCase)) bigEndian = true;
            else if (!parts[3].Equals("le", StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static bool TryParseLong(string text, out long value)
    {
        bool negative = text.StartsWith('-');
        string digits = negative || text.StartsWith('+') ? text[1..] : text;
        bool ok = digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(digits[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
            : long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        if (negative) value = -value;
        return ok;
    }

    internal async Task HexFillSequenceAsync(HexView hex)
    {
        if (!hex.Editor.HasSelection)
        {
            SetStatusMessage(Loc.Get(K.HexNeedsSelection));
            return;
        }
        long length = hex.Editor.Selection.Length;
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HexFillMenuSequence), Loc.Get(K.HexFillSequenceLabel), "0 1 32 le",
            text => TryParseSequence(text, out _, out _, out _, out _) ? Loc.Get(K.HexFillSequencePreview, ("bytes", length)) : Loc.Get(K.HexFillInvalid, ("reason", text)));
        if (answer is null) return;
        if (!TryParseSequence(answer, out long start, out long step, out int width, out bool bigEndian))
        {
            SetStatusMessage(Loc.Get(K.HexFillInvalid, ("reason", answer)));
            return;
        }
        FinishFill(hex, hex.Editor.FillSequence(start, step, width, bigEndian), length);
    }

    // ---- operations ----

    internal static string OperationName(HexOperation op, int amount = 0) => op switch
    {
        HexOperation.Xor => Loc.Get(K.HexOpXor),
        HexOperation.And => Loc.Get(K.HexOpAnd),
        HexOperation.Or => Loc.Get(K.HexOpOr),
        HexOperation.Not => Loc.Get(K.HexOpNot),
        HexOperation.Add => Loc.Get(K.HexOpAdd),
        HexOperation.ShiftLeft => Loc.Get(K.HexOpShiftLeft),
        HexOperation.ShiftRight => Loc.Get(K.HexOpShiftRight),
        HexOperation.RotateLeft => Loc.Get(K.HexOpRotateLeft),
        HexOperation.RotateRight => Loc.Get(K.HexOpRotateRight),
        _ => Loc.Get(K.HexOpSwap, ("n", amount)),
    };

    internal IReadOnlyList<HexToolItem> HexOperateItems(HexView hex)
    {
        HexToolItem Asks(HexOperation op) => new(OperationName(op) + "...", () => HexOperateAsync(hex, op));
        HexToolItem Swap(int n) => new(OperationName(HexOperation.SwapEndian, n), () => HexOperateAsync(hex, HexOperation.SwapEndian, n));
        return
        [
            Asks(HexOperation.Xor), Asks(HexOperation.And), Asks(HexOperation.Or),
            new(OperationName(HexOperation.Not), () => HexOperateAsync(hex, HexOperation.Not)),
            Asks(HexOperation.Add),
            Asks(HexOperation.ShiftLeft), Asks(HexOperation.ShiftRight), Asks(HexOperation.RotateLeft), Asks(HexOperation.RotateRight),
            Swap(2), Swap(4), Swap(8),
        ];
    }

    /// <summary>Hex bytes or text in quotes, as the operand prompt takes them.</summary>
    internal static bool TryParseOperand(HexView hex, string text, out byte[] operand, out string problem)
    {
        string t = text.Trim();
        problem = t;
        operand = [];
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
        {
            try { operand = hex.Charset.Codec.Encode(t[1..^1]); }
            catch (UnrepresentableCharactersException e) { problem = e.Message; return false; }
        }
        else if (!HexEditor.TryParseHexText(t, out operand)) return false;
        return operand.Length > 0;
    }

    /// <summary>Asks for what the operation needs (operand bytes, a number of bits), then applies it to the selection.</summary>
    internal async Task HexOperateAsync(HexView hex, HexOperation op, int amount = 0)
    {
        var editor = hex.Editor;
        if (!editor.HasSelection)
        {
            SetStatusMessage(Loc.Get(K.HexNeedsSelection));
            return;
        }
        long length = editor.Selection.Length;
        byte[]? operand = null;
        string name = OperationName(op, amount);
        if (op is HexOperation.Xor or HexOperation.And or HexOperation.Or or HexOperation.Add)
        {
            var answer = await _app.Dialogs.PromptAsync(this, name, Loc.Get(K.HexOpOperandLabel), "FF",
                text => TryParseOperand(hex, text, out _, out string problem) ? Loc.Get(K.HexOpPreview, ("bytes", length)) : Loc.Get(K.HexOpInvalid, ("reason", problem)));
            if (answer is null) return;
            if (!TryParseOperand(hex, answer, out operand, out string reason))
            {
                SetStatusMessage(Loc.Get(K.HexOpInvalid, ("reason", reason)));
                return;
            }
        }
        else if (op is HexOperation.ShiftLeft or HexOperation.ShiftRight or HexOperation.RotateLeft or HexOperation.RotateRight)
        {
            var answer = await _app.Dialogs.PromptAsync(this, name, Loc.Get(K.HexOpBitsLabel), "1",
                text => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 7
                    ? Loc.Get(K.HexOpPreview, ("bytes", length)) : Loc.Get(K.HexOpInvalid, ("reason", text)));
            if (answer is null) return;
            if (!int.TryParse(answer.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out amount) || amount is < 1 or > 7)
            {
                SetStatusMessage(Loc.Get(K.HexOpInvalid, ("reason", answer)));
                return;
            }
        }
        var result = editor.Operate(op, operand, amount);
        hex.Do(result);
        if (result.Applied) SetStatusMessage(Loc.Get(K.HexOpDone, ("name", name), ("bytes", length)));
    }

    // ---- checksums and hashes ----

    internal CommandOutcome HexChecksumMenu()
    {
        if (ActiveHex is not { } hex) return CommandOutcome.NotHandled;
        var flyout = new MenuFlyout();
        var crc = new MenuItem { Header = Loc.Get(K.HexSumCrcMenu) };
        foreach (var model in CrcModel.Catalog)
        {
            var spec = Checksums.Crc(model);
            var item = new MenuItem { Header = model.Name };
            item.Click += (_, _) => _ = HexChecksumAsync(hex, spec);
            crc.Items.Add(item);
        }
        var custom = new MenuItem { Header = Loc.Get(K.HexSumCustom) + "..." };
        custom.Click += (_, _) => _ = HexCustomCrcAsync(hex);
        crc.Items.Add(new Separator());
        crc.Items.Add(custom);
        flyout.Items.Add(crc);
        foreach (var spec in Checksums.All.Where(s => !CrcModel.Catalog.Any(m => m.Name.Equals(s.Name, StringComparison.Ordinal))))
        {
            var chosen = spec;
            var item = new MenuItem { Header = spec.Name };
            item.Click += (_, _) => _ = HexChecksumAsync(hex, chosen);
            flyout.Items.Add(item);
        }
        flyout.ShowAt(_hexRowItem);
        return CommandOutcome.Handled;
    }

    /// <summary>A CRC defined in the prompt: width in bits, then in hex the polynomial, start value, the two reflection flags and the final xor.</summary>
    internal static bool TryParseCrcModel(string text, out CrcModel model, out string problem)
    {
        model = new CrcModel("", 0, 0, 0, false, false, 0);
        problem = text;
        string[] p = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 6 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)) return false;
        if (!ulong.TryParse(p[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong poly)
            || !ulong.TryParse(p[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong init)
            || !ulong.TryParse(p[5], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong xor)
            || p[3] is not ("0" or "1") || p[4] is not ("0" or "1")) return false;
        model = new CrcModel($"CRC-{width}/{poly:X}", width, poly, init, p[3] == "1", p[4] == "1", xor);
        if (model.Problem is { } why)
        {
            problem = why;
            return false;
        }
        return true;
    }

    internal async Task HexCustomCrcAsync(HexView hex)
    {
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HexSumCustom), Loc.Get(K.HexSumCustomLabel), "16 8005 0000 1 1 0000",
            text => TryParseCrcModel(text, out _, out string problem) ? "" : Loc.Get(K.HexSumCustomInvalid, ("reason", problem)));
        if (answer is null) return;
        if (!TryParseCrcModel(answer, out var model, out string reason))
        {
            SetStatusMessage(Loc.Get(K.HexSumCustomInvalid, ("reason", reason)));
            return;
        }
        await HexChecksumAsync(hex, Checksums.Crc(model));
    }

    /// <summary>
    /// Calculates the checksum of the selection, or of the whole file when nothing is selected, in the background (Esc cancels, the status line
    /// shows the progress) and shows the value with the choices to copy it or compare it with a pasted one.
    /// </summary>
    internal async Task HexChecksumAsync(HexView hex, ChecksumSpec spec)
    {
        var editor = hex.Editor;
        var doc = editor.Document;
        var range = editor.HasSelection ? editor.Selection : new ByteRange(0, doc.ByteLength);
        string scope = editor.HasSelection
            ? Loc.Get(K.HexSumScopeSelection, ("bytes", range.Length), ("start", hex.OffsetText(range.Start)))
            : Loc.Get(K.HexSumScopeFile, ("bytes", range.Length));
        var snapshot = doc.GetSnapshot();
        var cts = hex.Job = new CancellationTokenSource();
        var progress = new Progress<long>(done => SetStatusMessage(Loc.Get(K.HexSumRunning, ("name", spec.Name), ("percent", range.Length == 0 ? 100 : done * 100 / range.Length))));
        string value;
        try
        {
            value = await Task.Run(() => Checksums.Compute(spec, snapshot, range, progress, cts.Token), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            SetStatusMessage(Loc.Get(K.HexSumCancelled));
            return;
        }
        catch (IOException)
        {
            hex.FileChangedBehind();
            SetStatusMessage(Loc.Get(K.HexFindStale));
            return;
        }
        finally
        {
            hex.Job = null;
            cts.Dispose();
        }
        SetStatusMessage("");
        string title = Loc.Get(K.HexSumResult, ("name", spec.Name), ("scope", scope));
        string note = "";
        while (true)
        {
            var request = new ChoiceRequest(Loc.Get(K.HexSumTitle), note.Length > 0 ? title + "\n\n" + note : title,
                [new DialogButton(Loc.Get(K.HexSumCopy), IsDefault: true), new DialogButton(Loc.Get(K.HexSumCompare)), new DialogButton(Loc.Get(K.HexChangesClose), IsCancel: true)],
                [value]);
            int choice = await _app.Dialogs.ChooseAsync(this, request);
            if (choice == 0)
            {
                if (Clipboard is { } clipboard) await clipboard.SetTextAsync(value);
                SetStatusMessage(Loc.Get(K.HexSumCopied, ("name", spec.Name)));
                return;
            }
            if (choice != 1) return;
            var pasted = await _app.Dialogs.PromptAsync(this, Loc.Get(K.HexSumCompareTitle), Loc.Get(K.HexSumCompareLabel), "");
            if (pasted is not null) note = Loc.Get(Checksums.SameValue(value, pasted) ? K.HexSumMatch : K.HexSumDiffers);
        }
    }
}
