using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Text;

internal static class MarkdownMessages
{
    internal static string Reason(MarkdownSyntaxException error) => error.Failure switch
    {
        MarkdownSyntaxFailure.Nesting => Loc.Get(K.MarkdownNestingLimit, ("limit", error.Limit!.Value)),
        MarkdownSyntaxFailure.GeneratedTableCells => Loc.Get(K.MarkdownTableExpansionLimit, ("limit", error.Limit!.Value)),
        _ => Loc.Get(K.MarkdownInputFailed),
    };
}
