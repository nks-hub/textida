using Avalonia.Media;
using NksHub.Textida.Core.Files;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The icons of the file tree: one small outline for each <see cref="FileKind"/>, drawn in a 16 by 16 box with the stroke of the shell (the colour comes from the style class of the kind, see
/// <see cref="ClassOf"/>). They are vectors, so they follow the theme and the scaling of the display.
/// </summary>
internal static class FileGlyphs
{
    private const string Page = "M4,2.5 L9.5,2.5 L12,5 L12,13.5 L4,13.5 Z M9.5,2.5 L9.5,5 L12,5";

    private static readonly Dictionary<FileKind, Geometry> Closed = new()
    {
        [FileKind.Folder] = Geometry.Parse("M2,4.5 L6,4.5 L7.5,6 L14,6 L14,12.5 L2,12.5 Z"),
        [FileKind.File] = Geometry.Parse(Page),
        [FileKind.Text] = Geometry.Parse(Page + " M6,8 L10,8 M6,10.5 L10,10.5"),
        [FileKind.Code] = Geometry.Parse(Page + " M7,7.5 L5.5,9.5 L7,11.5 M9,7.5 L10.5,9.5 L9,11.5"),
        [FileKind.Markup] = Geometry.Parse(Page + " M6.5,8 L5.5,9.5 L6.5,11 M9.5,8 L10.5,9.5 L9.5,11 M8.7,7.5 L7.3,11.5"),
        [FileKind.Data] = Geometry.Parse(Page + " M6,8 L10,8 M6,10.5 L10,10.5 M8,6.5 L8,12"),
        [FileKind.Style] = Geometry.Parse(Page + " M6.6,7 L6.2,12 M9.4,7 L9,12 M5.4,8.6 L10.4,8.6 M5.2,10.6 L10.2,10.6"),
        [FileKind.Image] = Geometry.Parse("M2.5,3.5 L13.5,3.5 L13.5,12.5 L2.5,12.5 Z M2.5,10.5 L6,7 L8.8,9.8 L10.5,8.2 L13.5,11 M10,5.5 L11.5,5.5 L11.5,7 L10,7 Z"),
        [FileKind.Archive] = Geometry.Parse(Page + " M8,2.5 L8,4 M8,5 L8,6.5 M8,7.5 L8,9 M7,9.5 L9,9.5 L9,12 L7,12 Z"),
        [FileKind.Binary] = Geometry.Parse(Page + " M5.6,8 L7.4,8 L7.4,11.5 L5.6,11.5 Z M9.6,8 L9.6,11.5"),
    };

    private static readonly Geometry FolderOpen = Geometry.Parse("M2,12.5 L2,4.5 L6,4.5 L7.5,6 L13,6 L13,7.5 M2,12.5 L4,7.5 L15,7.5 L13,12.5 Z");

    public static Geometry Of(FileKind kind, bool open) => kind == FileKind.Folder && open ? FolderOpen : Closed[kind];

    /// <summary>The style class that gives the icon the colour of its kind (<c>kindcode</c>, <c>kindfolder</c>, ...); the colours are the <c>File...</c> brushes of the theme.</summary>
    public static string ClassOf(FileKind kind) => "kind" + kind.ToString().ToLowerInvariant();
}
