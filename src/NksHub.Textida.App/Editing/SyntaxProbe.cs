using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

/// <summary>The tokens of one document in one language for the smart keys: a <see cref="SyntaxSession"/> that follows the document, so a keystroke re-tokenizes only the lines it touched.</summary>
internal sealed class SyntaxProbe : ITokenProbe, IDisposable
{
    private readonly SyntaxSession _session;

    private SyntaxProbe(SyntaxSession session, string language)
    {
        _session = session;
        Language = language;
    }

    public string Language { get; }

    /// <summary>Null when the language has no grammar or the document is too large to follow.</summary>
    public static SyntaxProbe? TryCreate(Document document, string language)
    {
        if (LanguageModes.Grammars.Find(language) is not { } grammar || !SyntaxSession.CanFollow(document)) return null;
        return new(new SyntaxSession(document, new Tokenizer(grammar)), language);
    }

    public IReadOnlyList<Token> TokensOfLine(int line) => _session.Tokens.TokenizeLine(line - 1).Tokens;

    public void Dispose() => _session.Dispose();
}
