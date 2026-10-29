namespace NksHub.Textida.Workbench.Icons;

/// <summary>
/// The icons that ship with the program, by name (the file name of <c>Icons/svg/&lt;name&gt;.svg</c>). The entries are generated from the SVG files when the program is built; a command names one in
/// the <c>icon</c> of its manifest entry, and a plugin may name one for its own commands. The own drawings are listed in THIRD-PARTY-NOTICES.md.
/// </summary>
public static partial class BuiltinIcons
{
    /// <summary>The value of <c>icon</c> that says a command has no icon on purpose; every command without a picture says so, so a command that only lacks one is found by a test.</summary>
    public const string TextOnly = "text";

    /// <summary>Every icon by name.</summary>
    public static IReadOnlyDictionary<string, IconShape> All { get; } = Build();

    /// <summary>The icon of that name; false when there is none.</summary>
    public static bool TryGet(string? name, out IconShape icon)
    {
        icon = null!;
        return name is not null && All.TryGetValue(name, out icon!);
    }
}
