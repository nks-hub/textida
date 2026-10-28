namespace NksHub.Textida.App.Shell;

/// <summary>
/// Ties the lines of the source to the places of the drawn preview (P9.3.1): the editor and the preview scroll together by moving along this map. Every block of the preview knows the
/// line where it starts and where it was drawn; between two blocks the map is a straight line, so a long paragraph scrolls in step with the lines it came from. The text before the first block
/// maps to the top and the end of the text to the end of the preview.
/// </summary>
internal sealed class PreviewScrollMap
{
    private readonly List<(double Line, double Top)> _points = [];

    /// <param name="anchors">The first line and the top (in pixels from the start of the preview) of each top-level block.</param>
    /// <param name="lineCount">Lines of the source.</param>
    /// <param name="height">Height of the whole preview in pixels.</param>
    public PreviewScrollMap(IEnumerable<(int Line, double Top)> anchors, int lineCount, double height)
    {
        _points.Add((0, 0));
        foreach (var (line, top) in anchors.OrderBy(a => a.Line))
            if (line > _points[^1].Line && top >= _points[^1].Top) _points.Add((line, top));
        _points.Add((Math.Max(lineCount, _points[^1].Line + 1), Math.Max(height, _points[^1].Top)));
    }

    /// <summary>Pixels from the start of the preview at which the (fractional, zero-based) line is.</summary>
    public double OffsetFor(double line) => Interpolate(_points, p => p.Line, p => p.Top, line);

    /// <summary>The (fractional, zero-based) line that is at <paramref name="offset"/> pixels from the start of the preview.</summary>
    public double LineAt(double offset) => Interpolate(_points, p => p.Top, p => p.Line, offset);

    private static double Interpolate(List<(double Line, double Top)> points, Func<(double Line, double Top), double> from, Func<(double Line, double Top), double> to, double x)
    {
        if (x <= from(points[0])) return to(points[0]);
        for (int i = 1; i < points.Count; i++)
        {
            double x1 = from(points[i]);
            if (x > x1) continue;
            double x0 = from(points[i - 1]), y0 = to(points[i - 1]), y1 = to(points[i]);
            return x1 <= x0 ? y1 : y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }
        return to(points[^1]);
    }
}
