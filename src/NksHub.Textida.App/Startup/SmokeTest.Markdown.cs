using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Startup;

internal sealed partial class SmokeTest
{
    private static IEnumerable<Control> MarkdownControls(Control root) => new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>());

    private static IEnumerable<Run> MarkdownRuns(IEnumerable<Inline> inlines) => inlines.SelectMany(inline => inline switch
    {
        Run run => new[] { run },
        Span span => MarkdownRuns(span.Inlines),
        _ => [],
    });

    private static string MarkdownText(TextBlock block) => block.Inlines is { Count: > 0 } inlines
        ? string.Concat(MarkdownRuns(inlines).Select(run => run.Text)) : block.Text ?? "";

    private async Task CheckMarkdownAsync(string temp)
    {
        string source = "# C#\n\n[Contents](#details--end)\n\n"
            + "- outer\n  - inner\n\n    continuation\n\n        nested code\n\n- last\n\n"
            + "1. first\n1. second\n\n***both*** and žluťoučký text.\n\n"
            + "````text\n# literal\n```\n![literal](https://example.invalid/image.png)\n[link](https://example.invalid)\n````\n\n"
            + "name|value\n:---|---:\na\\|b|`code`\n# Details | end\n\n"
            + string.Concat(Enumerable.Range(1, 40).Select(i => $"Following paragraph {i}.\n\n"));
        source = source.ReplaceLineEndings("\r\n");
        byte[] original = Encoding.UTF8.GetBytes(source);
        string hash = Convert.ToHexString(SHA256.HashData(original));
        string file = Path.Combine(temp, "markdown-smoke.md");
        await File.WriteAllBytesAsync(file, original);
        string previousMode = app.Workbench.Settings.GetString("markdown.defaultMode");
        app.Workbench.Set("markdown.defaultMode", System.Text.Json.Nodes.JsonValue.Create("read"));
        var tab = window.OpenFile(file, null);
        var document = tab.Doc;
        int undo = document.Doc.UndoDepth;
        try
        {
            var page = tab.Custom as MarkdownPreview ?? throw new InvalidOperationException("markdown smoke did not open a reading page");
            await page.RenderTask;
            await NextFrameAsync();
            window.UpdateLayout();
            var controls = MarkdownControls(page).ToList();
            var heading = controls.OfType<TextBlock>().Single(block => block.Classes.Contains("previewhead1") && MarkdownText(block) == "C#");
            Check(page.Bounds.Width > 100 && page.Bounds.Height > 100 && heading.Bounds.Width > 0 && heading.TextLayout.TextLines.Count > 0
                && MarkdownRuns(heading.Inlines!).All(run => run.FontWeight == FontWeight.SemiBold), "markdown.heading.native_frame");

            var inner = controls.OfType<TextBlock>().Single(block => MarkdownText(block) == "inner");
            var item = inner.GetLogicalAncestors().OfType<StackPanel>().FirstOrDefault(panel => MarkdownControls(panel).OfType<TextBlock>().Any(block => MarkdownText(block) == "continuation")
                && MarkdownControls(panel).OfType<Border>().Any(border => border.Classes.Contains("previewcode") && MarkdownControls(border).OfType<TextBlock>().Any(block => MarkdownText(block).Contains("nested code", StringComparison.Ordinal))));
            var numbers = controls.OfType<TextBlock>().Select(MarkdownText).Where(text => text is "1." or "2.").ToArray();
            Check(item is not null && !MarkdownControls(item).OfType<TextBlock>().Any(block => MarkdownText(block) == "last")
                && numbers.SequenceEqual(new[] { "1.", "2." }), "markdown.nested_and_ordered_lists");

            var both = controls.OfType<TextBlock>().SelectMany(block => MarkdownRuns(block.Inlines ?? [])).Single(run => run.Text == "both");
            Check(both.FontWeight >= FontWeight.SemiBold && both.FontStyle == FontStyle.Italic, "markdown.strong_emphasis");

            var code = controls.OfType<Border>().Single(border => border.Classes.Contains("previewcode")
                && MarkdownControls(border).OfType<TextBlock>().Any(block => MarkdownText(block).Contains("# literal", StringComparison.Ordinal)));
            string literal = string.Join('\n', MarkdownControls(code).OfType<TextBlock>().Select(MarkdownText));
            Check(literal.Contains("```", StringComparison.Ordinal) && literal.Contains("![literal](https://example.invalid/image.png)", StringComparison.Ordinal)
                && !MarkdownControls(code).Any(control => control is Image or Button), "markdown.fence_is_literal");

            var table = controls.OfType<Grid>().Single(grid => grid.Children.OfType<Border>().Any(border => border.Classes.Contains("previewcell")));
            string Cell(int row, int column) => string.Concat(MarkdownControls(table.Children.OfType<Border>().Single(border => Grid.GetRow(border) == row && Grid.GetColumn(border) == column)).OfType<TextBlock>().Select(MarkdownText));
            var headings = MarkdownTools.Headings(source.Split("\r\n"));
            Check(table.RowDefinitions.Count == 2 && table.ColumnDefinitions.Count == 2 && Cell(0, 0) == "name" && Cell(0, 1) == "value"
                && Cell(1, 0) == "a|b" && Cell(1, 1) == "code" && headings.Select(value => value.Title).SequenceEqual(new[] { "C#", "Details | end" })
                && controls.OfType<TextBlock>().Any(block => block.Classes.Contains("previewhead1") && MarkdownText(block) == "Details | end"), "markdown.table_and_heading_boundaries");

            byte[] Bytes() => document.Doc.ReadBytes(new ByteRange(0, document.Doc.ByteLength));
            bool unchanged = Bytes().SequenceEqual(original) && document.Doc.UndoDepth == undo && !document.Doc.IsModified;
            var contents = controls.OfType<Button>().Single(button => button.Classes.Contains("previewlink"));
            contents.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await NextFrameAsync();
            window.UpdateLayout();
            bool jumped = Math.Abs(page.TopLine - headings[1].Line) <= 3;
            window.SetViewMode(document, ViewMode.Edit);
            tab = window.Active!;
            await tab.SidePreview!.RenderTask;
            tab.Editor!.Select(source.IndexOf("both", StringComparison.Ordinal), 4);
            int caret = tab.Editor.CaretOffset;
            window.SetViewMode(document, ViewMode.Read);
            await ((MarkdownPreview)window.Active!.Custom!).RenderTask;
            window.SetViewMode(document, ViewMode.Text);
            tab = window.Active!;
            bool selected = tab.Editor!.SelectedText == "both" && tab.Editor.CaretOffset == caret;
            bool modesPreservedBytes = Bytes().SequenceEqual(original) && document.Doc.UndoDepth == undo;
            tab.Editor.CaretOffset = tab.Editor.Document.Text.IndexOf("a\\|b", StringComparison.Ordinal);
            window.FormatMarkdownTable();
            bool formatted = document.Doc.UndoDepth == undo + 1 && !Bytes().SequenceEqual(original);
            window.Undo();
            byte[] disk = await File.ReadAllBytesAsync(file);
            Check(unchanged && jumped && selected && modesPreservedBytes && formatted && document.Doc.UndoDepth == undo && !document.Doc.IsModified
                && Bytes().SequenceEqual(original) && disk.SequenceEqual(original) && Convert.ToHexString(SHA256.HashData(Bytes())) == hash, "markdown.anchor_modes_bytes_and_undo");
            _log.Add("markdown.sha256=" + hash);
        }
        finally
        {
            app.Workbench.Set("markdown.defaultMode", System.Text.Json.Nodes.JsonValue.Create(previousMode));
            foreach (var view in document.Views.ToList()) window.CloseTab(view);
        }
    }
}
