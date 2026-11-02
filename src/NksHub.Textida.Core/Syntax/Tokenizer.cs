using System.Text.RegularExpressions;

namespace NksHub.Textida.Core.Syntax;

/// <summary>
/// Line tokenizer of one grammar: a line and the state at its start give the tokens of the line and the state at its end.
/// Stateless and thread safe. Lines are given without their terminator.
/// </summary>
public sealed class Tokenizer(Grammar grammar)
{
    /// <summary>Characters kept back at the end of a non-final chunk, so a token or delimiter is not cut by the chunk edge.</summary>
    internal const int Margin = 256;
    private const int BackoffSearch = 4096;

    public Grammar Grammar { get; } = grammar;
    public LineState InitialState => Grammar.InitialState;

    /// <summary>Tokenizes a whole line, appends its tokens to <paramref name="tokens"/> and returns the state at the start of the next line.</summary>
    public LineState TokenizeLine(string line, LineState start, List<Token> tokens)
    {
        var state = start;
        Scan(line, 0, true, ref state, tokens, 0);
        return state;
    }

    /// <summary>
    /// Scans <paramref name="text"/> from <paramref name="begin"/>. A final chunk ends the line: regions that do not span lines are closed.
    /// A non-final chunk stops a little before its end and returns how far it got, so the caller continues with the rest in front of the next chunk.
    /// Token columns are offset by <paramref name="shift"/>.
    /// </summary>
    internal int Scan(string text, int begin, bool final, ref LineState state, List<Token> tokens, int shift,
        int firstColumn = 0, int lastColumn = int.MaxValue, Action? regionEnded = null)
    {
        int n = text.Length;
        int limit = final ? n : SafeLimit(text, begin);
        int pos = begin, from = begin;
        if (begin == 0 && state.HasWhile) pos = from = MatchWhiles(text, ref state, tokens, shift, firstColumn, lastColumn);
        while (true)
        {
            var ctx = state.Context;
            var matcher = state.Matcher;
            Match? m = null;
            if (from <= n)
            {
                try
                {
                    m = matcher.Regex.Match(text, from);
                }
                catch (RegexMatchTimeoutException)
                {
                }
            }

            if (m is not { Success: true } || (final ? m.Index > n : m.Index >= limit))
            {
                int stop = Math.Max(pos, limit);
                Emit(tokens, pos, stop, ctx.Token, shift, firstColumn, lastColumn);
                pos = stop;
                break;
            }

            int index = m.Index, length = m.Length;
            int alt = 0;
            while (!m.Groups[matcher.Groups[alt]].Success) alt++;
            var a = matcher.Alts[alt];

            if (a.Kind == AltKind.End)
            {
                regionEnded?.Invoke();
                Emit(tokens, pos, index, ctx.Token, shift, firstColumn, lastColumn);
                if (ctx.EndCaptures is { } endCaptures) EmitGroups(tokens, index, length, ctx.EndToken, endCaptures, ctx.EndMatch(matcher, text, index), shift, firstColumn, lastColumn);
                else Emit(tokens, index, index + length, ctx.EndToken, shift, firstColumn, lastColumn);
                state = state.Parent!;
                pos = from = index + length;
                continue;
            }

            if (length == 0)
            {
                from = index + 1;
                continue;
            }

            Emit(tokens, pos, index, ctx.Token, shift, firstColumn, lastColumn);
            switch (a.Kind)
            {
                case AltKind.Match:
                    Emit(tokens, index, index + length, a.Token, shift, firstColumn, lastColumn);
                    break;
                case AltKind.Word:
                    Emit(tokens, index, index + length, a.Owner.LookupWord(m.ValueSpan), shift, firstColumn, lastColumn);
                    break;
                case AltKind.Begin:
                    var child = a.Child!;
                    var begun = child.NeedsBeginMatch ? child.BeginMatch(text, index) : null;
                    if (state.Depth < LineState.MaxDepth) state = child.Push(state, begun);
                    if (child.BeginCaptures is { } beginCaptures) EmitGroups(tokens, index, length, child.BeginToken, beginCaptures, begun, shift, firstColumn, lastColumn);
                    else Emit(tokens, index, index + length, child.BeginToken, shift, firstColumn, lastColumn);
                    break;
            }
            pos = from = index + length;
        }

        if (final)
            while (state.Parent is not null && !state.Context.Multiline) state = state.Parent;
        return pos;
    }

    /// <summary>
    /// At a line start: each open region with a <c>while</c> expression, outermost first, must match it where the previous one ended; the first that does not is closed together with everything inside it.
    /// Returns where the scan goes on.
    /// </summary>
    private static int MatchWhiles(string text, ref LineState state, List<Token> tokens, int shift, int firstColumn, int lastColumn)
    {
        var frames = new List<LineState>();
        for (var s = state; s is not null; s = s.Parent)
            if (s.Context.While is not null) frames.Add(s);
        int pos = 0;
        for (int i = frames.Count - 1; i >= 0; i--)
        {
            var ctx = frames[i].Context;
            if (ctx.WhileMatch(text, pos) is not { } m)
            {
                state = frames[i].Parent!;
                break;
            }
            if (ctx.WhileCaptures is { } captures) EmitGroups(tokens, m.Index, m.Length, ctx.EndToken, captures, m, shift, firstColumn, lastColumn);
            else Emit(tokens, m.Index, m.Index + m.Length, ctx.EndToken, shift, firstColumn, lastColumn);
            pos = m.Index + m.Length;
        }
        return pos;
    }

    /// <summary>Emits a delimiter whose capturing groups have kinds of their own; the later group paints over the earlier one where they overlap.</summary>
    private static void EmitGroups(List<Token> tokens, int index, int length, string? kind, (int Group, string Kind)[] captures, Match? detail, int shift, int firstColumn, int lastColumn)
    {
        var paint = new string?[length];
        Array.Fill(paint, kind);
        if (detail is not null)
            foreach (var (group, groupKind) in captures)
            {
                var g = detail.Groups[group];
                if (g.Success && g.Index >= index && g.Index + g.Length <= index + length) Array.Fill(paint, groupKind, g.Index - index, g.Length);
            }
        for (int i = 0, run = 0; i < length; i = run)
        {
            for (run = i + 1; run < length && paint[run] == paint[i]; run++) { }
            Emit(tokens, index + i, index + run, paint[i], shift, firstColumn, lastColumn);
        }
    }

    private static int SafeLimit(string text, int begin)
    {
        int limit = text.Length - Margin;
        if (limit <= begin) return begin;
        for (int k = limit, floor = Math.Max(begin + 1, limit - BackoffSearch); k >= floor; k--)
            if (char.IsWhiteSpace(text[k])) return k;
        return limit;
    }

    private static void Emit(List<Token> tokens, int start, int end, string? kind, int shift, int firstColumn, int lastColumn)
    {
        if (kind is null || end <= start) return;
        int s = Math.Max(start + shift, firstColumn);
        int e = Math.Min(end + shift, lastColumn);
        if (e <= s) return;
        if (tokens.Count > 0)
        {
            var last = tokens[^1];
            if (last.End == s && (ReferenceEquals(last.Kind, kind) || last.Kind == kind))
            {
                tokens[^1] = last with { Length = last.Length + (e - s) };
                return;
            }
        }
        tokens.Add(new Token(s, e - s, kind));
    }
}
