namespace NksHub.Textida.Core.Syntax;

/// <summary>
/// Tokenizer state at a line start: the stack of open regions (block comment, multi-line string, embedded language).
/// States are interned per grammar, so two equal states are the same object and convergence is a reference comparison.
/// </summary>
public sealed class LineState
{
    /// <summary>Open regions deeper than this are ignored, so a pathological file cannot grow the stack without bound.</summary>
    internal const int MaxDepth = 64;

    internal LineState(ContextDef context, LineState? parent)
    {
        Context = context;
        Parent = parent;
        Depth = parent is null ? 0 : parent.Depth + 1;
    }

    internal ContextDef Context { get; }
    internal LineState? Parent { get; }
    internal int Depth { get; }

    /// <summary>True outside every region.</summary>
    public bool IsInitial => Parent is null && Context.IsRoot;

    /// <summary>Names of the open regions from the outermost, joined by <c>&gt;</c>; empty at the top level.</summary>
    public string Description
    {
        get
        {
            var names = new List<string>();
            for (var s = this; s is not null; s = s.Parent)
                if (!s.Context.IsRoot) names.Add(s.Context.Name);
            names.Reverse();
            return string.Join('>', names);
        }
    }

    public override string ToString() => Description;
}
