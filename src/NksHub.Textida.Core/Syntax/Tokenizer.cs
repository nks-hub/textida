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
    internal int Scan(string text, int begin, bool final, ref LineState state, List<Token> tokens, int shift)
    {
        int n = text.Length;
        int limit = final ? n : SafeLimit(text, begin);
        int pos = begin, from = begin;
        while (true)
        {
            var ctx = state.Context;
            var matcher = ctx.GetMatcher();
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
                Emit(tokens, pos, stop, ctx.Token, shift);
                pos = stop;
                break;
            }

            int index = m.Index, length = m.Length;
            int alt = 0;
            while (!m.Groups[matcher.Groups[alt]].Success) alt++;
            var a = matcher.Alts[alt];

            if (a.Kind == AltKind.End)
            {
                Emit(tokens, pos, index, ctx.Token, shift);
                Emit(tokens, index, index + length, ctx.EndToken, shift);
                state = state.Parent!;
                pos = from = index + length;
                continue;
            }

            if (length == 0)
            {
                from = index + 1;
                continue;
            }

            Emit(tokens, pos, index, ctx.Token, shift);
            switch (a.Kind)
            {
                case AltKind.Match:
                    Emit(tokens, index, index + length, a.Token, shift);
                    break;
                case AltKind.Word:
                    Emit(tokens, index, index + length, a.Owner.LookupWord(m.ValueSpan), shift);
                    break;
                case AltKind.Begin:
                    var child = a.Child!;
                    if (state.Depth < LineState.MaxDepth) state = child.Push(state);
                    Emit(tokens, index, index + length, child.BeginToken, shift);
                    break;
            }
            pos = from = index + length;
        }

        if (final)
            while (state.Parent is not null && !state.Context.Multiline) state = state.Parent;
        return pos;
    }

    private static int SafeLimit(string text, int begin)
    {
        int limit = text.Length - Margin;
        if (limit <= begin) return begin;
        for (int k = limit, floor = Math.Max(begin + 1, limit - BackoffSearch); k >= floor; k--)
            if (char.IsWhiteSpace(text[k])) return k;
        return limit;
    }

    private static void Emit(List<Token> tokens, int start, int end, string? kind, int shift)
    {
        if (kind is null || end <= start) return;
        int s = start + shift;
        if (tokens.Count > 0)
        {
            var last = tokens[^1];
            if (last.End == s && (ReferenceEquals(last.Kind, kind) || last.Kind == kind))
            {
                tokens[^1] = last with { Length = last.Length + (end - start) };
                return;
            }
        }
        tokens.Add(new Token(s, end - start, kind));
    }
}
