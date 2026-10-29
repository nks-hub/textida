namespace NksHub.Textida.Workbench.Icons;

/// <summary>How a path of an icon is drawn. The colours come from the theme at draw time; an icon never carries a colour of its own.</summary>
public enum IconPaint
{
    /// <summary>A line in the text colour.</summary>
    Line,

    /// <summary>A filled shape in the text colour (a dot, an arrow head).</summary>
    Fill,

    /// <summary>A line in the accent colour.</summary>
    AccentLine,

    /// <summary>A filled shape in the accent colour.</summary>
    AccentFill,
}

/// <summary>One path of an icon: the data in the path mini-language of Avalonia (the <c>d</c> attribute of SVG) and how it is drawn.</summary>
public sealed record IconPath(string Data, IconPaint Paint);

/// <summary>An icon: square, <paramref name="Size"/> units on a side (16 for the built-in set), made of paths.</summary>
public sealed record IconShape(int Size, IReadOnlyList<IconPath> Paths);
