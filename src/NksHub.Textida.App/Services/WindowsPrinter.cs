using System.Runtime.InteropServices;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Prints pages through the Windows print dialog and GDI (PZ.2.2). The dialog (<c>PrintDlgEx</c>) gives the printer and its settings as a device context; the pages are laid out for that device
/// (its resolution and printable area) and drawn row by row in a fixed-width font with the colour of each run. Where the editor must not ask anything, <see cref="PrintTo"/> prints to a named
/// printer, which is also how the code is tested (the printer "Microsoft Print to PDF" writes a file).
/// </summary>
internal static partial class WindowsPrinter
{
    internal enum Outcome { Printed, Cancelled, Failed }

    private const uint PdReturnDc = 0x100, PdNoSelection = 0x4, PdNoPageNums = 0x8, PdNoCurrentPage = 0x800000, PdUseDevModeCopies = 0x40000, StartPageGeneral = 0xFFFFFFFF;
    private const int HorzRes = 8, VertRes = 10, LogPixelsX = 88, LogPixelsY = 90, PhysicalOffsetX = 112, PhysicalOffsetY = 113;

    [StructLayout(LayoutKind.Sequential)]
    private struct PrintDlgEx
    {
        public uint StructSize;
        public IntPtr Owner, DevMode, DevNames, Dc;
        public uint Flags, Flags2, ExclusionFlags, PageRanges, MaxPageRanges;
        public IntPtr PageRangesPointer;
        public uint MinPage, MaxPage, Copies;
        public IntPtr Instance, TemplateName, Callback;
        public uint PropertyPages;
        public IntPtr PropertyPagesPointer;
        public uint StartPage, ResultAction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DocInfo
    {
        public int Size;
        public IntPtr Name, Output, DataType;
        public int Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size32
    {
        public int Width, Height;
    }

    [LibraryImport("comdlg32.dll", EntryPoint = "PrintDlgExW")]
    private static partial int PrintDlgExW(ref PrintDlgEx dialog);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDCW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateDc(string driver, string device, string? output, IntPtr devMode);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(IntPtr dc, int index);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strike, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial uint SetTextColor(IntPtr dc, uint color);

    [LibraryImport("gdi32.dll")]
    private static partial int SetBkMode(IntPtr dc, int mode);

    [LibraryImport("gdi32.dll", EntryPoint = "TextOutW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TextOut(IntPtr dc, int x, int y, string text, int length);

    [LibraryImport("gdi32.dll", EntryPoint = "GetTextExtentPoint32W", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTextExtent(IntPtr dc, string text, int length, out Size32 size);

    [LibraryImport("gdi32.dll", EntryPoint = "StartDocW")]
    private static partial int StartDoc(IntPtr dc, ref DocInfo info);

    [LibraryImport("gdi32.dll")]
    private static partial int StartPage(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int EndPage(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int EndDoc(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int AbortDoc(IntPtr dc);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GlobalFree(IntPtr handle);

    private static uint ColorRef(int rgb) => (uint)(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));

    /// <summary>Shows the print dialog of Windows and prints with what the user chose. Must be called on the UI thread: the dialog needs a single-threaded apartment.</summary>
    public static (Outcome Outcome, string? Error) PrintWithDialog(IntPtr owner, string title, Func<PrintOptions, IReadOnlyList<PrintPage>> layout, bool lineNumbers)
    {
        var dialog = new PrintDlgEx
        {
            StructSize = (uint)Marshal.SizeOf<PrintDlgEx>(),
            Owner = owner,
            Flags = PdReturnDc | PdNoSelection | PdNoPageNums | PdNoCurrentPage | PdUseDevModeCopies,
            Copies = 1,
            StartPage = StartPageGeneral,
        };
        try
        {
            int hr = PrintDlgExW(ref dialog);
            if (hr != 0) return (Outcome.Failed, $"PrintDlgEx 0x{hr:X8}");
            if (dialog.ResultAction != 1 || dialog.Dc == IntPtr.Zero) return (Outcome.Cancelled, null);
            try { return Render(dialog.Dc, null, title, layout, lineNumbers); }
            finally { DeleteDC(dialog.Dc); }
        }
        finally
        {
            if (dialog.DevMode != IntPtr.Zero) GlobalFree(dialog.DevMode);
            if (dialog.DevNames != IntPtr.Zero) GlobalFree(dialog.DevNames);
        }
    }

    /// <summary>Prints without a dialog to the printer <paramref name="printer"/>; with <paramref name="outputFile"/> a printer that writes files (Microsoft Print to PDF) writes there and asks nothing.</summary>
    public static (Outcome Outcome, string? Error) PrintTo(string printer, string? outputFile, string title, Func<PrintOptions, IReadOnlyList<PrintPage>> layout, bool lineNumbers)
    {
        IntPtr dc = CreateDc("WINSPOOL", printer, null, IntPtr.Zero);
        if (dc == IntPtr.Zero) return (Outcome.Failed, $"The printer '{printer}' cannot be opened ({Marshal.GetLastPInvokeError()}).");
        try { return Render(dc, outputFile, title, layout, lineNumbers); }
        finally { DeleteDC(dc); }
    }

    private static (Outcome, string?) Render(IntPtr dc, string? outputFile, string title, Func<PrintOptions, IReadOnlyList<PrintPage>> layout, bool lineNumbers)
    {
        int dpiX = GetDeviceCaps(dc, LogPixelsX), dpiY = GetDeviceCaps(dc, LogPixelsY);
        int width = GetDeviceCaps(dc, HorzRes), height = GetDeviceCaps(dc, VertRes);
        int offsetX = GetDeviceCaps(dc, PhysicalOffsetX), offsetY = GetDeviceCaps(dc, PhysicalOffsetY);
        IntPtr font = CreateFont(-(int)Math.Round(9.0 * dpiY / 72), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0x31, "Consolas");
        IntPtr old = SelectObject(dc, font);
        try
        {
            if (!GetTextExtent(dc, "M", 1, out var cell) || cell.Width <= 0 || cell.Height <= 0) return (Outcome.Failed, "The size of a character could not be measured.");
            // half an inch from the edge of the paper, but never closer than the printer can reach
            int marginX = Math.Max(0, dpiX / 2 - offsetX), marginY = Math.Max(0, dpiY / 2 - offsetY);
            int rowHeight = cell.Height;
            var options = new PrintOptions(Math.Max(10, (width - 2 * marginX) / cell.Width), Math.Max(5, (height - 2 * marginY) / rowHeight - 2), lineNumbers);
            var pages = layout(options);

            IntPtr name = Marshal.StringToHGlobalUni(title.Length > 120 ? title[..120] : title);
            IntPtr output = outputFile is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(outputFile);
            try
            {
                var info = new DocInfo { Size = Marshal.SizeOf<DocInfo>(), Name = name, Output = output };
                if (StartDoc(dc, ref info) <= 0) return (Outcome.Failed, $"StartDoc failed ({Marshal.GetLastPInvokeError()}).");
                SetBkMode(dc, 1);
                foreach (var page in pages)
                {
                    if (StartPage(dc) <= 0)
                    {
                        AbortDoc(dc);
                        return (Outcome.Failed, "StartPage failed.");
                    }
                    SelectObject(dc, font);
                    SetBkMode(dc, 1);
                    SetTextColor(dc, 0);
                    TextOut(dc, marginX, marginY, title, title.Length);
                    string right = $"{page.Number} / {pages.Count}";
                    TextOut(dc, marginX + (options.Columns - right.Length) * cell.Width, marginY, right, right.Length);
                    int y = marginY + 2 * rowHeight;
                    foreach (var line in page.Lines)
                    {
                        int x = marginX;
                        foreach (var run in line.Runs)
                        {
                            SetTextColor(dc, ColorRef(run.Rgb));
                            TextOut(dc, x, y, run.Text, run.Text.Length);
                            x += run.Text.Length * cell.Width;
                        }
                        y += rowHeight;
                    }
                    if (EndPage(dc) <= 0)
                    {
                        AbortDoc(dc);
                        return (Outcome.Failed, "EndPage failed.");
                    }
                }
                return EndDoc(dc) > 0 ? (Outcome.Printed, null) : (Outcome.Failed, "EndDoc failed.");
            }
            finally
            {
                Marshal.FreeHGlobal(name);
                if (output != IntPtr.Zero) Marshal.FreeHGlobal(output);
            }
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(font);
        }
    }
}
