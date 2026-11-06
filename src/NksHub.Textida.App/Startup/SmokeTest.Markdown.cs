using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Markdown;

namespace NksHub.Textida.App.Startup;

internal sealed partial class SmokeTest
{
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
            var reader = page.Reader ?? throw new InvalidOperationException("markdown smoke has no reader");
            var blocks = reader.Document.Blocks;
            int Find(Func<ReaderBlock, bool> match) { for (int i = 0; i < blocks.Count; i++) if (match(blocks[i])) return i; return -1; }
            int heading = Find(b => b.Kind == ReaderBlockKind.Heading && b.Level == 1 && b.Text == "C#");
            Check(page.Bounds.Width > 100 && page.Bounds.Height > 100 && heading >= 0 && reader.IsMeasured(heading) && reader.BlockBounds(heading).Width > 0
                && blocks[heading].Spans.All(s => s.Style.HasFlag(ReaderStyle.Bold)), "markdown.heading.native_frame");

            int inner = Find(b => b.Text == "inner"), continued = Find(b => b.Text == "continuation"), nested = Find(b => b.Kind == ReaderBlockKind.Code && b.Text.Contains("nested code", StringComparison.Ordinal));
            int last = Find(b => b.Text == "last");
            var item = inner >= 0 ? blocks[inner].Path[^1] : null;
            var numbers = blocks.Where(b => b.Text is "first" or "second").Select(b => b.Path[^1].Marker).ToArray();
            Check(item is not null && continued >= 0 && nested >= 0 && blocks[continued].Path.Contains(item) && blocks[nested].Path.Contains(item) && last >= 0 && !blocks[last].Path.Contains(item)
                && numbers.SequenceEqual(new[] { "1.", "2." }), "markdown.nested_and_ordered_lists");

            int bothBlock = Find(b => b.Text.StartsWith("both", StringComparison.Ordinal));
            Check(bothBlock >= 0 && blocks[bothBlock].Spans.Any(s => s.Start == 0 && s.Length == 4 && s.Style.HasFlag(ReaderStyle.Bold) && s.Style.HasFlag(ReaderStyle.Italic)), "markdown.strong_emphasis");

            int code = Find(b => b.Kind == ReaderBlockKind.Code && b.Text.Contains("# literal", StringComparison.Ordinal));
            Check(code >= 0 && blocks[code].Text.Contains("```", StringComparison.Ordinal) && blocks[code].Text.Contains("![literal](https://example.invalid/image.png)", StringComparison.Ordinal)
                && !blocks.Any(b => b.Kind == ReaderBlockKind.Images), "markdown.fence_is_literal");

            var rows = blocks.Where(b => b.Kind == ReaderBlockKind.TableRow).ToList();
            var headings = MarkdownTools.Headings(source.Split("\r\n"));
            Check(rows.Count == 2 && rows[0].Cells.Length == 2 && rows[0].Text == "name\tvalue" && rows[1].Text == "a|b\tcode" &&headings.Select(value => value.Title).SequenceEqual(new[] { "C#", "Details | end" })
                && Find(b => b.Kind == ReaderBlockKind.Heading && b.Text == "Details | end") >= 0, "markdown.table_and_heading_boundaries");

            byte[] Bytes() => document.Doc.ReadBytes(new ByteRange(0, document.Doc.ByteLength));
            bool unchanged = Bytes().SequenceEqual(original) && document.Doc.UndoDepth == undo && !document.Doc.IsModified;
            int contents = reader.Document.Links.Select((l, i) => (l, i)).Single(x => x.l.Line is not null).i;
            reader.ActivateLink(contents);
            // the scroll lands within a few frames; a loaded machine (Rosetta, a watcher beside it) needs more than one
            bool jumped = false;
            for (int i = 0; i < 60 && !jumped; i++)
            {
                await NextFrameAsync();
                window.UpdateLayout();
                jumped = Math.Abs(page.TopLine - headings[1].Line) <= 3;
            }
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
            bool restored = unchanged && jumped && selected && modesPreservedBytes && formatted && document.Doc.UndoDepth == undo && !document.Doc.IsModified
                && Bytes().SequenceEqual(original) && disk.SequenceEqual(original) && Convert.ToHexString(SHA256.HashData(Bytes())) == hash;
            Check(restored, restored ? "markdown.anchor_modes_bytes_and_undo"
                : $"markdown.anchor_modes_bytes_and_undo (unchanged={unchanged} jumped={jumped} selected={selected} modes={modesPreservedBytes} formatted={formatted} undo={document.Doc.UndoDepth - undo} modified={document.Doc.IsModified} disk={disk.SequenceEqual(original)})");
            _log.Add("markdown.sha256=" + hash);
        }
        finally
        {
            app.Workbench.Set("markdown.defaultMode", System.Text.Json.Nodes.JsonValue.Create(previousMode));
            foreach (var view in document.Views.ToList()) window.CloseTab(view);
        }
    }
}
