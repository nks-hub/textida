using NksHub.Textida.Accessibility;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.Accessibility.Tests;

/// <summary>Surface with a fixed 10x20 pixel grid and no wrapping, over a real paged-backend document.</summary>
internal sealed class TestSurface : DocumentTextSurface
{
    public const double CharWidth = 10, LineHeight = 20;
    public bool Focused { get; set; }

    public TestSurface(string text) : base(Document.FromBytes(System.Text.Encoding.UTF8.GetBytes(text), PagedBackendFactory.Instance)) { }

    public override bool HasFocus => Focused;
    public override void Focus() { Focused = true; RaiseFocusChanged(); }
    public override ScreenRect ScreenBounds => new(100, 200, 800, 600);
    public override TextSpan VisibleRange => new(0, Length);

    public override IReadOnlyList<ScreenRect> GetRangeBounds(int start, int length)
    {
        var result = new List<ScreenRect>();
        int end = start + length;
        for (int line = LineOfOffset(start); line < LineCount && LineStart(line) < end || (length == 0 && line == LineOfOffset(start)); line++)
        {
            int a = Math.Max(start, LineStart(line)), b = Math.Min(end, LineStart(line + 1));
            result.Add(new(100 + (a - LineStart(line)) * CharWidth, 200 + line * LineHeight, (b - a) * CharWidth, LineHeight));
            if (length == 0) break;
        }
        return result;
    }

    public override int OffsetFromPoint(double x, double y)
    {
        int line = (int)((y - 200) / LineHeight);
        if (line < 0 || line >= LineCount || x < 100) return -1;
        return Math.Min(LineStart(line) + (int)((x - 100) / CharWidth), LineStart(line + 1));
    }

    public override void ScrollIntoView(int start, int length, bool alignToTop) { }
}
