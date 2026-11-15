namespace NksHub.Textida.App.Editing;

internal sealed partial class PagedTextView
{
    private sealed partial class LinesSurface
    {
        /// <summary>Character boundary nearest to a horizontal position in the decoded text.</summary>
        public int IndexAtX(LineView view, double x)
        {
            double WidthAt(int index)
            {
                // An unpaired high surrogate can be wider than its complete glyph and break the binary search.
                while (index > 0 && !view.Map.IsCharBoundary(index)) index--;
                return XOf(view, index);
            }

            int lo = 0, hi = view.Map.Text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (WidthAt(mid) <= x) lo = mid; else hi = mid - 1;
            }
            if (lo < view.Map.Text.Length && WidthAt(lo + 1) - x < x - WidthAt(lo)) lo++;
            while (lo > 0 && !view.Map.IsCharBoundary(lo)) lo--;
            return lo;
        }
    }
}
