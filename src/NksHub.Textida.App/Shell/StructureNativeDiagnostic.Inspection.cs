using System.Diagnostics;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.VisualTree;
using NksHub.Textida.Core.Data.Structure;

namespace NksHub.Textida.App.Shell;

internal sealed partial class StructureNativeDiagnostic
{
    private sealed record Inspection(string Nonce, string Target);
    private readonly HashSet<string> inspectionNonces = [];
    private string? lastInspectionNonce;
    private JsonObject? lastPointer, lastWheel;
    private long inputSequence;

    private static Inspection ParseInspection(string request)
    {
        var json = JsonNode.Parse(request)?.AsObject() ?? throw new InvalidDataException("The inspection request is not an object.");
        string nonce = json["nonce"]?.GetValue<string>() ?? "";
        string target = json["target"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > 128 || nonce.Any(char.IsControl) || target.Length > 4096)
            throw new InvalidDataException("Inspection requires a nonce of 1 to 128 characters and a target of at most 4096 characters.");
        return new(nonce, target);
    }

    private JsonObject Inspect(Inspection request)
    {
        if (inspectionNonces.Count >= 4096 || !inspectionNonces.Add(request.Nonce))
            throw new InvalidDataException("Inspection nonces must be unique; at most 4096 requests are accepted.");
        var result = StatusJson();
        var documentId = result["sourceDocumentId"]?.GetValue<string>();
        var revision = result["sourceRevision"]?.GetValue<long>();
        result["status"] = "inspection";
        result["inspectionNonce"] = request.Nonce;
        result["requestedTarget"] = request.Target;
        result["row"] = null;
        result["wheelPoint"] = null;
        var viewport = TreeViewport(window, view.Tree);
        result["viewport"] = RectJson(viewport);
        if (!InspectionReady()) result["result"] = "not-ready";
        else
        {
            result["wheelPoint"] = SafePoint(window, view.Tree, viewport);
            if (request.Target.Length == 0) result["result"] = "viewport";
            else
            {
                var matches = Items().Where(item => AutomationProperties.GetName(item) == request.Target).Take(2).ToArray();
                result["result"] = matches.Length switch { 0 => "not-found", 1 => "unique", _ => "ambiguous" };
                if (matches.Length == 1)
                {
                    var row = InspectRow(matches[0]);
                    result["row"] = row;
                    if (row["headerRect"] is null) result["result"] = "offscreen";
                    else if (row["headerPoint"] is null) result["result"] = "no-safe-hit";
                }
            }
        }
        var latest = source?.Invoke();
        if (!InspectionReady() || latest?.DocumentId.ToString() != documentId || latest?.Revision != revision)
        {
            result["result"] = "not-ready";
            result["row"] = null; result["wheelPoint"] = null;
        }
        result["inspectionCompleteQpc"] = Stopwatch.GetTimestamp();
        lastInspectionNonce = request.Nonce;
        return result;
    }

    private bool InspectionReady() => window.IsVisible && view.Working.IsCompleted && view.Parsing.IsCompleted
        && view.IsArrangeValid && view.IsMeasureValid && view.Tree.IsArrangeValid && view.Tree.IsMeasureValid;

    private JsonObject InspectRow(TreeViewItem target)
    {
        var node = (StructureNode)target.Tag!;
        var children = target.Items.OfType<TreeViewItem>().Where(item => item.Tag is StructureNode).ToArray();
        var list = new JsonArray();
        JsonObject? firstVisible = null;
        int headerHitTests = 0;
        foreach (var child in children.Take(StructureView.PageSize))
        {
            var data = (StructureNode)child.Tag!;
            list.Add((JsonNode)new JsonObject { ["nodeId"] = data.Id, ["parentNodeId"] = data.ParentId,
                ["name"] = BoundedDiagnostic(AutomationProperties.GetName(child), 4096) });
            if (firstVisible is not null || headerHitTests >= 8) continue;
            if (child.Header is not Visual childHeader || ClippedPixels(window, childHeader) is null) continue;
            headerHitTests++;
            var geometry = InspectHeader(window, view.Tree, child);
            if (geometry?["headerPoint"] is null) continue;
            firstVisible = new JsonObject
            {
                ["nodeId"] = data.Id, ["parentNodeId"] = data.ParentId, ["treeParentNodeId"] = ParentNodeId(child),
                ["name"] = BoundedDiagnostic(AutomationProperties.GetName(child), 4096),
                ["headerRect"] = geometry["headerRect"]?.DeepClone(), ["headerPoint"] = geometry["headerPoint"]?.DeepClone(),
            };
        }
        var header = InspectHeader(window, view.Tree, target);
        return new()
        {
            ["nodeId"] = node.Id, ["parentNodeId"] = node.ParentId, ["treeParentNodeId"] = ParentNodeId(target),
            ["name"] = BoundedDiagnostic(AutomationProperties.GetName(target), 4096), ["expanded"] = target.IsExpanded,
            ["children"] = children.Length, ["childRows"] = list, ["childrenTruncated"] = children.Length > StructureView.PageSize,
            ["firstVisibleChild"] = firstVisible,
            ["headerRect"] = header?["headerRect"]?.DeepClone(), ["headerPoint"] = header?["headerPoint"]?.DeepClone(),
        };
    }

    internal static JsonObject ActualSource(TreeView tree, object? source)
    {
        bool inTree = source is Visual visual && (ReferenceEquals(visual, tree) || tree.IsVisualAncestorOf(visual));
        var item = inTree ? SourceItem(tree, source) : null;
        return new()
        {
            ["sourceType"] = source?.GetType().FullName, ["inTree"] = inTree,
            ["inHeader"] = InHeader(item, source),
            ["nodeId"] = item?.Tag is StructureNode node ? node.Id : null,
            ["parentNodeId"] = item?.Tag is StructureNode data ? data.ParentId : null,
            ["treeParentNodeId"] = item is null ? null : ParentNodeId(item),
        };
    }

    private static TreeViewItem? SourceItem(TreeView tree, object? source) => source is Visual visual
        && (ReferenceEquals(visual, tree) || tree.IsVisualAncestorOf(visual))
        ? visual.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault() : null;

    private static bool InHeader(TreeViewItem? item, object? source) => item?.Header is Visual header && source is Visual hit
        && (ReferenceEquals(header, hit) || header.IsVisualAncestorOf(hit));

    private static long? ParentNodeId(TreeViewItem item) => item.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault()?.Tag is StructureNode node
        ? node.Id : null;

    private JsonObject InputJson(string kind, object? eventSource, long observed, PixelPoint? point = null)
    {
        var record = IdentityJson();
        var current = source?.Invoke();
        record["sourceDocumentId"] = current?.DocumentId.ToString(); record["sourceRevision"] = current?.Revision;
        record["kind"] = kind; record["eventId"] = ++inputSequence; record["observedQpc"] = observed;
        record["inspectionNonce"] = lastInspectionNonce;
        record["x"] = point?.X; record["y"] = point?.Y;
        foreach (var pair in ActualSource(view.Tree, eventSource)) record[pair.Key] = pair.Value?.DeepClone();
        return record;
    }

    internal static JsonObject? InspectHeader(Window window, TreeView tree, TreeViewItem target)
    {
        if (target.Tag is not StructureNode || target.Header is not Visual header || !tree.IsVisualAncestorOf(header)) return null;
        var clipped = ClippedPixels(window, header);
        if (clipped is not { } bounds || bounds.Width <= 0 || bounds.Height <= 0) return null;
        var hit = SafePoint(window, tree, bounds, target);
        return new() { ["headerRect"] = RectJson(bounds), ["headerPoint"] = hit };
    }

    private static JsonObject? SafePoint(Window window, TreeView tree, PixelRect? area, TreeViewItem? target = null)
    {
        if (area is not { Width: > 0, Height: > 0 } rectangle) return null;
        foreach (var point in PointsInside(rectangle))
        {
            var hit = window.InputHitTest(window.PointToClient(point));
            var actual = ActualSource(tree, hit);
            if (actual["inTree"]?.GetValue<bool>() != true
                || target is not null && (!ReferenceEquals(SourceItem(tree, hit), target) || !InHeader(target, hit))) continue;
            actual["x"] = point.X; actual["y"] = point.Y;
            if (target is not null) actual["headerMatches"] = true;
            return actual;
        }
        return null;
    }

    private static IEnumerable<PixelPoint> PointsInside(PixelRect area)
    {
        foreach (double y in new[] { 0.5, 0.25, 0.75 })
            foreach (double x in new[] { 0.5, 0.25, 0.75 })
                yield return new(area.X + Math.Min(area.Width - 1, (int)(area.Width * x)),
                    area.Y + Math.Min(area.Height - 1, (int)(area.Height * y)));
    }

    private static PixelRect? TreeViewport(Window window, TreeView tree)
    {
        var presenter = tree.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        return ClippedPixels(window, presenter ?? (Visual)tree);
    }

    private static PixelRect? ClippedPixels(Window window, Visual visual)
    {
        if (!visual.IsEffectivelyVisible || visual.TransformToVisual(window) is not { } transform) return null;
        var bounds = new Rect(visual.Bounds.Size).TransformToAABB(transform);
        for (Visual? ancestor = visual; ancestor is not null; ancestor = ancestor.GetVisualParent())
        {
            if (ancestor.TransformToVisual(window) is not { } matrix) return null;
            if (ancestor.ClipToBounds) bounds = bounds.Intersect(new Rect(ancestor.Bounds.Size).TransformToAABB(matrix));
            if (ancestor.Clip is { } clip) bounds = bounds.Intersect(clip.Bounds.TransformToAABB(matrix));
            if (ReferenceEquals(ancestor, window)) break;
        }
        bounds = bounds.Intersect(new Rect(window.ClientSize));
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        PixelPoint origin = window.PointToScreen(default);
        double scale = window.RenderScaling;
        int left = origin.X + (int)Math.Ceiling(bounds.Left * scale), top = origin.Y + (int)Math.Ceiling(bounds.Top * scale);
        int right = origin.X + (int)Math.Floor(bounds.Right * scale), bottom = origin.Y + (int)Math.Floor(bounds.Bottom * scale);
        return right > left && bottom > top ? new PixelRect(left, top, right - left, bottom - top) : null;
    }

    private static JsonObject? RectJson(PixelRect? rectangle) => rectangle is { } bounds ? new()
    {
        ["x"] = bounds.X, ["y"] = bounds.Y, ["width"] = bounds.Width, ["height"] = bounds.Height,
    } : null;
}
