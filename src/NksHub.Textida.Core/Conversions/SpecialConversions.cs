namespace NksHub.Textida.Core.Conversions;

/// <summary>One text conversion of the editor: a plain function from the text of a selection to the new text. <paramref name="TitleKey"/> is the key of its name in the string tables of the app.</summary>
public sealed record SpecialConversion(string Id, string TitleKey, Func<string, string> Convert);

/// <summary>
/// The closed list of special conversions (F048). The list of ids is an owner decision that is not written down yet, so <see cref="All"/> is empty and no
/// conversion is invented here. When the owner fixes the list, each id is added here together with its row in the test that closes the list; the editor offers
/// whatever <see cref="All"/> holds next to the converters of data plugins, over every selection as one undo step.
/// </summary>
public static class SpecialConversions
{
    public static IReadOnlyList<SpecialConversion> All { get; } = [];
}
