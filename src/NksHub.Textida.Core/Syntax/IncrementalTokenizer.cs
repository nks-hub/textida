namespace NksHub.Textida.Core.Syntax;

/// <summary>Lines of a text as strings without terminators. Line numbers are zero-based; a text always has at least one (possibly empty) line.</summary>
public interface ILineSource
{
    long LineCount { get; }
    string GetLine(long line);
}

/// <summary>Tokens of one line and the state the next line starts in.</summary>
public sealed record LineTokens(long Line, IReadOnlyList<Token> Tokens, LineState StartState, LineState EndState);

/// <summary>
/// Keeps the state at the start of every line of a text and brings it up to date lazily. After an edit only the lines from the edit on
/// are scanned again, and only until a line ends in the state it had before the edit (the states below are then still right).
/// Nothing is scanned that no caller asked for: tokens of a viewport cost the lines from the first stale line down to the viewport.
/// Not thread safe; the owner serialises edits, queries and cancellation.
/// </summary>
public sealed class IncrementalTokenizer
{
    private readonly Tokenizer _tokenizer;
    private readonly ILineSource _lines;
    private readonly List<LineState?> _starts;
    private readonly List<Token> _scratch = [];
    private long _lineCount;
    private int _valid;
    private int _pendingEnd;

    public IncrementalTokenizer(Tokenizer tokenizer, ILineSource lines)
    {
        _tokenizer = tokenizer;
        _lines = lines;
        _lineCount = lines.LineCount;
        _starts = [tokenizer.InitialState];
        _valid = 1;
    }

    /// <summary>Lines scanned since creation (also by the state catch-up); tests and benchmarks read the cost of an edit from it.</summary>
    public long LinesScanned { get; private set; }

    /// <summary>Number of leading lines whose start state is known to be right for the current text.</summary>
    public int ValidLines => _valid;

    public Tokenizer Tokenizer => _tokenizer;

    /// <summary>Line count the states were last adjusted to.</summary>
    public long LineCount => _lineCount;

    /// <summary>Forgets all states; the next query scans from the first line.</summary>
    public void Reset()
    {
        _lineCount = _lines.LineCount;
        _starts.Clear();
        _starts.Add(_tokenizer.InitialState);
        _valid = 1;
        _pendingEnd = 0;
    }

    /// <summary>The source changed from line <paramref name="first"/> on and nothing is known about the lines below; they are scanned again when needed.</summary>
    public void Invalidate(long first)
    {
        _lineCount = _lines.LineCount;
        int keep = (int)Math.Min(Math.Max(first, 0) + 1, _starts.Count);
        _starts.RemoveRange(keep, _starts.Count - keep);
        _valid = Math.Min(_valid, keep);
        _pendingEnd = 0;
    }

    /// <summary>
    /// <paramref name="removed"/> lines starting at <paramref name="first"/> were replaced by <paramref name="inserted"/> lines. The states below the
    /// edit are kept (shifted) as the reference for the convergence check.
    /// </summary>
    public void OnEdit(long first, long removed, long inserted)
    {
        long oldCount = _lineCount;
        _lineCount = _lines.LineCount;
        bool unresolved = _valid < _starts.Count;
        if (unresolved || first < 0 || removed < 1 || inserted < 1 || first + removed > oldCount || _lineCount != oldCount - removed + inserted || first >= _valid)
        {
            Invalidate(Math.Min(first, _valid - 1));
            return;
        }

        int f = (int)first;
        // _starts[f] (the state the first replaced line starts in) is unchanged; _starts[f + removed] is the first state of an untouched line.
        int tailFrom = (int)Math.Min(f + removed, _starts.Count);
        var tail = _starts.GetRange(tailFrom, _starts.Count - tailFrom);
        _starts.RemoveRange(f + 1, _starts.Count - f - 1);
        for (long i = 1; i < inserted; i++) _starts.Add(null);
        _starts.AddRange(tail);
        _valid = f + 1;
        _pendingEnd = (int)Math.Min(first + inserted, int.MaxValue);
    }

    /// <summary>State at the start of <paramref name="line"/>, scanning the lines in front of it that are stale.</summary>
    public LineState GetStartState(long line, CancellationToken ct = default)
    {
        if (line < 0 || line > _lineCount) throw new ArgumentOutOfRangeException(nameof(line));
        while (_valid <= line)
        {
            ct.ThrowIfCancellationRequested();
            Step();
        }
        return _starts[(int)line]!;
    }

    /// <summary>Tokens of one line, with the states around it.</summary>
    public LineTokens TokenizeLine(long line, CancellationToken ct = default)
    {
        var start = GetStartState(line, ct);
        var tokens = new List<Token>();
        var end = _tokenizer.TokenizeLine(_lines.GetLine(line), start, tokens);
        LinesScanned++;
        if (line + 1 == _valid) Adopt((int)line, end);
        return new LineTokens(line, tokens, start, end);
    }

    /// <summary>Tokens of <paramref name="count"/> lines from <paramref name="first"/> (clipped to the text).</summary>
    public IReadOnlyList<LineTokens> TokenizeViewport(long first, int count, CancellationToken ct = default)
    {
        var result = new List<LineTokens>(count);
        long last = Math.Min(first + count, _lineCount);
        if (first >= last) return result;
        var state = GetStartState(first, ct);
        for (long line = first; line < last; line++)
        {
            ct.ThrowIfCancellationRequested();
            var tokens = new List<Token>();
            var end = _tokenizer.TokenizeLine(_lines.GetLine(line), state, tokens);
            LinesScanned++;
            if (line + 1 == _valid) Adopt((int)line, end);
            result.Add(new LineTokens(line, tokens, state, end));
            state = end;
        }
        return result;
    }

    private void Step()
    {
        int line = _valid - 1;
        LinesScanned++;
        _scratch.Clear();
        Adopt(line, _tokenizer.TokenizeLine(_lines.GetLine(line), _starts[line]!, _scratch));
    }

    /// <summary>Stores the state a just scanned frontier line ended in; when it equals the stale one below the edit, everything below is right again.</summary>
    private void Adopt(int line, LineState end)
    {
        int next = line + 1;
        if (next < _starts.Count && _starts[next] is { } old && next >= _pendingEnd && ReferenceEquals(old, end))
        {
            _valid = _starts.Count;
            return;
        }
        if (next < _starts.Count) _starts[next] = end;
        else _starts.Add(end);
        _valid = next + 1;
    }
}
