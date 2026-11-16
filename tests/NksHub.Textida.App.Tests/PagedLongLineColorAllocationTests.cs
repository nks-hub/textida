using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using NksHub.Textida.App.Services;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core;
using NksHub.Textida.Core.Syntax;

namespace NksHub.Textida.App.Tests;

public sealed partial class PagedLongLineFormattingTests
{
    [AvaloniaFact]
    public void Sixteen_color_only_repaints_of_a_16_kib_prefix_allocate_at_most_250_kb_each()
    {
        const int prefixLength = 16 * 1024, repaints = 16;
        const long allocationLimit = 250_000;
        bool tracing = TypingTrace.Enabled;
        try
        {
            TypingTrace.Enabled = false;
            using var host = new Host(new string('x', 2 << 20));
            host.App.Workbench.Set(EditorThemes.ColorsSetting,
                $"keyword={SyntaxPink} normal;number={SyntaxBlue} normal");
            host.Paged.SelectBytes(3, 9);
            host.Tokens([new Token(0, prefixLength, "keyword")]);
            host.Draw();
            Assert.Equal(prefixLength, Assert.IsType<string>(host.Paged.DisplayOf(0)).Length);
            var palette = EditorLook.Of(host.Paged).Palette;
            Assert.Equal(TokenStyle.None, palette.GetStyle("keyword"));
            Assert.Equal(TokenStyle.None, palette.GetStyle("number"));
            object geometry = Assert.IsAssignableFrom<object>(host.Geometry());
            object entry = Assert.IsAssignableFrom<object>(host.Entry());
            var selection = host.Paged.SelectionRange;
            long caret = host.Paged.CaretOffset, revision = host.Tab.Doc.Doc.Revision;
            byte[] previous = TextPixels(EntryProperty<DrawingGroup>(entry, "Drawing"));
            Assert.True(Count(previous, Color.Parse(SyntaxPink)) > 10);
            long largestAllocation = 0;

            for (int repaint = 0; repaint < repaints; repaint++)
            {
                string kind = repaint % 2 == 0 ? "number" : "keyword";
                Color expected = Color.Parse(repaint % 2 == 0 ? SyntaxBlue : SyntaxPink);
                Color other = Color.Parse(repaint % 2 == 0 ? SyntaxPink : SyntaxBlue);
                TypingTrace.Reset();
                TypingTrace.Enabled = true;
                host.Tokens([new Token(0, prefixLength, kind)]);
                host.Draw();
                TypingTrace.Enabled = false;
                string summary = TypingTrace.Summary();
                var render = Regex.Match(summary, @"(?m)^paged\.render\s+n=\s*(\d+)[^\r\n]*\balloc=(\d+)");
                Assert.True(render.Success, "The actual paged render allocation was not recorded.");
                int count = int.Parse(render.Groups[1].Value, CultureInfo.InvariantCulture);
                long mean = long.Parse(render.Groups[2].Value, CultureInfo.InvariantCulture);
                Assert.True(count > 0);
                // The summary rounds its mean; the sum prevents extra cached draws from diluting the recolor.
                long allocationUpperBound = checked((mean + 1) * count);
                largestAllocation = Math.Max(largestAllocation, allocationUpperBound);
                output.WriteLine($"repaint={repaint + 1}/{repaints} renders={count} mean={mean} B total-upper={allocationUpperBound} B limit={allocationLimit} B");

                object next = Assert.IsAssignableFrom<object>(host.Entry());
                Assert.NotSame(entry, next);
                Assert.Same(geometry, host.Geometry());
                byte[] pixels = TextPixels(EntryProperty<DrawingGroup>(next, "Drawing"));
                Assert.True(Count(pixels, expected) > 10);
                Assert.Equal(0, Count(pixels, other));
                Assert.False(previous.AsSpan().SequenceEqual(pixels));
                Assert.Equal(selection, host.Paged.SelectionRange);
                Assert.Equal("xxxxxx", host.Paged.SelectedText());
                Assert.Equal(caret, host.Paged.CaretOffset);
                Assert.Equal(revision, host.Tab.Doc.Doc.Revision);
                previous = pixels;
                entry = next;
            }

            host.Tokens([]);
            host.Draw();
            Assert.Same(geometry, host.Geometry());
            byte[] cleared = TextPixels(EntryProperty<DrawingGroup>(host.Entry()!, "Drawing"));
            Assert.Equal(0, Count(cleared, Color.Parse(SyntaxPink)));
            Assert.Equal(0, Count(cleared, Color.Parse(SyntaxBlue)));
            Assert.True(largestAllocation <= allocationLimit,
                $"A color-only repaint allocated up to {largestAllocation} B; the fixed limit is {allocationLimit} B.");
        }
        finally
        {
            TypingTrace.Enabled = tracing;
            TypingTrace.Reset();
        }
    }
}
