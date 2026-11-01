using System.Runtime.InteropServices;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The clipboard monitor (PZ.4.1): while the setting <c>editor.clipboardMonitor</c> is on, a text that another program copies is added to the clipboard history. It is off by default and only the user turns it on. Switching it on
/// takes the text that is on the clipboard at that moment as the starting point and does not collect it: only copies made after the switch are kept. Switching it off forgets the starting point, so a text copied while it was off is never collected later.
/// The history lives in memory only; nothing is written to disk or sent anywhere.
/// </summary>
internal sealed class ClipboardMonitor(Func<bool> enabled, Func<Task<string?>> read, Action<string> collect)
{
    private bool _armed;
    private string? _last;

    /// <summary>True while the monitor has a starting point, which is to say while it is on.</summary>
    public bool Armed => _armed;

    /// <summary>One look at the clipboard; the app calls it on a timer.</summary>
    public async Task PollAsync()
    {
        if (!enabled())
        {
            _armed = false;
            _last = null;
            return;
        }
        string? text;
        try { text = await read(); }
        catch (Exception e) when (e is ExternalException or InvalidOperationException or IOException or TimeoutException) { return; } // another program holds the clipboard: look again next time
        if (!_armed)
        {
            _armed = true;
            _last = text;
            return;
        }
        if (text is { Length: > 0 } && text != _last) collect(text);
        _last = text;
    }
}
