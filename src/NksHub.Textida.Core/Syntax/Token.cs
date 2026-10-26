namespace NksHub.Textida.Core.Syntax;

/// <summary>
/// A classified run of one line: UTF-16 column <see cref="Start"/> and <see cref="Length"/>. Text between tokens is plain.
/// <see cref="Kind"/> is the dotted name the grammar gave (<c>comment</c>, <c>string.escape</c>, <c>keyword</c>, ...);
/// the same instance is shared by all tokens of one kind, so kinds can be compared by reference after <see cref="string.Intern(string)"/>.
/// </summary>
public readonly record struct Token(int Start, int Length, string Kind)
{
    public int End => Start + Length;
}

/// <summary>Kind names the built-in grammars use. A grammar may use any other dotted lower-case name.</summary>
public static class TokenKinds
{
    public const string Comment = "comment";
    public const string DocComment = "comment.doc";
    public const string String = "string";
    public const string Escape = "string.escape";
    public const string Number = "number";
    public const string Keyword = "keyword";
    public const string Type = "type";
    public const string Constant = "constant";
    public const string Operator = "operator";
    public const string Punctuation = "punctuation";
    public const string Variable = "variable";
    public const string Property = "property";
    public const string Function = "function";
    public const string Tag = "tag";
    public const string Attribute = "attribute";
    public const string Regex = "regex";
    public const string Preprocessor = "preprocessor";
    public const string Meta = "meta";
    public const string Heading = "heading";
    public const string Emphasis = "emphasis";
    public const string Strong = "strong";
    public const string Link = "link";
    public const string Code = "code";
}
