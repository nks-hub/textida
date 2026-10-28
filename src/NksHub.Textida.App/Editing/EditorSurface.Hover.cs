using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using NksHub.Textida.App.Services;

namespace NksHub.Textida.App.Editing;

/// <summary>A description a plugin gave for a stretch of text: <see cref="Start"/> to <see cref="End"/> are text offsets of the view.</summary>
internal readonly record struct HoverText(int Start, int End, string Text);

/// <summary>
/// Hover descriptions: once the pointer has rested on text for a moment, the provider (set by the window while the tab is a file a plugin describes) is asked what the text at that
/// place is, and the answer is shown as the tooltip of the text, together with the message of a diagnostic there. The core shows it; a plugin never draws.
/// </summary>
internal sealed partial class EditorSurface
{
    private Func<int, Task<HoverText?>>? _hoverProvider;
    private DispatcherTimer? _hoverTimer;
    private HoverText? _hover;
    private int _hoverAt = -1;
    private int _hoverRequest;

    /// <summary>Asks for the description at a text offset; null when there is none. Setting it the first time starts the watching of the pointer.</summary>
    public Func<int, Task<HoverText?>>? HoverProvider
    {
        get => _hoverProvider;
        set
        {
            bool first = _hoverProvider is null && value is not null;
            _hoverProvider = value;
            if (value is null) ClearHover();
            if (!first) return;
            TextArea.TextView.PointerMoved += OnHoverPointerMoved;
            TextArea.TextView.PointerExited += (_, _) => ClearHover();
            Document.TextChanged += (_, _) => _hover = null;
        }
    }

    /// <summary>The description shown now, or null.</summary>
    internal HoverText? Hover => _hover;

    private void ClearHover()
    {
        _hoverTimer?.Stop();
        _hoverRequest++;
        _hoverAt = -1;
        if (_hover is null) return;
        _hover = null;
        ToolTip.SetTip(TextArea.TextView, null);
    }

    /// <summary>The tooltip for the text at <paramref name="at"/>: the message of a diagnostic and the description, whichever are there.</summary>
    private string? TipAt(int at)
    {
        string? diagnostic = DiagnosticAt(at)?.Message;
        string? description = _hover is { } h && at >= h.Start && at < h.End ? h.Text : null;
        return diagnostic is not null && description is not null ? diagnostic + "\n\n" + description : diagnostic ?? description;
    }

    private void OnHoverPointerMoved(object? sender, PointerEventArgs e)
    {
        if (OffsetAt(e) is not { } at)
        {
            ClearHover();
            return;
        }
        _hoverAt = at;
        if (_hover is { } shown && at >= shown.Start && at < shown.End) return;
        _hover = null;
        var view = TextArea.TextView;
        var tip = TipAt(at);
        if (!Equals(ToolTip.GetTip(view), tip)) ToolTip.SetTip(view, tip);
        if (_hoverTimer is null)
        {
            _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.HoverDelayMilliseconds) };
            _hoverTimer.Tick += (_, _) => _ = AskHoverAsync();
        }
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private async Task AskHoverAsync()
    {
        _hoverTimer?.Stop();
        if (_hoverProvider is not { } provider || _hoverAt < 0) return;
        int at = _hoverAt, request = ++_hoverRequest;
        HoverText? found;
        try { found = await provider(Math.Min(at, Document.TextLength)); }
        catch (Exception e) when (e is InvalidOperationException or OperationCanceledException or NksHub.Textida.Rpc.RpcException) { found = null; }
        if (request != _hoverRequest || at != _hoverAt || found is not { } text || text.End > Document.TextLength) return; // the pointer or the text moved on while the plugin answered
        _hover = text;
        if (at >= text.Start && at < text.End) ToolTip.SetTip(TextArea.TextView, TipAt(at));
    }
}
