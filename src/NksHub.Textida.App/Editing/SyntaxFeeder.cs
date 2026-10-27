using System.Diagnostics;
using Avalonia.Threading;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Keeps the tokens of what one view shows up to date. It owns a <see cref="CheckpointSyntaxSession"/> of the document, asks it for the
/// visible lines (plus a margin) in short slices on the UI thread and hands a finished result to the view; an unfinished one is
/// continued in the background, and the view keeps what it showed until then. Session queries and document edits both run on the UI
/// thread, which is what the session requires. The grammar follows <see cref="OpenDocument.Language"/>.
/// A one-line edit first tries a cheaper way: the line is tokenized again from the state its predecessor left, and when it ends
/// in the state it ended in before, no other line can have changed.
/// </summary>
internal sealed class SyntaxFeeder : IDisposable
{
    private const int MaxRows = 1024;
    private const int MaxColumns = 65536;
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(6);
    private static readonly TimeSpan BackgroundSlice = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan ShowPendingAfter = TimeSpan.FromMilliseconds(150);

    private readonly OpenDocument _doc;
    private readonly Action<SyntaxViewport> _apply;
    private readonly Action _clear;
    private readonly Func<long, string?> _lineText;
    private readonly Func<long> _lineCount;
    private readonly Action _viewRequested;
    private CheckpointSyntaxSession? _session;
    private Tokenizer? _tokenizer;
    private Dictionary<long, LineTokens> _known = [];
    private long _knownLines = -1;
    private Document? _bound;
    private readonly record struct Window(long First, int Count, int Column, int Columns);
    private Window _wanted = new(-1, 0, 0, 0);
    private Window _requested;
    private long _doneRevision = -1;
    private long _pendingSince;
    private bool _pending;
    private bool _lineTooLong;
    private bool _queued;
    private bool _disposed;
    private readonly DispatcherTimer? _settleTimer;

    /// <param name="apply">Receives the tokens of a complete viewport.</param>
    /// <param name="clear">Drops all tokens; called when the grammar or the document changes and for lines the grammar cannot handle.</param>
    /// <param name="lineText">Text of a zero-based line as the view shows it, or null when it is not available.</param>
    /// <param name="lineCount">Number of lines of the view's text, or a negative number when it is not known.</param>
    /// <param name="viewRequested">Asks the view to report what is on screen again; called after the grammar or the document changed.</param>
    /// <param name="settle">When set, an edit is followed by one new request after this quiet time instead of at once; for views that are not the one being typed in.</param>
    public SyntaxFeeder(OpenDocument doc, Action<SyntaxViewport> apply, Action clear, Func<long, string?> lineText, Func<long> lineCount, Action viewRequested, TimeSpan? settle = null)
    {
        if (settle is { } quiet)
        {
            var timer = new DispatcherTimer { Interval = quiet };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Run();
            };
            _settleTimer = timer;
        }
        _viewRequested = viewRequested;
        _lineText = lineText;
        _lineCount = lineCount;
        _doc = doc;
        _apply = apply;
        _clear = clear;
        doc.LanguageChanged += OnSourceChanged;
        doc.Replaced += OnSourceChanged;
        Rebuild();
    }

    /// <summary>The document has a grammar, so colors are being kept; views skip measuring what is on screen otherwise.</summary>
    public bool Active => _session is not null;

    /// <summary>The catch-up has been running for a noticeable time, so the view should say that its colors are not complete.</summary>
    public bool Pending => _pending;

    /// <summary>
    /// The viewport holds a line too long for a full reference scan. That is final for this request: the lines stay plain
    /// and the view should say so. Scrolling elsewhere or editing starts a new request.
    /// </summary>
    public bool LineTooLong => _lineTooLong;

    /// <summary>Raised when <see cref="Pending"/> or <see cref="LineTooLong"/> changed.</summary>
    public event Action? PendingChanged;

    /// <summary>
    /// The zero-based lines and UTF-16 columns (<paramref name="lastColumn"/> exclusive) on screen. Scrolling inside the margin of the
    /// last request costs nothing.
    /// </summary>
    public void SetVisible(long first, long last, int firstColumn, int lastColumn)
    {
        if (_wanted.First >= 0 && first >= _wanted.First && last < _wanted.First + _wanted.Count
            && firstColumn >= _wanted.Column && lastColumn <= _wanted.Column + _wanted.Columns) return;
        long rows = Math.Max(1, last - first + 1);
        int width = Math.Max(1, lastColumn - firstColumn);
        long start = Math.Max(0, first - rows);
        int column = Math.Max(0, firstColumn - width);
        _wanted = new(start, (int)Math.Min(MaxRows, last + rows - start + 1), column, Math.Min(MaxColumns, lastColumn + width - column));
        Run();
    }

    private void OnSourceChanged()
    {
        if (_disposed) return;
        Rebuild();
        _viewRequested();
    }

    private void Rebuild()
    {
        if (_disposed) return;
        Release();
        _clear();
        SetPending(false);
        _pendingSince = 0;
        _doneRevision = -1;
        _known = [];
        _tokenizer = null;
        if (LanguageModes.GrammarId(_doc) is not { } id) return;
        _bound = _doc.Doc;
        _tokenizer = BuiltinGrammars.Tokenizer(id);
        try { _session = new CheckpointSyntaxSession(_bound, _tokenizer); }
        catch (IOException)
        {
            Abandon();
            return;
        }
        _bound.Changed += OnChanged;
        Run();
    }

    /// <summary>
    /// The file changed behind the snapshot: drop the session and the colors. The check that tells the user hashes the file, so it runs
    /// off the UI thread; whatever it throws, the watcher of the document reports the change again.
    /// </summary>
    private void Abandon()
    {
        var doc = _bound;
        Release();
        _known = [];
        _clear();
        _pendingSince = 0;
        SetPending(false);
        if (doc is not null) Task.Run(() => { try { doc.CheckExternalChange(); } catch (Exception) { } });
    }

    private void OnChanged(object? sender, DocumentChangedEventArgs e)
    {
        long t = Core.TypingTrace.Begin();
        _doneRevision = -1;
        if (_settleTimer is { } timer)
        {
            if (Dispatcher.UIThread.CheckAccess()) Restart(timer);
            else Dispatcher.UIThread.Post(() => Restart(timer));
        }
        else if (!Dispatcher.UIThread.CheckAccess()) Dispatcher.UIThread.Post(() => Run());
        else if (!TryRetokenizeLine(e.Change)) Run();
        Core.TypingTrace.End("syntax.onChanged", t);
    }

    private void Restart(DispatcherTimer timer)
    {
        if (_disposed) return;
        timer.Stop();
        timer.Start();
    }

    private bool TryRetokenizeLine(ChangeSet change)
    {
        if (_session is null || _tokenizer is null || _bound is null || _known.Count == 0 || change.Kind == ChangeKind.View || change.Edits.Count != 1) return false;
        var edit = change.Edits[0];
        if (_bound.Codec.Kind is CodecKind.Utf16 or CodecKind.Utf32 || edit.Inserted > 256 || !_bound.TryGetPosition(edit.Start, out var at)) return false;
        // no line break typed, and (same line count) none deleted: only line at.Line changed
        Span<byte> typed = stackalloc byte[(int)edit.Inserted];
        if (_bound.Read(edit.Start, typed) != typed.Length || typed.IndexOfAny((byte)10, (byte)13) >= 0) return false;
        if (_lineCount() != _knownLines || !_known.TryGetValue(at.Line, out var old) || _lineText(at.Line) is not { Length: <= MaxColumns } text) return false;
        var all = new List<Token>();
        var end = _tokenizer.TokenizeLine(text, old.StartState, all);
        if (!ReferenceEquals(end, old.EndState)) return false;
        var tokens = new List<Token>(all.Count);
        int to = _wanted.Column + _wanted.Columns;
        foreach (var token in all)
            if (Math.Max(token.Start, _wanted.Column) is var from && Math.Min(token.End, to) is var stop && from < stop)
                tokens.Add(new Token(from, stop - from, token.Kind));
        _known[at.Line] = new LineTokens(at.Line, tokens, old.StartState, end);
        _apply(new SyntaxViewport(_bound.Id, _session.Revision, _known.Values.OrderBy(l => l.Line).ToArray(), SyntaxViewportStatus.Complete, 0));
        _doneRevision = _session.Revision;
        return true;
    }

    private void Run(TimeSpan? slice = null)
    {
        if (_session is null || _wanted.First < 0) return;
        if (_requested != _wanted)
        {
            _requested = _wanted;
            _doneRevision = -1;
        }
        if (_doneRevision == _session.Revision) return;
        SyntaxViewport result;
        long tRun = Core.TypingTrace.Begin();
        try { result = _session.TokenizeViewport(_wanted.First, _wanted.Count, slice ?? Slice, default, _wanted.Column, _wanted.Columns); }
        catch (IOException) // the file changed behind a paged snapshot: no tokens from this revision, the external-change path takes over
        {
            Abandon();
            return;
        }
        catch (InvalidOperationException) // more token runs than a viewport may hold: show the lines plain
        {
            _known.Clear();
            _clear();
            SetPending(false);
            return;
        }
        Core.TypingTrace.End("syntax.tokenizeViewport", tRun);
        switch (result.Status)
        {
            case SyntaxViewportStatus.Complete:
                SetLineTooLong(false);
                _known = result.Lines.ToDictionary(l => l.Line);
                _knownLines = _lineCount();
                _apply(result);
                _doneRevision = result.Revision;
                _pendingSince = 0;
                SetPending(false);
                break;
            case SyntaxViewportStatus.FullLineRequired:
                _known.Clear();
                _clear();
                _pendingSince = 0;
                SetPending(false);
                SetLineTooLong(true);
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
            if (!_disposed) Run(BackgroundSlice);
        }, DispatcherPriority.Background);
    }

    private void SetPending(bool value)
    {
        if (_pending == value) return;
        _pending = value;
        PendingChanged?.Invoke();
    }

    private void SetLineTooLong(bool value)
    {
        if (_lineTooLong == value) return;
        _lineTooLong = value;
        PendingChanged?.Invoke();
    }

    private void Release()
    {
        if (_bound is not null) _bound.Changed -= OnChanged;
        _session?.Dispose();
        _session = null;
        _bound = null;
        _requested = default;
        SetLineTooLong(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settleTimer?.Stop();
        _doc.LanguageChanged -= OnSourceChanged;
        _doc.Replaced -= OnSourceChanged;
        Release();
    }
}
