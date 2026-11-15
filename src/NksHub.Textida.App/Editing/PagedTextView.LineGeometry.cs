using System.Collections;
using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Editing;

internal sealed partial class PagedTextView
{
    private sealed partial class LinesSurface
    {
        private readonly record struct GeometryKey(string Text, long Revision, string Family, double Size, CultureInfo Culture,
            bool SecretsHidden, bool SecretsRevealed);
        private readonly record struct FontSpan(int Start, int Length, TokenStyle Style);
        private LineGeometry? lineGeometry;

        private LineGeometry GeometryOf(LineFormatKey key, string shown, Token[]? tokens, SyntaxPalette palette)
        {
            var geometryKey = new GeometryKey(shown, key.Revision, key.Family, key.FontSize, key.Culture,
                key.SecretsHidden, key.SecretsRevealed);
            var spans = FontSpans(key.View, tokens, palette);
            if (lineGeometry is { } cached && cached.Key == geometryKey
                && ReferenceEquals(cached.Key.Culture, key.Culture) && cached.Spans.AsSpan().SequenceEqual(spans)) return cached;
            var next = new LineGeometry(geometryKey, shown, spans);
            lineGeometry?.Dispose();
            return lineGeometry = next;
        }

        private static FontSpan[] FontSpans(LineView view, Token[]? tokens, SyntaxPalette palette)
        {
            TokenStyle[]? styles = null;
            if (tokens is not null)
                foreach (var token in tokens)
                {
                    var style = palette.GetStyle(token.Kind);
                    if (style == TokenStyle.None || palette.GetBrush(token.Kind) is null) continue;
                    int from = view.DisplayAt[Math.Clamp(token.Start, 0, view.Map.Text.Length)];
                    int to = view.DisplayAt[Math.Clamp(token.End, 0, view.Map.Text.Length)];
                    styles ??= new TokenStyle[view.Display.Length];
                    for (int at = from; at < to; at++) styles[at] |= style;
                }
            if (styles is null) return [];
            var result = new List<FontSpan>();
            for (int from = 0; from < styles.Length;)
            {
                int to = from + 1;
                while (to < styles.Length && styles[to] == styles[from]) to++;
                if (styles[from] != TokenStyle.None) result.Add(new(from, to - from, styles[from]));
                from = to;
            }
            return result.ToArray();
        }

        public void ReleaseLineGeometry()
        {
            formattedLine = null;
            lineGeometry?.Dispose();
            lineGeometry = null;
        }

        private sealed class LineGeometry : IDisposable
        {
            public GeometryKey Key { get; }
            public FontSpan[] Spans { get; }
            private readonly TextLayout layout;
            private readonly DrawingGroup drawing = new();
            private readonly Dictionary<GlyphRun, int> sourceStarts = new(ReferenceEqualityComparer.Instance);
            private List<GlyphRun> fragments = [];

            public LineGeometry(GeometryKey key, string text, FontSpan[] spans)
            {
                Key = key;
                Spans = spans;
                var family = new FontFamily(key.Family);
                var overrides = spans.Select(span => new ValueSpan<TextRunProperties>(span.Start, span.Length,
                    new GenericTextRunProperties(new Typeface(family,
                        span.Style.HasFlag(TokenStyle.Italic) ? FontStyle.Italic : FontStyle.Normal,
                        span.Style.HasFlag(TokenStyle.Bold) ? FontWeight.Bold : FontWeight.Normal), key.Size,
                        foregroundBrush: Brushes.Black, cultureInfo: key.Culture))).ToArray();
                layout = new TextLayout(text, new Typeface(family), key.Size, Brushes.Black, textStyleOverrides: overrides);
                foreach (var line in layout.TextLines)
                    foreach (var bounds in line.GetTextBounds(line.FirstTextSourceIndex, line.Length))
                        foreach (var run in bounds.TextRunBounds)
                            if (run.TextRun is ShapedTextRun shaped) sourceStarts[shaped.GlyphRun] = run.TextSourceCharacterIndex;
                using var recording = drawing.Open();
                layout.Draw(recording, default);
            }

            public DrawingGroup Paint(Token[]? tokens, LineView view, SyntaxPalette palette, IBrush foreground)
            {
                var colors = new IBrush?[view.Display.Length];
                if (tokens is not null)
                    foreach (var token in tokens)
                    {
                        if (palette.GetBrush(token.Kind) is not { } brush) continue;
                        int from = view.DisplayAt[Math.Clamp(token.Start, 0, view.Map.Text.Length)];
                        int to = view.DisplayAt[Math.Clamp(token.End, 0, view.Map.Text.Length)];
                        if (from < to) Array.Fill(colors, brush, from, to - from);
                    }
                var result = new DrawingGroup();
                var next = new List<GlyphRun>();
                try
                {
                    using var recording = result.Open();
                    Recolor(drawing, recording, colors, foreground, sourceStarts, next);
                }
                catch { foreach (var run in next) run.Dispose(); throw; }
                foreach (var run in fragments) run.Dispose();
                fragments = next;
                return result;
            }

            private static void Recolor(Drawing source, DrawingContext context, IBrush?[] colors,
                IBrush foreground, IReadOnlyDictionary<GlyphRun, int> sourceStarts, List<GlyphRun> fragments)
            {
                if (source is DrawingGroup group)
                {
                    using (context.PushTransform(group.Transform?.Value ?? Matrix.Identity))
                    using (context.PushOpacity(group.Opacity))
                    using (group.ClipGeometry is { } clip ? context.PushGeometryClip(clip) : default)
                        foreach (var child in group.Children) Recolor(child, context, colors, foreground, sourceStarts, fragments);
                    return;
                }
                if (source is not GlyphRunDrawing { GlyphRun: { } run })
                    throw new InvalidOperationException("A long-line text layout produced an unexpected drawing.");
                if (run.GlyphInfos.Count == 0) return;
                if (!sourceStarts.TryGetValue(run, out int start))
                    throw new InvalidOperationException("A long-line glyph run lost its source position.");
                var infos = run.GlyphInfos;
                int first = run.Metrics.FirstCluster;
                IBrush ColorAt(int index) => colors[start + infos[index].GlyphCluster - first] ?? foreground;
                double x = 0;
                for (int from = 0; from < infos.Count;)
                {
                    var brush = ColorAt(from);
                    int to = from + 1;
                    while (to < infos.Count && Equals(brush, ColorAt(to))) to++;
                    if (from == 0 && to == infos.Count) context.DrawGlyphRun(brush, run);
                    else
                    {
                        int low = Math.Min(infos[from].GlyphCluster, infos[to - 1].GlyphCluster);
                        int high = run.IsLeftToRight
                            ? to < infos.Count ? infos[to].GlyphCluster : first + run.Characters.Length
                            : from > 0 ? infos[from - 1].GlyphCluster : first + run.Characters.Length;
                        var fragment = new GlyphRun(run.GlyphTypeface, run.FontRenderingEmSize,
                            run.Characters.Slice(low - first, high - low), new GlyphSlice(infos, from, to - from, low),
                            run.BaselineOrigin + new Vector(x, 0), run.BiDiLevel);
                        fragments.Add(fragment);
                        context.DrawGlyphRun(brush, fragment);
                    }
                    for (int at = from; at < to; at++) x += infos[at].GlyphAdvance;
                    from = to;
                }
            }

            public void Dispose()
            {
                foreach (var run in fragments) run.Dispose();
                fragments.Clear();
                layout.Dispose();
            }

            // Keep the already-shaped glyphs, including their original visual order and offsets.
            private sealed class GlyphSlice(IReadOnlyList<GlyphInfo> source, int start, int count, int cluster)
                : IReadOnlyList<GlyphInfo>
            {
                public int Count => count;
                public GlyphInfo this[int index]
                {
                    get
                    {
                        if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
                        var glyph = source[start + index];
                        return new(glyph.GlyphIndex, glyph.GlyphCluster - cluster, glyph.GlyphAdvance, glyph.GlyphOffset);
                    }
                }
                public IEnumerator<GlyphInfo> GetEnumerator() { for (int i = 0; i < count; i++) yield return this[i]; }
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
        }
    }
}
