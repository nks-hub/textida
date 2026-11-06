using System.Text;
using Markdig.Extensions.Alerts;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Shell;

[Flags]
internal enum ReaderStyle : byte { None = 0, Bold = 1, Italic = 2, Strike = 4, Code = 8, Link = 16 }

/// <summary>A run of the text of a block in one style. <see cref="Source"/> is the offset in the Markdown source where the run's text stands character for character, or -1.</summary>
internal readonly record struct ReaderSpan(int Start, int Length, ReaderStyle Style, int Link, int Source);

internal enum ReaderBlockKind { Paragraph, Heading, Code, TableRow, Rule, Images, Diagram, AlertTitle }

internal enum ReaderContainerKind { Quote, Alert, ListItem, Footnote }

/// <summary>A quote, an alert, an item of a list or a footnote that blocks stand in; blocks of the same instance share it, which is how bars and markers join.</summary>
internal sealed class ReaderContainer
{
    public required ReaderContainerKind Kind { get; init; }
    /// <summary>The list (or footnote group) an item belongs to; items of one list share it.</summary>
    public int List { get; init; } = -1;
    public bool Loose { get; init; }
    /// <summary>The marker of an item: a bullet, a number with its dot, or empty for a task item.</summary>
    public string Marker { get; init; } = "";
    /// <summary>The state of the box of a task item; null for any other item.</summary>
    public bool? Task { get; init; }
    public string AlertKind { get; init; } = "";
    /// <summary>A list that stands directly in an item has no margin of its own.</summary>
    public bool NestedInItem { get; init; }
}

internal sealed record ReaderImage(string Target, string Alt, int Link);

/// <summary>A picture that stands in a sentence: it is drawn in the flow of the text before the character at <see cref="Offset"/> and adds no character of its own.</summary>
internal readonly record struct ReaderPicture(int Offset, ReaderImage Image);

/// <summary>A link of the page: a web or mail address the policy allows (<see cref="External"/>) or a line of the source to jump to (<see cref="Line"/>).</summary>
internal sealed record ReaderLink(string Target, string Label, Uri? External, int? Line);

internal sealed class ReaderTable
{
    public required int Columns { get; init; }
    public required TableColumnAlign?[] Align { get; init; }
    /// <summary>The longest cell text of each column, in characters, which the layout turns into column widths without measuring every row.</summary>
    public required int[] Chars { get; init; }
    public int Rows { get; set; }
}

/// <summary>One block of the page as the reader lays it out: a paragraph, a heading, a slice of a code block, a row of a table, a rule, a row of pictures or a diagram.</summary>
internal sealed class ReaderBlock
{
    public ReaderBlockKind Kind { get; init; }
    public string Text { get; set; } = "";
    public ReaderSpan[] Spans { get; set; } = [];
    public ReaderContainer[] Path { get; init; } = [];
    /// <summary>The items of <see cref="Path"/> (by index) whose marker this block draws, because it is their first block.</summary>
    public int[] Markers { get; set; } = [];
    public int Level { get; init; }
    public int Line { get; set; }
    public int EndLine { get; set; }
    public int SourceStart { get; set; } = -1;
    public int SourceEnd { get; set; } = -1;
    public int DocStart { get; set; }
    /// <summary>The space above the block, in multiples of the base font size.</summary>
    public double GapEm { get; set; }
    /// <summary>A paragraph that stands directly in a tight list item has no margins (GitHub writes no paragraph there).</summary>
    public bool Tight { get; init; }
    /// <summary>Text of a footnote: smaller and muted.</summary>
    public bool Small { get; init; }
    /// <summary>What separates the block from the one before in copied text.</summary>
    public string Join { get; set; } = "\n\n";
    /// <summary>The code block or table this block is a slice of (-1 for none); the slices of one group touch.</summary>
    public int Group { get; init; } = -1;
    public bool GroupFirst { get; init; }
    public bool GroupLast { get; set; }
    public int Row { get; init; }
    public bool Header { get; init; }
    /// <summary>Where each cell of a table row starts in <see cref="Text"/>; the cells are separated by a tab.</summary>
    public int[] Cells { get; init; } = [];
    public ReaderImage[] Images { get; init; } = [];
    /// <summary>The pictures of a paragraph that has text as well, in the order they stand.</summary>
    public ReaderPicture[] Pictures { get; set; } = [];
    public Flowchart? Chart { get; init; }

    public int CellEnd(int cell) => cell + 1 < Cells.Length ? Cells[cell + 1] - 1 : Text.Length;
}

/// <summary>
/// The Markdown source turned into the flat list of blocks that <see cref="MarkdownReader"/> lays out (P9.3.4). It holds no controls and is built off the UI thread. The text of all blocks, joined by a line break,
/// is one position space: the selection is a pair of positions in it, so it survives scrolling and the blocks that are laid out coming and going.
/// </summary>
internal sealed class MarkdownReaderDocument
{
    public required string Source { get; init; }
    public required IReadOnlyList<ReaderBlock> Blocks { get; init; }
    public required IReadOnlyList<ReaderLink> Links { get; init; }
    public required IReadOnlyList<ReaderTable> Tables { get; init; }
    public required IReadOnlyList<MarkdownHeading> Headings { get; init; }
    public required IReadOnlyDictionary<string, int> Targets { get; init; }
    public required int[] Lines { get; init; }
    /// <summary>Why the source could not be parsed; the page then shows it as code below the reason.</summary>
    public string? Problem { get; init; }
    public required string PlainText { get; init; }

    public int Length => PlainText.Length;

    /// <summary>The block that holds a position (a position on the line break after a block belongs to that block).</summary>
    public int BlockAt(int position)
    {
        int lo = 0, hi = Blocks.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Blocks[mid].DocStart <= position) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
}

internal static class MarkdownReaderModel
{
    /// <summary>Lines of a code block per laid-out slice, so a very long block is laid out only where it is seen.</summary>
    internal const int CodeSliceLines = 64;

    internal static readonly Dictionary<string, (string Key, string Icon)> AlertKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOTE"] = (K.PreviewAlertNote, "M8,1.5 A6.5,6.5 0 1 0 8.01,1.5 Z M8,7 L8,11.5 M8,4.6 L8,4.9"),
        ["TIP"] = (K.PreviewAlertTip, "M6,12.5 L10,12.5 M6.5,14.5 L9.5,14.5 M5,9 A3.5,3.5 0 1 1 11,9 C10.3,9.8 10,10.5 10,11.5 L6,11.5 C6,10.5 5.7,9.8 5,9 Z"),
        ["IMPORTANT"] = (K.PreviewAlertImportant, "M2,2.5 L14,2.5 L14,11 L8.5,11 L5,14 L5,11 L2,11 Z M8,5 L8,8 M8,9.3 L8,9.6"),
        ["WARNING"] = (K.PreviewAlertWarning, "M8,2 L14.5,13.5 L1.5,13.5 Z M8,6.5 L8,10 M8,11.7 L8,12"),
        ["CAUTION"] = (K.PreviewAlertCaution, "M5.2,2 L10.8,2 L14,5.2 L14,10.8 L10.8,14 L5.2,14 L2,10.8 L2,5.2 Z M8,5 L8,8.5 M8,10.5 L8,10.9"),
    };

    public static MarkdownReaderDocument Build(string source)
    {
        var lines = MarkdownSyntax.LineStarts(source);
        MarkdownDocument document;
        try { document = MarkdownSyntax.Parse(source, PluginLimits.PreviewMarkdownMaxDepth); }
        catch (MarkdownSyntaxException e) { return Unparsed(source, lines, MarkdownMessages.Reason(e)); }
        var builder = new Builder(source, lines, MarkdownSyntax.HeadingTargets(document));
        foreach (var block in document) builder.Block(block, []);
        return builder.Finish(MarkdownSyntax.Headings(document));
    }

    /// <summary>A source the parser refused (nesting beyond its limit, a table that would add too many cells) is shown whole, as code below the reason.</summary>
    private static MarkdownReaderDocument Unparsed(string source, int[] lines, string reason)
    {
        var builder = new Builder(source, lines, new Dictionary<string, int>());
        builder.Note(Loc.Get(K.PreviewMarkdownLimit, ("reason", reason)), 0);
        builder.CodeLines([.. Enumerable.Range(0, lines.Length).Select(i => (Start: lines[i], End: i + 1 < lines.Length ? lines[i + 1] : source.Length))], 0);
        var doc = builder.Finish([]);
        return new MarkdownReaderDocument
        {
            Source = doc.Source, Blocks = doc.Blocks, Links = doc.Links, Tables = doc.Tables, Headings = doc.Headings, Targets = doc.Targets,
            Lines = doc.Lines, PlainText = doc.PlainText, Problem = reason,
        };
    }

    private sealed class Builder(string source, int[] lines, IReadOnlyDictionary<string, int> targets)
    {
        internal readonly string Source = source;
        internal readonly IReadOnlyDictionary<string, int> Targets = targets;
        private readonly List<ReaderBlock> _blocks = [];
        private readonly List<ReaderLink> _links = [];
        private readonly List<ReaderTable> _tables = [];
        private readonly HashSet<ReaderContainer> _pendingMarkers = [];
        private int _lists, _groups;

        private int LineOf(int offset) => MarkdownSyntax.LineAt(lines, Math.Clamp(offset, 0, Math.Max(0, Source.Length)));

        public void Block(Block block, ReaderContainer[] path, int listDepth = 0, int orderedDepth = 0)
        {
            switch (block)
            {
                case ListBlock list:
                    ListBlock(list, path, listDepth, orderedDepth);
                    break;
                case AlertBlock alert:
                {
                    var box = new ReaderContainer { Kind = ReaderContainerKind.Alert, AlertKind = alert.Kind.ToString() };
                    ReaderContainer[] inner = [.. path, box];
                    string kind = alert.Kind.ToString();
                    string title = AlertKinds.TryGetValue(kind, out var look) ? Loc.Get(look.Key) : kind;
                    Add(new ReaderBlock { Kind = ReaderBlockKind.AlertTitle, Path = inner, Text = title, Line = alert.Line, EndLine = alert.Line, SourceStart = alert.Span.Start, SourceEnd = alert.Span.Start });
                    foreach (var child in alert) Block(child, inner, listDepth, orderedDepth);
                    break;
                }
                case QuoteBlock quote:
                {
                    ReaderContainer[] inner = [.. path, new ReaderContainer { Kind = ReaderContainerKind.Quote }];
                    int before = _blocks.Count;
                    foreach (var child in quote) Block(child, inner, listDepth, orderedDepth);
                    if (_blocks.Count == before) Add(Empty(inner, quote));
                    break;
                }
                case FootnoteGroup group:
                {
                    Add(new ReaderBlock { Kind = ReaderBlockKind.Rule, Level = 1, Path = path, Line = group.Line, EndLine = group.Line, SourceStart = group.Span.Start, SourceEnd = group.Span.Start });
                    int id = _lists++;
                    foreach (var note in group.OfType<Footnote>())
                    {
                        var item = new ReaderContainer { Kind = ReaderContainerKind.Footnote, List = id, Marker = note.Order + "." };
                        _pendingMarkers.Add(item);
                        ReaderContainer[] inner = [.. path, item];
                        int before = _blocks.Count;
                        foreach (var child in note) Block(child, inner, listDepth, orderedDepth);
                        if (_blocks.Count == before) Add(Empty(inner, note));
                    }
                    break;
                }
                case Table table:
                    TableBlock(table, path);
                    break;
                case CodeBlock code:
                    CodeBlock(code, path);
                    break;
                case ThematicBreakBlock rule:
                    Add(new ReaderBlock { Kind = ReaderBlockKind.Rule, Path = path, Line = rule.Line, EndLine = rule.Line, SourceStart = rule.Span.Start, SourceEnd = rule.Span.End });
                    break;
                case HeadingBlock heading:
                    Text(new ReaderBlock { Kind = ReaderBlockKind.Heading, Level = heading.Level, Path = path }, heading, heading.Inline);
                    break;
                case LinkReferenceDefinitionGroup or LinkReferenceDefinition or BlankLineBlock:
                    break; // invisible on GitHub as well
                case ParagraphBlock paragraph:
                    Paragraph(paragraph, path);
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    Text(new ReaderBlock { Kind = ReaderBlockKind.Paragraph, Path = path, Tight = TightIn(path), Small = SmallIn(path) }, leaf, leaf.Inline);
                    break;
                case LeafBlock leaf:
                    // an HTML block and any other block without inlines stays the text of its lines
                    SourceLines(leaf, path);
                    break;
                case ContainerBlock container:
                    foreach (var child in container) Block(child, path, listDepth, orderedDepth);
                    break;
            }
        }

        private static bool TightIn(ReaderContainer[] path) => path.Length > 0 && path[^1] is { Kind: ReaderContainerKind.ListItem, Loose: false } or { Kind: ReaderContainerKind.Footnote };

        private static bool SmallIn(ReaderContainer[] path) => path.Any(c => c.Kind == ReaderContainerKind.Footnote);

        private ReaderBlock Empty(ReaderContainer[] path, Block block) => new()
        {
            Kind = ReaderBlockKind.Paragraph, Path = path, Tight = true, Small = SmallIn(path), Line = block.Line, EndLine = block.Line,
            SourceStart = Math.Max(0, block.Span.Start), SourceEnd = Math.Max(0, block.Span.Start),
        };

        private void ListBlock(ListBlock list, ReaderContainer[] path, int listDepth, int orderedDepth)
        {
            int id = _lists++;
            long number = long.TryParse(list.OrderedStart, out var first) ? first : 1;
            bool nested = path.Length > 0 && path[^1].Kind == ReaderContainerKind.ListItem;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                bool? task = item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList box } ? box.Checked : null;
                string marker = task is not null ? "" : list.IsOrdered ? Ordinal(number++, listDepth) + "." : listDepth switch { 0 => "•", 1 => "◦", _ => "▪" };
                var container = new ReaderContainer { Kind = ReaderContainerKind.ListItem, List = id, Loose = list.IsLoose, Marker = marker, Task = task, NestedInItem = nested };
                _pendingMarkers.Add(container);
                ReaderContainer[] inner = [.. path, container];
                int before = _blocks.Count;
                foreach (var child in item) Block(child, inner, listDepth + 1, orderedDepth + (list.IsOrdered ? 1 : 0));
                if (_blocks.Count == before) Add(Empty(inner, item));
            }
        }

        /// <summary>GitHub numbers a list in a list with small roman numerals and one deeper with small letters.</summary>
        private static string Ordinal(long number, int listDepth)
        {
            if (listDepth == 0 || number <= 0 || number > 3999) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (listDepth >= 2)
            {
                var letters = new StringBuilder();
                for (long n = number; n > 0; n = (n - 1) / 26) letters.Insert(0, (char)('a' + (n - 1) % 26));
                return letters.ToString();
            }
            var roman = new StringBuilder();
            foreach (var (value, digits) in new[] { (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i") })
                for (; number >= value; number -= value) roman.Append(digits);
            return roman.ToString();
        }

        /// <summary>A paragraph of pictures only is a row of pictures (GitHub's badges); pictures in a sentence stay in its flow.</summary>
        private void Paragraph(ParagraphBlock paragraph, ReaderContainer[] path)
        {
            var images = new List<ReaderPicture>();
            var block = new ReaderBlock { Kind = ReaderBlockKind.Paragraph, Path = path, Tight = TightIn(path), Small = SmallIn(path) };
            var inlines = new Inlines(this, images);
            inlines.Walk(paragraph.Inline, ReaderStyle.None, -1);
            if (images.Count == 0 || !string.IsNullOrWhiteSpace(inlines.Text.ToString()))
            {
                block.Pictures = [.. images];
                Finish(block, paragraph, inlines);
                return;
            }
            Add(new ReaderBlock
            {
                Kind = ReaderBlockKind.Images, Path = path, Images = [.. images.Select(p => p.Image)], Tight = block.Tight, Small = block.Small,
                Line = paragraph.Line, EndLine = LineOf(paragraph.Span.End), SourceStart = paragraph.Span.Start, SourceEnd = paragraph.Span.End,
            });
        }

        private void Text(ReaderBlock block, Block owner, ContainerInline? inline)
        {
            var inlines = new Inlines(this, null);
            inlines.Walk(inline, block.Kind == ReaderBlockKind.Heading ? ReaderStyle.Bold : ReaderStyle.None, -1);
            Finish(block, owner, inlines);
        }

        private void Finish(ReaderBlock block, Block owner, Inlines inlines)
        {
            block.Text = inlines.Text.ToString();
            block.Spans = [.. inlines.Spans];
            block.Line = owner.Line;
            block.EndLine = LineOf(owner.Span.End);
            block.SourceStart = owner.Span.Start;
            block.SourceEnd = owner.Span.End + 1;
            Add(block);
        }

        public void Note(string text, int line) => Add(new ReaderBlock
        {
            Kind = ReaderBlockKind.Paragraph, Text = text, Spans = [new ReaderSpan(0, text.Length, ReaderStyle.None, -1, -1)], Line = line, EndLine = line, SourceStart = 0, SourceEnd = 0,
        });

        private void SourceLines(LeafBlock leaf, ReaderContainer[] path)
        {
            var text = new StringBuilder();
            var spans = new List<ReaderSpan>();
            var group = leaf.Lines;
            for (int i = 0; i < group.Count; i++)
            {
                var slice = group.Lines[i].Slice;
                if (i > 0) text.Append('\n');
                spans.Add(new ReaderSpan(text.Length, slice.Length, ReaderStyle.None, -1, ReferenceEquals(slice.Text, Source) ? slice.Start : -1));
                text.Append(slice.AsSpan());
            }
            Add(new ReaderBlock
            {
                Kind = ReaderBlockKind.Paragraph, Path = path, Tight = TightIn(path), Small = SmallIn(path), Text = text.ToString(), Spans = [.. spans],
                Line = leaf.Line, EndLine = LineOf(leaf.Span.End), SourceStart = leaf.Span.Start, SourceEnd = leaf.Span.End + 1,
            });
        }

        private void CodeBlock(CodeBlock code, ReaderContainer[] path)
        {
            if (code is FencedCodeBlock { Info: { } info } && info.Equals("mermaid", StringComparison.OrdinalIgnoreCase))
            {
                if (MermaidFlowchart.Parse(code.Lines.ToString(), PluginLimits.PreviewMermaidMaxNodes, PluginLimits.PreviewMermaidMaxEdges) is { } chart)
                {
                    Add(new ReaderBlock { Kind = ReaderBlockKind.Diagram, Path = path, Chart = chart, Line = code.Line, EndLine = LineOf(code.Span.End), SourceStart = code.Span.Start, SourceEnd = code.Span.End + 1 });
                    return;
                }
                Add(new ReaderBlock
                {
                    Kind = ReaderBlockKind.Paragraph, Path = path, Text = Loc.Get(K.PreviewMermaidUnsupported), Line = code.Line, EndLine = code.Line,
                    SourceStart = code.Span.Start, SourceEnd = code.Span.Start,
                });
            }
            var group = code.Lines;
            var slices = new List<(int Start, int End)>(group.Count);
            for (int i = 0; i < group.Count; i++)
            {
                var slice = group.Lines[i].Slice;
                slices.Add(ReferenceEquals(slice.Text, Source) ? (slice.Start, slice.End + 1) : (-1, -1));
            }
            var texts = new string[group.Count];
            for (int i = 0; i < group.Count; i++) texts[i] = group.Lines[i].Slice.ToString();
            CodeLines(slices, code.Line, path, texts, code.Span);
        }

        /// <summary>The lines of a code block in slices of <see cref="CodeSliceLines"/>; <paramref name="texts"/> null takes each line from the source.</summary>
        public void CodeLines(IReadOnlyList<(int Start, int End)> sourceLines, int firstLine, ReaderContainer[]? path = null, string[]? texts = null, SourceSpan? span = null)
        {
            path ??= [];
            int group = _groups++;
            int count = Math.Max(1, sourceLines.Count);
            for (int first = 0; first < count; first += CodeSliceLines)
            {
                var text = new StringBuilder();
                var spans = new List<ReaderSpan>();
                int last = Math.Min(count, first + CodeSliceLines);
                for (int i = first; i < last && i < sourceLines.Count; i++)
                {
                    if (i > first) text.Append('\n');
                    var (start, end) = sourceLines[i];
                    string line = texts?[i] ?? Source[start..end].TrimEnd('\r', '\n');
                    spans.Add(new ReaderSpan(text.Length, line.Length, ReaderStyle.Code, -1, start));
                    text.Append(line);
                }
                int lineIndex = firstLine + first;
                int sourceStart = first < sourceLines.Count && sourceLines[first].Start >= 0 ? sourceLines[first].Start : span?.Start ?? 0;
                int sourceEnd = last - 1 < sourceLines.Count && sourceLines[last - 1].End >= 0 ? sourceLines[last - 1].End : span is { } s ? s.End + 1 : 0;
                Add(new ReaderBlock
                {
                    Kind = ReaderBlockKind.Code, Path = path, Text = text.ToString(), Spans = [.. spans], Group = group, GroupFirst = first == 0, GroupLast = last >= count,
                    Line = lineIndex, EndLine = firstLine + last - 1, SourceStart = sourceStart, SourceEnd = sourceEnd, Join = first == 0 ? "\n\n" : "\n",
                });
            }
        }

        private void TableBlock(Table table, ReaderContainer[] path)
        {
            int columns = table.Max(row => ((TableRow)row).Count);
            var info = new ReaderTable
            {
                Columns = columns, Chars = new int[columns],
                Align = [.. Enumerable.Range(0, columns).Select(c => c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null)],
            };
            int group = _groups++;
            _tables.Add(info);
            int tableIndex = _tables.Count - 1;
            int body = 0;
            for (int r = 0; r < table.Count; r++)
            {
                var row = (TableRow)table[r];
                var inlines = new Inlines(this, null);
                var cells = new int[columns];
                for (int c = 0; c < columns; c++)
                {
                    if (c > 0) inlines.Plain("\t");
                    cells[c] = inlines.Text.Length;
                    if (c < row.Count)
                        foreach (var child in (TableCell)row[c])
                            if (child is LeafBlock { Inline: { } inline }) inlines.Walk(inline, row.IsHeader ? ReaderStyle.Bold : ReaderStyle.None, -1, inCell: true);
                    info.Chars[c] = Math.Max(info.Chars[c], inlines.Text.Length - cells[c]);
                }
                Add(new ReaderBlock
                {
                    Kind = ReaderBlockKind.TableRow, Path = path, Text = inlines.Text.ToString(), Spans = [.. inlines.Spans], Cells = cells, Group = group, GroupFirst = r == 0,
                    GroupLast = r == table.Count - 1, Row = row.IsHeader ? -1 : body++, Header = row.IsHeader, Level = tableIndex,
                    Line = row.Line, EndLine = LineOf(row.Span.End), SourceStart = row.Span.Start, SourceEnd = row.Span.End + 1, Join = r == 0 ? "\n\n" : "\n",
                });
            }
            info.Rows = table.Count;
        }

        private void Add(ReaderBlock block)
        {
            var markers = new List<int>();
            for (int i = 0; i < block.Path.Length; i++)
                if (_pendingMarkers.Remove(block.Path[i])) markers.Add(i);
            block.Markers = [.. markers];
            var previous = _blocks.Count > 0 ? _blocks[^1] : null;
            block.GapEm = Gap(previous, block);
            if (previous is not null && block.Join == "\n\n" && block.Group < 0 && Siblings(previous, block)) block.Join = "\n";
            if (previous is not null && block.Group >= 0 && block.Group == previous.Group) previous.GroupLast = false;
            _blocks.Add(block);
        }

        /// <summary>Two blocks in neighbouring items of one tight list (or a nested list that starts in the item before) copy one per line.</summary>
        private static bool Siblings(ReaderBlock previous, ReaderBlock next)
        {
            if (!previous.Tight && next.Markers.Length == 0) return false;
            int common = Common(previous, next);
            return next.Markers.Length > 0 && (common < previous.Path.Length || previous.Tight) && next.Path[^1].Kind is ReaderContainerKind.ListItem or ReaderContainerKind.Footnote && !next.Path[^1].Loose;
        }

        private static int Common(ReaderBlock a, ReaderBlock b)
        {
            int common = 0;
            while (common < a.Path.Length && common < b.Path.Length && ReferenceEquals(a.Path[common], b.Path[common])) common++;
            return common;
        }

        /// <summary>
        /// The space between two blocks, as GitHub's style sheet gives it with collapsed margins: 16 px under a paragraph, quote, list, table and code; 24 px above a heading and around a rule;
        /// 0.25em between the items of a tight list and none around a list nested in an item; an alert adds 8 px of padding inside its bar. All in multiples of the base font size.
        /// </summary>
        internal static double Gap(ReaderBlock? previous, ReaderBlock next)
        {
            double padding = 0;
            if (previous is null)
            {
                foreach (var c in next.Path) if (c.Kind == ReaderContainerKind.Alert) padding += 0.5;
                return padding;
            }
            if (next.Group >= 0 && next.Group == previous.Group) return 0;
            int common = Common(previous, next);
            for (int i = common; i < previous.Path.Length; i++) if (previous.Path[i].Kind == ReaderContainerKind.Alert) padding += 0.5;
            for (int i = common; i < next.Path.Length; i++) if (next.Path[i].Kind == ReaderContainerKind.Alert) padding += 0.5;
            double bottom = previous.Tight ? 0 : previous.Kind == ReaderBlockKind.Rule ? (previous.Level == 1 ? 1 : 1.5) : 1;
            double top = next.Kind switch
            {
                ReaderBlockKind.Heading => 1.5,
                ReaderBlockKind.Rule => next.Level == 1 ? 1 : 1.5,
                _ => 0,
            };
            bool sibling = common < previous.Path.Length && common < next.Path.Length && previous.Path[common].Kind is ReaderContainerKind.ListItem or ReaderContainerKind.Footnote
                && previous.Path[common].List == next.Path[common].List;
            double containers = 0;
            for (int i = common; i < previous.Path.Length; i++)
            {
                var c = previous.Path[i];
                if (c.Kind is ReaderContainerKind.Quote or ReaderContainerKind.Alert) containers = 1;
                else if (c.Kind == ReaderContainerKind.ListItem && !(i == common && sibling) && !c.NestedInItem) containers = 1;
            }
            if (sibling) top = Math.Max(top, previous.Path[common].Loose ? 1 : 0.25);
            if (next.Kind == ReaderBlockKind.Paragraph && !next.Tight && next.Path.Length > 0 && next.Path[^1] is { Kind: ReaderContainerKind.ListItem, Loose: true }) top = Math.Max(top, 1);
            return Math.Max(bottom, Math.Max(top, containers)) + padding;
        }

        public MarkdownReaderDocument Finish(IReadOnlyList<MarkdownHeading> headings)
        {
            if (_blocks.Count == 0) _blocks.Add(new ReaderBlock { Kind = ReaderBlockKind.Paragraph, SourceStart = 0, SourceEnd = 0 });
            var plain = new StringBuilder();
            for (int i = 0; i < _blocks.Count; i++)
            {
                if (i > 0) plain.Append('\n');
                _blocks[i].DocStart = plain.Length;
                plain.Append(_blocks[i].Text);
            }
            return new MarkdownReaderDocument
            {
                Source = Source, Blocks = _blocks, Links = _links, Tables = _tables, Headings = headings, Targets = Targets, Lines = lines, PlainText = plain.ToString(),
            };
        }

        /// <summary>Collects the text and styled runs of inlines; pictures go to <c>images</c> with the place they stand at when a list is given, else they stay their alt text.</summary>
        private sealed class Inlines(Builder owner, List<ReaderPicture>? images)
        {
            public readonly StringBuilder Text = new();
            public readonly List<ReaderSpan> Spans = [];

            public void Plain(string text, ReaderStyle style = ReaderStyle.None, int link = -1) => Emit(text, style, link, -1);

            private bool _afterTask;

            private void Emit(string text, ReaderStyle style, int link, int at)
            {
                if (_afterTask)
                {
                    // the space between the box of a task and its text
                    _afterTask = false;
                    int trimmed = text.Length - text.TrimStart().Length;
                    text = text[trimmed..];
                    if (at >= 0) at += trimmed;
                }
                if (text.Length == 0) return;
                if (Spans.Count > 0 && Spans[^1] is var last && last.Style == style && last.Link == link && last.Start + last.Length == Text.Length
                    && (last.Source < 0 && at < 0 || last.Source >= 0 && at == last.Source + last.Length))
                    Spans[^1] = last with { Length = last.Length + text.Length };
                else Spans.Add(new ReaderSpan(Text.Length, text.Length, style, link, at));
                Text.Append(text);
            }

            private int SourceOf(StringSlice slice) => ReferenceEquals(slice.Text, owner.Source) ? slice.Start : -1;

            public void Walk(ContainerInline? container, ReaderStyle style, int link, bool inCell = false)
            {
                if (container is null) return;
                for (var inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
                {
                    switch (inline)
                    {
                        case LiteralInline literal:
                        {
                            string text = literal.Content.ToString();
                            if (inCell) text = text.Replace('\t', ' ');
                            Emit(text, style, link, literal.Content.Length == text.Length ? SourceOf(literal.Content) : -1);
                            break;
                        }
                        case HtmlEntityInline entity:
                            Emit(entity.Transcoded.ToString(), style, link, -1);
                            break;
                        case HtmlInline html:
                            Emit(html.Tag, style, link, html.Span.Start >= 0 && html.Span.Length == html.Tag.Length ? html.Span.Start : -1);
                            break;
                        case CodeInline code:
                            Emit(code.Content, style | ReaderStyle.Code, link, -1);
                            break;
                        case LineBreakInline { IsHard: true }:
                            Emit(inCell ? " " : "\n", style, link, -1);
                            break;
                        case LineBreakInline:
                            Emit(" ", style, link, -1);
                            break;
                        case EmphasisInline { DelimiterChar: '~' } struck:
                            Walk(struck, style | ReaderStyle.Strike, link, inCell);
                            break;
                        case EmphasisInline emphasis:
                            Walk(emphasis, style | (emphasis.DelimiterCount >= 2 ? ReaderStyle.Bold : ReaderStyle.None) | (emphasis.DelimiterCount % 2 == 1 ? ReaderStyle.Italic : ReaderStyle.None), link, inCell);
                            break;
                        case LinkInline { IsImage: true } image:
                            if (images is not null) images.Add(new ReaderPicture(Text.Length, new ReaderImage(image.Url ?? "", MarkdownSyntax.Text(image), link)));
                            else Emit(MarkdownSyntax.Text(image), style, link, -1);
                            break;
                        case LinkInline target:
                            Link(target, target.Url ?? "", MarkdownSyntax.Text(target), style, link, inCell);
                            break;
                        case AutolinkInline auto:
                            Link(null, auto.IsEmail ? "mailto:" + auto.Url : auto.Url, auto.Url, style, link, inCell);
                            break;
                        case FootnoteLink note:
                        {
                            int? line = note.IsBackLink ? note.Footnote.Links.FirstOrDefault(l => !l.IsBackLink)?.Line : note.Footnote.Line;
                            string label = note.IsBackLink ? "↩" : "[" + note.Index + "]";
                            if (line is { } at)
                            {
                                string name = note.IsBackLink ? Loc.Get(K.PreviewFootnoteBack) : Loc.Get(K.PreviewFootnoteName, ("number", note.Index));
                                owner._links.Add(new ReaderLink("#" + at, name, null, at));
                                Emit(label, style | ReaderStyle.Link, owner._links.Count - 1, -1);
                            }
                            else Emit(label, style, link, -1);
                            break;
                        }
                        case TaskList:
                            _afterTask = Text.Length == 0; // the box is the marker of the item
                            break;
                        case ContainerInline nested:
                            Walk(nested, style, link, inCell);
                            break;
                        default:
                            if (inline.Span.Start >= 0 && inline.Span.End < owner.Source.Length)
                                Emit(owner.Source.Substring(inline.Span.Start, inline.Span.Length), style, link, inline.Span.Start);
                            break;
                    }
                }
            }

            /// <summary>
            /// A link the policy allows (the web, mail) or a heading of the page becomes a link of the page; any other stays text followed by its target. A link inside a link keeps the outer one.
            /// </summary>
            private void Link(ContainerInline? label, string target, string plain, ReaderStyle style, int outer, bool inCell)
            {
                int link = outer;
                if (outer < 0)
                {
                    if (target.StartsWith('#'))
                    {
                        string fragment;
                        try { fragment = Uri.UnescapeDataString(target[1..]); }
                        catch (UriFormatException) { fragment = target[1..]; }
                        if (owner.Targets.TryGetValue(fragment, out int line)) link = AddLink(target, plain, null, line);
                    }
                    else if (PreviewPolicy.LinkTarget(target) is { } uri) link = AddLink(target, plain, uri, null);
                }
                if (link < 0)
                {
                    if (label is not null) Walk(label, style, -1, inCell);
                    else Plain(plain, style);
                    if (plain != target) Plain(" (" + target + ")", style);
                    return;
                }
                if (label is not null) Walk(label, style | ReaderStyle.Link, link, inCell);
                else Emit(plain, style | ReaderStyle.Link, link, -1);
            }

            private int AddLink(string target, string label, Uri? uri, int? line)
            {
                owner._links.Add(new ReaderLink(target, label, uri, line));
                return owner._links.Count - 1;
            }
        }
    }
}
