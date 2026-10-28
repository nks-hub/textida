using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Agents.Hooks;

/// <summary>Limits the caller decides; there are no defaults.</summary>
public sealed record HookLimits(int MaxDepth, TimeSpan RegexTimeout);

/// <summary>One handler of a matcher group. <see cref="Index"/> is its place in the <c>hooks</c> array of the group.</summary>
public sealed class HookHandlerNode(DataNode node, int index)
{
    public DataNode Node { get; } = node;
    public int Index { get; } = index;

    public string Type => Node.Get("type") is { Kind: DataKind.String } t ? t.Text : "";

    /// <summary>The text of a string member, or null when it is missing or not a string.</summary>
    public string? Text(string field) => Node.Get(field) is { Kind: DataKind.String } n ? n.Text : null;

    /// <summary>The number or boolean member as written, or null.</summary>
    public string? Raw(string field, string text) => Node.Get(field) is { Kind: DataKind.Number or DataKind.Boolean } n ? text[n.Start..n.End] : null;

    /// <summary>What the handler runs, for a list: the command, the URL, the tool or the prompt.</summary>
    public string Summary
    {
        get
        {
            string? main = Type switch
            {
                "command" => Text("command"),
                "http" => Text("url"),
                "mcp_tool" => Text("server") is { } s ? s + " / " + Text("tool") : null,
                "prompt" or "agent" => Text("prompt"),
                _ => null,
            };
            main = main is null ? "" : main.ReplaceLineEndings(" ");
            return Type.Length == 0 ? main : main.Length == 0 ? Type : Type + ": " + (main.Length > 80 ? main[..80] + "…" : main);
        }
    }
}

public sealed class HookGroupNode(DataNode node, int index, IReadOnlyList<HookHandlerNode> handlers)
{
    public DataNode Node { get; } = node;
    public int Index { get; } = index;
    public IReadOnlyList<HookHandlerNode> Handlers { get; } = handlers;

    /// <summary>The matcher; empty when the group has none.</summary>
    public string Matcher => Node.Get("matcher") is { Kind: DataKind.String } m ? m.Text : "";

    public DataNode? HooksArray => Node.Get("hooks") is { Kind: DataKind.Array } a ? a : null;
}

public sealed class HookEventNode(DataMember member, HookEventInfo? info, IReadOnlyList<HookGroupNode> groups)
{
    public DataMember Member { get; } = member;
    public string Name => Member.Name;

    /// <summary>Null when the catalogue of the product does not have an event of this name.</summary>
    public HookEventInfo? Info { get; } = info;

    public IReadOnlyList<HookGroupNode> Groups { get; } = groups;
}

/// <summary>A hooks file read as strict JSON: the tree event, matcher group, handler, each with its place in the text. What is not shaped like the documentation is left out of the tree and reported by <see cref="HookValidator"/>.</summary>
public sealed class HookDocument
{
    private HookDocument(string text, HookProduct product, DataNode root, DataNode? hooks, IReadOnlyList<HookEventNode> events)
    {
        Text = text;
        Product = product;
        Root = root;
        Hooks = hooks;
        Events = events;
    }

    public string Text { get; }
    public HookProduct Product { get; }
    public DataNode Root { get; }

    /// <summary>The <c>hooks</c> object, or null when the file has none (or it is not an object).</summary>
    public DataNode? Hooks { get; }

    public IReadOnlyList<HookEventNode> Events { get; }

    public static bool TryParse(string text, HookProduct product, HookLimits limits, out HookDocument? document, out DataSyntaxException? error)
    {
        document = null;
        if (!JsonDataReader.TryParse(text, jsonc: false, limits.MaxDepth, out var root, out error)) return false;
        var catalog = HookCatalog.For(product);
        DataNode? hooks = root!.Kind == DataKind.Object && root.Get("hooks") is { Kind: DataKind.Object } h ? h : null;
        var events = new List<HookEventNode>();
        foreach (var member in hooks?.Members ?? [])
        {
            var groups = new List<HookGroupNode>();
            if (member.Value.Kind == DataKind.Array)
            {
                for (int g = 0; g < member.Value.Items.Count; g++)
                {
                    var group = member.Value.Items[g];
                    if (group.Kind != DataKind.Object) continue;
                    var handlers = new List<HookHandlerNode>();
                    if (group.Get("hooks") is { Kind: DataKind.Array } array)
                        for (int i = 0; i < array.Items.Count; i++)
                            if (array.Items[i].Kind == DataKind.Object) handlers.Add(new HookHandlerNode(array.Items[i], i));
                    groups.Add(new HookGroupNode(group, g, handlers));
                }
            }
            events.Add(new HookEventNode(member, catalog.Find(member.Name), groups));
        }
        document = new HookDocument(text, product, root, hooks, events);
        return true;
    }

    public (HookEventNode Event, HookGroupNode Group, HookHandlerNode Handler)? Find(HookRef at)
    {
        foreach (var e in Events)
        {
            if (e.Name != at.Event) continue;
            foreach (var g in e.Groups)
            {
                if (g.Index != at.Group) continue;
                foreach (var h in g.Handlers) if (h.Index == at.Handler) return (e, g, h);
            }
        }
        return null;
    }
}

/// <summary>The place of a handler: the event, then the number of the matcher group in the array of the event and the number of the handler in the array of the group.</summary>
public sealed record HookRef(string Event, int Group, int Handler);
