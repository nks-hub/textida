using System.Diagnostics;
using Avalonia.Threading;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Keeps the tokens of what one view shows up to date. It owns a <see cref="CheckpointSyntaxSession"/> of the document, asks it for the
/// visible lines (plus a margin) in short slices on the UI thread and hands a finished result to the view; an unfinished one is
/// continued in the background, and the view keeps what it showed until then. Session queries and document edits both run on the UI
/// thread, which is what the session requires. The grammar follows <see cref="OpenDocument.Language"/>.
/// </summary>
internal sealed class SyntaxFeeder : IDisposable
{
    private const int MaxRows = 1024;
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(6);
    private static readonly TimeSpan ShowPendingAfter = TimeSpan.FromMilliseconds(150);

    private readonly OpenDocument _doc;
    private readonly Action<SyntaxViewport> _apply;
    private readonly Action _clear;
    private CheckpointSyntaxSession? _session;
    private Document? _bound;
    private (long First, int Count) _wanted = (-1, 0);
    private (long First, int Count) _requested;
    private long _doneRevision = -1;
    private long _pendingSince;
    private bool _pending;
    private bool _queued;
    private bool _disposed;

    /// <param name="apply">Receives the tokens of a complete viewport.</param>
    /// <param name="clear">Drops all tokens; called when the grammar or the document changes and for lines the grammar cannot handle.</param>
    public SyntaxFeeder(OpenDocument doc, Action<SyntaxViewport> apply, Action clear)
    {
        _doc = doc;
        _apply = apply;
        _clear = clear;
        doc.LanguageChanged += Rebuild;
        doc.Replaced += Rebuild;
        Rebuild();
    }

    /// <summary>The catch-up has been running for a noticeable time, so the view should say that its colors are not complete.</summary>
    public bool Pending => _pending;

    public event Action? PendingChanged;

    /// <summary>The zero-based lines on screen. Scrolling inside the margin of the last request costs nothing.</summary>
    public void SetVisible(long first, long last)
    {
        long count = Math.Max(1, last - first + 1);
        if (_wanted.First >= 0 && first >= _wanted.First && last < _wanted.First + _wanted.Count) return;
        long start = Math.Max(0, first - count);
        _wanted = (start, (int)Math.Min(MaxRows, last + count - start + 1));
        Run();
    }

    private void Rebuild()
    {
        if (_disposed) return;
        Release();
        _clear();
        SetPending(false);
        _pendingSince = 0;
        _doneRevision = -1;
        if (LanguageModes.GrammarId(_doc.Language) is not { } id) return;
        _bound = _doc.Doc;
        try { _session = new CheckpointSyntaxSession(_bound, BuiltinGrammars.Tokenizer(id)); }
        catch (IOException)
        {
            Abandon();
            return;
        }
        _bound.Changed += OnChanged;
        Run();
    }

    private void Abandon()
    {
        _pendingSince = 0;
        SetPending(false);
        try { _bound?.CheckExternalChange(); }
        catch (IOException) { } // the watcher reports it again
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        _doneRevision = -1;
        if (Dispatcher.UIThread.CheckAccess()) Run();
        else Dispatcher.UIThread.Post(Run);
    }

    private void Run()
    {
        if (_session is null || _wanted.First < 0) return;
        if (_requested != _wanted)
        {
            _requested = _wanted;
            _doneRevision = -1;
        }
        if (_doneRevision == _session.Revision) return;
        SyntaxViewport result;
        try { result = _session.TokenizeViewport(_wanted.First, _wanted.Count, Slice); }
        catch (IOException) // the file changed behind a paged snapshot: no tokens from this revision, the external-change path takes over
        {
            Abandon();
            return;
        }
        catch (InvalidOperationException) // more token runs than a viewport may hold: show the lines plain
        {
            _clear();
            SetPending(false);
            return;
        }
        switch (result.Status)
        {
            case SyntaxViewportStatus.Complete:
                _apply(result);
                _doneRevision = result.Revision;
                _pendingSince = 0;
                SetPending(false);
                break;
            case SyntaxViewportStatus.FullLineRequired:
                _clear();
                _pendingSince = 0;
                SetPending(false);
                break;
            default:
                if (_pendingSince == 0) _pendingSince = Stopwatch.GetTimestamp();
                SetPending(Stopwatch.GetElapsedTime(_pendingSince) >= ShowPendingAfter);
                Queue();
                break;
        }
    }

    private void Queue()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _queued = false;
            if (!_disposed) Run();
        }, DispatcherPriority.Background);
    }

    private void SetPending(bool value)
    {
        if (_pending == value) return;
        _pending = value;
        PendingChanged?.Invoke();
    }

    private void Release()
    {
        if (_bound is not null) _bound.Changed -= OnChanged;
        _session?.Dispose();
        _session = null;
        _bound = null;
        _requested = default;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _doc.LanguageChanged -= Rebuild;
        _doc.Replaced -= Rebuild;
        Release();
    }
}
