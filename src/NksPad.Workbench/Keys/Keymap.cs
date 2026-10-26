using NksPad.Workbench.Commands;
using NksPad.Workbench.Contributions;

namespace NksPad.Workbench.Keys;

/// <summary>
/// Layer kinds in increasing priority. A plugin binding never overrides a default, profile or user binding;
/// a user binding overrides everything.
/// </summary>
public enum KeymapLayerKind
{
    Plugin,
    Default,
    Profile,
    User,
}

/// <summary>A named set of keybinding data. <see cref="Source"/> says where it came from, e.g. "nks.core" or "user".</summary>
public sealed record KeymapLayer(KeymapLayerKind Kind, string Source, IReadOnlyList<KeybindingContribution> Bindings);

/// <summary>A keybinding after resolution for one platform.</summary>
public sealed class KeyBinding
{
    internal KeyBinding(KeySequence sequence, string commandId, bool isUnbind, WhenExpression? when, string? whenText, KeymapLayerKind layer, string source, int order)
    {
        Sequence = sequence;
        CommandId = commandId;
        IsUnbind = isUnbind;
        When = when;
        WhenText = whenText;
        Layer = layer;
        Source = source;
        Order = order;
    }

    public KeySequence Sequence { get; }

    public string CommandId { get; }

    public bool IsUnbind { get; }

    public WhenExpression? When { get; }

    public string? WhenText { get; }

    public KeymapLayerKind Layer { get; }

    /// <summary>Where the binding was defined.</summary>
    public string Source { get; }

    internal int Order { get; }

    public bool IsActive(IWhenContext context) => When?.Evaluate(context) ?? true;

    public override string ToString() => $"{Sequence} -> {CommandId}" + (WhenText is null ? "" : $" [{WhenText}]") + $" ({Layer}: {Source})";
}

public enum KeyConflictKind
{
    /// <summary>Two bindings of one layer on the same keys can be active together: the later one wins, which is ambiguous.</summary>
    SameLayer,

    /// <summary>A binding of a higher layer shadows one of a lower layer. Intended, shown for information.</summary>
    Overridden,

    /// <summary>The winner's keys are the start of the loser's chord sequence, so the loser can never complete.</summary>
    Prefix,
}

public sealed record KeyConflict(KeyConflictKind Kind, KeyBinding Winner, KeyBinding Loser)
{
    /// <summary>True for conflicts the user should resolve (everything except an intended override).</summary>
    public bool IsAmbiguous => Kind != KeyConflictKind.Overridden;
}

public enum KeyResolutionKind { NoMatch, Pending, Command }

public readonly record struct KeyResolution(KeyResolutionKind Kind, KeyBinding? Binding);

/// <summary>
/// The effective keymap of one platform, built from layers. Bindings are data; resolution depends on the
/// <c>when</c> context. Invalid entries are skipped and listed in <see cref="Errors"/>.
/// </summary>
public sealed class Keymap
{
    private readonly List<KeyBinding> _effective;
    private IReadOnlyList<KeyConflict>? _conflicts;

    public Keymap(Platform platform, IEnumerable<KeymapLayer> layers)
    {
        Platform = platform;
        var errors = new List<string>();
        var all = new List<KeyBinding>();
        var order = 0;
        foreach (var layer in layers.OrderBy(l => l.Kind))
        {
            foreach (var b in layer.Bindings)
            {
                var text = platform == Platform.MacOS && b.Mac is not null ? b.Mac : b.Key;
                if (!KeySequence.TryParse(text, platform, out var sequence, out var error))
                {
                    errors.Add($"{layer.Source}: {error}");
                    continue;
                }

                WhenExpression? when = null;
                if (b.When is not null && !WhenExpression.TryParse(b.When, out when, out error))
                {
                    errors.Add($"{layer.Source}: when '{b.When}': {error}");
                    continue;
                }

                var unbind = b.Command.StartsWith('-');
                all.Add(new KeyBinding(sequence, unbind ? b.Command[1..] : b.Command, unbind, when, b.When, layer.Kind, layer.Source, order++));
            }
        }

        _effective = all.Where(b => !b.IsUnbind
            && !all.Any(u => u.IsUnbind && u.Order > b.Order && u.Sequence.Equals(b.Sequence) && u.CommandId == b.CommandId)).ToList();
        Errors = errors;
    }

    public Platform Platform { get; }

    public IReadOnlyList<string> Errors { get; }

    /// <summary>All bindings that remain after unbinding, lowest layer first.</summary>
    public IReadOnlyList<KeyBinding> Bindings => _effective;

    /// <summary>Shortcut texts of a command: higher layers first, then in definition order (the first one is the primary shortcut).</summary>
    public IReadOnlyList<string> ShortcutsFor(string commandId) => _effective
        .Where(b => b.CommandId == commandId)
        .OrderByDescending(b => b.Layer).ThenBy(b => b.Order)
        .Select(b => b.Sequence.Display(Platform))
        .Distinct()
        .ToList();

    /// <summary>
    /// Looks up the chords pressed so far. An exact match wins over longer sequences that start the same;
    /// among exact matches the highest layer wins, then the later definition.
    /// </summary>
    public KeyResolution Resolve(IReadOnlyList<KeyChord> pressed, IWhenContext context)
    {
        KeyBinding? best = null;
        var longer = false;
        foreach (var b in _effective)
        {
            if (!b.Sequence.StartsWith(pressed) || !b.IsActive(context))
            {
                continue;
            }

            if (b.Sequence.Chords.Count == pressed.Count)
            {
                if (best is null || b.Layer > best.Layer || (b.Layer == best.Layer && b.Order > best.Order))
                {
                    best = b;
                }
            }
            else
            {
                longer = true;
            }
        }

        if (best is not null)
        {
            return new KeyResolution(KeyResolutionKind.Command, best);
        }

        return new KeyResolution(longer ? KeyResolutionKind.Pending : KeyResolutionKind.NoMatch, null);
    }

    /// <summary>Conflicts between bindings that can be active at the same time, each with the source of both sides.</summary>
    public IReadOnlyList<KeyConflict> Conflicts => _conflicts ??= FindConflicts();

    private List<KeyConflict> FindConflicts()
    {
        var result = new List<KeyConflict>();
        foreach (var group in _effective.GroupBy(b => b.Sequence.Chords[0]))
        {
            var items = group.ToList();
            for (var i = 0; i < items.Count; i++)
            {
                for (var j = i + 1; j < items.Count; j++)
                {
                    var a = items[i];
                    var b = items[j];
                    if (a.CommandId == b.CommandId || !WhenExpression.CanBothBeTrue(a.When, b.When))
                    {
                        continue;
                    }

                    if (a.Sequence.Equals(b.Sequence))
                    {
                        var (low, high) = a.Layer == b.Layer || a.Layer < b.Layer ? (a, b) : (b, a);
                        result.Add(new KeyConflict(a.Layer == b.Layer ? KeyConflictKind.SameLayer : KeyConflictKind.Overridden, high, low));
                    }
                    else if (a.Sequence.IsStrictPrefixOf(b.Sequence))
                    {
                        result.Add(new KeyConflict(KeyConflictKind.Prefix, a, b));
                    }
                    else if (b.Sequence.IsStrictPrefixOf(a.Sequence))
                    {
                        result.Add(new KeyConflict(KeyConflictKind.Prefix, b, a));
                    }
                }
            }
        }

        return result;
    }

    public IReadOnlyList<KeyConflict> ConflictsOf(KeyBinding binding) =>
        Conflicts.Where(c => ReferenceEquals(c.Winner, binding) || ReferenceEquals(c.Loser, binding)).ToList();
}

public enum KeyDispatchKind
{
    /// <summary>No binding: the UI handles the key normally (typing, caret movement).</summary>
    Unbound,

    /// <summary>First chord of a sequence accepted, waiting for the next.</summary>
    Pending,

    /// <summary>The sequence was broken; the keys are swallowed.</summary>
    Cancelled,

    Executed,

    /// <summary>The command and its fallback declined: the UI handles the key normally.</summary>
    NotHandled,

    /// <summary>The command exists but is disabled in this context; the key is swallowed.</summary>
    Disabled,
}

public readonly record struct KeyDispatchResult(KeyDispatchKind Kind, string? CommandId);

/// <summary>Feeds key presses into a keymap and runs the resolved command through the registry.</summary>
public sealed class KeyDispatcher(Keymap keymap, CommandRegistry registry)
{
    private readonly List<KeyChord> _pending = [];

    public async ValueTask<KeyDispatchResult> HandleAsync(KeyChord chord, IWhenContext context, CancellationToken cancellationToken = default)
    {
        _pending.Add(chord);
        var resolution = keymap.Resolve(_pending, context);
        switch (resolution.Kind)
        {
            case KeyResolutionKind.Pending:
                return new KeyDispatchResult(KeyDispatchKind.Pending, null);
            case KeyResolutionKind.NoMatch:
                var wasChord = _pending.Count > 1;
                _pending.Clear();
                return new KeyDispatchResult(wasChord ? KeyDispatchKind.Cancelled : KeyDispatchKind.Unbound, null);
        }

        _pending.Clear();
        var id = resolution.Binding!.CommandId;
        var outcome = await registry.ExecuteAsync(id, context, cancellationToken).ConfigureAwait(false);
        var kind = outcome switch
        {
            CommandOutcome.Handled => KeyDispatchKind.Executed,
            CommandOutcome.Disabled => KeyDispatchKind.Disabled,
            _ => KeyDispatchKind.NotHandled,
        };
        return new KeyDispatchResult(kind, id);
    }
}
