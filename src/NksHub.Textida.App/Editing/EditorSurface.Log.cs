using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaEdit.Folding;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Editing;

/// <summary>Full log mode on the editor surface (P9.4.2): hidden and colored ANSI escapes, folded runs of uninteresting lines, and stack trace frames that open their file.</summary>
internal sealed partial class EditorSurface
{
    private const int MaxLinkChars = 8192;

    private AnsiHideGenerator? _ansiHide;
    private AnsiColorizer? _ansi;
    private FoldingManager? _folding;
    private bool _ansiRaw;

    /// <summary>Raised when the user Ctrl+clicks a stack trace frame of a log.</summary>
    public event Action<StackTraceLink>? StackTraceClicked;

    private void InitLogFull()
    {
        _ansiHide = new AnsiHideGenerator();
        _ansi = new AnsiColorizer(TextArea.TextView);
        TextArea.TextView.ElementGenerators.Insert(0, _ansiHide);
        TextArea.TextView.LineTransformers.Add(_ansi);
        TextArea.AddHandler(PointerPressedEvent, OnLogLinkPressed, RoutingStrategies.Tunnel);
        ApplyAnsi();
    }

    /// <summary>Shows the escape sequences as they are in the file instead of their colors.</summary>
    public bool AnsiRaw
    {
        get => _ansiRaw;
        set
        {
            _ansiRaw = value;
            ApplyAnsi();
        }
    }

    private void ApplyAnsi()
    {
        if (_ansi is null || _ansiHide is null) return;
        bool on = Doc.IsLog && !_ansiRaw;
        _ansiHide.Enabled = on;
        _ansi.Enabled = on;
        TextArea.TextView.Redraw();
    }

    public bool HasFolds => _folding is not null && _folding.AllFoldings.Any();

    /// <summary>Folds each run into one placeholder row titled by <paramref name="title"/> (given the number of lines); the text is untouched and a click on the row opens it. Returns the number of runs folded.</summary>
    public int Fold(IReadOnlyList<LogFoldRange> ranges, Func<int, string> title)
    {
        ClearFolds();
        if (ranges.Count == 0) return 0;
        _folding = FoldingManager.Install(TextArea);
        foreach (var range in ranges)
        {
            var first = Document.GetLineByNumber(range.First + 1);
            var last = Document.GetLineByNumber(range.Last + 1);
            if (_folding.CreateFolding(first.Offset, last.EndOffset) is { } section)
            {
                section.Title = title(range.Count);
                section.IsFolded = true;
            }
        }
        return ranges.Count;
    }

    /// <summary>Opens every placeholder row and removes the folding.</summary>
    public void ClearFolds()
    {
        if (_folding is null) return;
        FoldingManager.Uninstall(_folding);
        _folding = null;
    }

    private string LineTextAt(int offset, out int lineOffset)
    {
        var line = Document.GetLineByOffset(Math.Clamp(offset, 0, Document.TextLength));
        lineOffset = line.Offset;
        return Document.GetText(line.Offset, Math.Min(line.Length, MaxLinkChars));
    }

    /// <summary>The stack trace frame under the caret, or null.</summary>
    public StackTraceLink? StackTraceAtCaret() => StackTraceLinks.At(LineTextAt(CaretOffset, out int start), CaretOffset - start);

    private void OnLogLinkPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!Doc.IsLog || !e.GetCurrentPoint(TextArea).Properties.IsLeftButtonPressed || !(e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))) return;
        if (OffsetAt(e) is not { } at || StackTraceLinks.At(LineTextAt(at, out int start), at - start) is not { } link) return;
        e.Handled = true;
        StackTraceClicked?.Invoke(link);
    }
}
