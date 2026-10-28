namespace NksHub.Textida.Plugins.Json;

internal enum TokenKind { Open, Close, Comma, Colon, Scalar, LineComment, BlockComment }

/// <summary>A piece of JSON or JSONC text. <see cref="NewlineBefore"/> tells that a line break lies between the end of the previous token and this one.</summary>
internal readonly record struct Token(TokenKind Kind, int Start, int End, bool NewlineBefore)
{
    public bool IsComment => Kind is TokenKind.LineComment or TokenKind.BlockComment;
}

/// <summary>A comment kept as written. <see cref="OwnLine"/> is true when it stood on a line of its own.</summary>
internal readonly record struct JsonComment(string Raw, bool OwnLine);

/// <summary>A value with the comments around its entries. A scalar has <see cref="Raw"/>; a container has <see cref="Open"/> (<c>{</c> or <c>[</c>) and entries.</summary>
internal sealed class JsonValueNode
{
    public string? Raw;
    public char Open;
    public bool IsContainer => Open != '\0';

    /// <summary>Comments on the line of the opening bracket.</summary>
    public List<JsonComment> AfterOpen = [];
    public List<JsonEntry> Entries = [];

    /// <summary>Comments after the last entry.</summary>
    public List<JsonComment> Dangling = [];

    /// <summary>The last entry was followed by a comma (allowed in JSONC); it stays with the position, not with the entry, so it survives sorting.</summary>
    public bool TrailingComma;
}

/// <summary>A member of an object (with its <see cref="Key"/> as written, quotes included) or an item of an array (no key), with the comments that belong to it.</summary>
internal sealed class JsonEntry
{
    public List<JsonComment> Leading = [];
    public string? Key;
    public JsonValueNode Value = null!;

    /// <summary>Comments on the line where the entry ends.</summary>
    public List<JsonComment> Trailing = [];
}

internal sealed class JsonDocumentNode
{
    public List<JsonComment> Before = [];
    public JsonValueNode Root = null!;
    public List<JsonComment> After = [];
}

internal static class JsonTokens
{
    /// <summary>Splits text that is already known to be valid JSON or JSONC (the reader of System.Text.Json has accepted it).</summary>
    public static List<Token> Scan(string s)
    {
        var tokens = new List<Token>();
        bool newline = false;
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c is '\n' or '\r') { newline = true; i++; continue; }
            if (c is ' ' or '\t') { i++; continue; }
            int start = i;
            TokenKind kind;
            if (c == '/' && s[i + 1] == '/')
            {
                kind = TokenKind.LineComment;
                while (i < s.Length && s[i] is not ('\n' or '\r')) i++;
            }
            else if (c == '/')
            {
                kind = TokenKind.BlockComment;
                i = s.IndexOf("*/", i + 2, StringComparison.Ordinal) + 2;
            }
            else if (c == '"')
            {
                kind = TokenKind.Scalar;
                for (i++; s[i] != '"'; i += s[i] == '\\' ? 2 : 1) { }
                i++;
            }
            else if (c is '{' or '[') { kind = TokenKind.Open; i++; }
            else if (c is '}' or ']') { kind = TokenKind.Close; i++; }
            else if (c == ',') { kind = TokenKind.Comma; i++; }
            else if (c == ':') { kind = TokenKind.Colon; i++; }
            else
            {
                kind = TokenKind.Scalar;
                while (i < s.Length && s[i] is not (' ' or '\t' or '\r' or '\n' or ',' or ':' or '{' or '}' or '[' or ']' or '"' or '/')) i++;
            }
            tokens.Add(new Token(kind, start, i, newline));
            newline = false;
        }
        return tokens;
    }

    /// <summary>Reads the tokens of a valid text into the tree the printers work on.</summary>
    public static JsonDocumentNode Parse(string text, List<Token> tokens) => new Parser(text, tokens).ParseDocument();

    private sealed class Parser(string text, List<Token> tokens)
    {
        private int _i;

        private JsonComment Take()
        {
            var t = tokens[_i++];
            return new JsonComment(text[t.Start..t.End], t.NewlineBefore);
        }

        private string TakeRaw()
        {
            var t = tokens[_i++];
            return text[t.Start..t.End];
        }

        private void TakeComments(List<JsonComment> into)
        {
            while (_i < tokens.Count && tokens[_i].IsComment) into.Add(Take());
        }

        public JsonDocumentNode ParseDocument()
        {
            var document = new JsonDocumentNode();
            TakeComments(document.Before);
            document.Root = ParseValue();
            TakeComments(document.After);
            return document;
        }

        /// <summary>Whether the comments that start at <paramref name="from"/> and sit on one line end that line, so they belong to the entry before and not to the one after.</summary>
        private bool RunEndsLine(int from)
        {
            int k = from;
            while (k < tokens.Count && tokens[k].IsComment && !tokens[k].NewlineBefore) k++;
            return k >= tokens.Count || tokens[k].NewlineBefore || tokens[k].Kind == TokenKind.Close;
        }

        private JsonValueNode ParseValue()
        {
            if (tokens[_i].Kind == TokenKind.Scalar) return new JsonValueNode { Raw = TakeRaw() };
            var node = new JsonValueNode { Open = text[tokens[_i++].Start] };
            while (tokens[_i].IsComment && !tokens[_i].NewlineBefore) node.AfterOpen.Add(Take());
            var pending = new List<JsonComment>();
            while (true)
            {
                TakeComments(pending);
                if (tokens[_i].Kind == TokenKind.Close)
                {
                    _i++;
                    node.Dangling = pending;
                    return node;
                }
                var entry = new JsonEntry { Leading = pending };
                pending = [];
                if (node.Open == '{')
                {
                    entry.Key = TakeRaw();
                    TakeComments(entry.Leading); // between the key and the colon: kept, in front of the member
                    _i++; // the colon
                    TakeComments(entry.Leading);
                }
                entry.Value = ParseValue();

                int j = _i;
                while (tokens[j].IsComment) j++;
                if (tokens[j].Kind == TokenKind.Comma)
                {
                    while (_i < j) entry.Trailing.Add(Take());
                    _i++; // the comma
                    while (_i < tokens.Count && tokens[_i].IsComment && !tokens[_i].NewlineBefore && RunEndsLine(_i)) entry.Trailing.Add(Take());
                    int k = _i;
                    while (tokens[k].IsComment) k++;
                    if (tokens[k].Kind == TokenKind.Close) node.TrailingComma = true;
                }
                else
                {
                    while (tokens[_i].IsComment && !tokens[_i].NewlineBefore) entry.Trailing.Add(Take());
                }
                node.Entries.Add(entry);
            }
        }
    }
}
