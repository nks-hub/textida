using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.Workbench.Commands;

public enum UndoMode { None, Transaction }

/// <summary>A registered command: the parsed form of a <see cref="CommandContribution"/>.</summary>
public sealed class CommandDescriptor
{
    internal CommandDescriptor(CommandContribution c, string source)
    {
        Id = c.Id;
        TitleKey = c.Title;
        CategoryKey = c.Category;
        When = c.When is null ? null : WhenExpression.Parse(c.When);
        Enablement = c.Enablement is null ? null : WhenExpression.Parse(c.Enablement);
        FallbackId = c.Fallback;
        Undo = c.Undo == "transaction" ? UndoMode.Transaction : UndoMode.None;
        Cancellable = c.Cancellable;
        Repeatable = c.Repeatable;
        Macro = c.Macro;
        Hidden = c.Hidden;
        Aliases = c.Aliases.ToArray();
        Source = source;
    }

    public string Id { get; }

    public string TitleKey { get; }

    public string? CategoryKey { get; }

    public WhenExpression? When { get; }

    public WhenExpression? Enablement { get; }

    public string? FallbackId { get; }

    public UndoMode Undo { get; }

    public bool Cancellable { get; }

    public bool Repeatable { get; }

    /// <summary>A macro may record and replay the command.</summary>
    public bool Macro { get; }

    public bool Hidden { get; }

    public IReadOnlyList<string> Aliases { get; }

    /// <summary>Manifest id the command came from.</summary>
    public string Source { get; }

    public bool IsAvailable(IWhenContext context) => When?.Evaluate(context) ?? true;

    public bool IsEnabled(IWhenContext context) => IsAvailable(context) && (Enablement?.Evaluate(context) ?? true);
}

/// <summary>A resolved menu entry.</summary>
public sealed record MenuItemModel(string CommandId, string TitleKey, string Group, int Order, bool Enabled, string? Submenu = null);

/// <summary>What a handler reports. A handler that declines returns <see cref="NotHandled"/>.</summary>
public enum CommandOutcome
{
    Handled,
    NotHandled,
    Disabled,
    Unknown,
    NoHandler,

    /// <summary>A macro is being recorded and the command is not one a macro can replay; it did not run.</summary>
    NotRecordable,
}

/// <summary>
/// One step of a macro: a command of the registry, or text typed at the carets. Exactly one of the two is set.
/// <see cref="Argument"/> is what the command recorded with itself (<see cref="CommandRegistry.RecordArgument"/>), such as the search of Find Next.
/// </summary>
public sealed record MacroStep(string? Command, string? Text, string? Argument = null);

/// <summary>How far <see cref="CommandRegistry.PlayAsync"/> got: <see cref="Done"/> steps ran; <see cref="Outcome"/> is <see cref="CommandOutcome.Handled"/> when all of them did.</summary>
public sealed record MacroResult(int Done, CommandOutcome Outcome);

public delegate ValueTask<CommandOutcome> CommandHandler(IWhenContext context, CancellationToken cancellationToken);

/// <summary>
/// The single source for palette, menus and keys. Commands, menu items and aliases come from manifests;
/// handlers are attached in code by the owner of the command.
/// </summary>
public sealed class CommandRegistry
{
    private const int MaxFallbackDepth = 4;

    private readonly Dictionary<string, CommandDescriptor> _commands = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CommandHandler> _handlers = new(StringComparer.Ordinal);
    private readonly List<(MenuContribution Item, string Source)> _menus = [];

    public IReadOnlyCollection<CommandDescriptor> Commands => _commands.Values;

    public IReadOnlyList<MenuContribution> MenuContributions => _menus.Select(m => m.Item).ToList();

    /// <summary>Finds a command by id or alias.</summary>
    public bool TryGet(string idOrAlias, out CommandDescriptor command)
    {
        if (_commands.TryGetValue(idOrAlias, out command!))
        {
            return true;
        }

        return _aliases.TryGetValue(idOrAlias, out var id) && _commands.TryGetValue(id, out command!);
    }

    /// <summary>
    /// Adds the commands, aliases and menu items of a manifest. Nothing is added when there is any conflict
    /// (duplicate id, alias clash); the returned list then describes every conflict.
    /// </summary>
    public IReadOnlyList<string> AddManifest(ContributionManifest manifest)
    {
        var errors = new List<string>();
        var newIds = new HashSet<string>(StringComparer.Ordinal);
        var newAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in manifest.Commands)
        {
            if (_commands.TryGetValue(c.Id, out var existing))
            {
                errors.Add($"Command '{c.Id}' from '{manifest.Id}' is already registered by '{existing.Source}'.");
            }

            newIds.Add(c.Id);
        }

        foreach (var c in manifest.Commands)
        {
            foreach (var alias in c.Aliases)
            {
                if (_commands.ContainsKey(alias) || newIds.Contains(alias))
                {
                    errors.Add($"Alias '{alias}' of '{c.Id}' collides with a command id.");
                }
                else if (_aliases.TryGetValue(alias, out var other) || newAliases.TryGetValue(alias, out other))
                {
                    errors.Add($"Alias '{alias}' of '{c.Id}' is already used by '{other}'.");
                }
                else
                {
                    newAliases[alias] = c.Id;
                }
            }
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        foreach (var c in manifest.Commands)
        {
            _commands[c.Id] = new CommandDescriptor(c, manifest.Id);
        }

        foreach (var (alias, id) in newAliases)
        {
            _aliases[alias] = id;
        }

        foreach (var menu in manifest.Menus)
        {
            _menus.Add((menu, manifest.Id));
        }

        return errors;
    }

    /// <summary>
    /// Takes back what <see cref="AddManifest"/> added for <paramref name="source"/> (a plugin that was removed, switched off or replaced by another version): its commands with their
    /// handlers and aliases, and its menu items. Returns how many commands went.
    /// </summary>
    public int RemoveSource(string source)
    {
        var ids = _commands.Values.Where(c => c.Source == source).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            _commands.Remove(id);
            _handlers.Remove(id);
        }

        foreach (var alias in _aliases.Where(a => ids.Contains(a.Value)).Select(a => a.Key).ToList())
        {
            _aliases.Remove(alias);
        }

        _menus.RemoveAll(m => m.Source == source);
        return ids.Count;
    }

    /// <summary>Attaches the handler of a command. A command has at most one handler.</summary>
    public void SetHandler(string commandId, CommandHandler handler)
    {
        if (!_commands.ContainsKey(commandId))
        {
            throw new ArgumentException($"Unknown command '{commandId}'.", nameof(commandId));
        }

        if (!_handlers.TryAdd(commandId, handler))
        {
            throw new InvalidOperationException($"Command '{commandId}' already has a handler.");
        }
    }

    public bool HasHandler(string commandId) => _handlers.ContainsKey(commandId);

    /// <summary>Prefix of the commands that control macros; they are never recorded and never refused.</summary>
    public const string MacroCommandPrefix = "nks.macro.";

    private List<MacroStep>? _recording;
    private string? _argument;
    private int _depth;
    private static readonly AsyncLocal<string?> s_running = new();

    /// <summary>Id of the command whose handler started the current flow of work, also after its awaits; null outside a command. A dialog the command opens uses it to offer the help of that command.</summary>
    public static string? Running => s_running.Value;

    public bool IsRecording => _recording is not null;

    public bool IsPlaying { get; private set; }

    /// <summary>True while a command runs; a command started meanwhile runs inside it and is not recorded as a step of its own.</summary>
    public bool IsExecuting => _depth > 0;

    /// <summary>While a macro plays a command step, the argument recorded with it; the command uses it instead of asking (Find Next takes its search from here).</summary>
    public string? StepArgument { get; private set; }

    /// <summary>Called by a running command while a macro is recorded: the step it is recorded as keeps <paramref name="argument"/>.</summary>
    public void RecordArgument(string argument)
    {
        if (_recording is not null && !IsPlaying) _argument = argument;
    }

    /// <summary>The last macro that was recorded and not empty.</summary>
    public IReadOnlyList<MacroStep> LastMacro { get; private set; } = [];

    /// <summary>Raised with the command id when a command is refused because it is not recorded in a macro.</summary>
    public event Action<string>? RecordingRefused;

    /// <summary>Starts recording the commands that run from now on; false while a macro is already recorded or played.</summary>
    public bool StartRecording()
    {
        if (_recording is not null || IsPlaying) return false;
        _recording = [];
        return true;
    }

    /// <summary>Ends the recording; the macro becomes <see cref="LastMacro"/> unless it is empty.</summary>
    public IReadOnlyList<MacroStep> StopRecording()
    {
        var steps = _recording ?? [];
        _recording = null;
        if (steps.Count > 0) LastMacro = steps;
        return steps;
    }

    /// <summary>Records text typed at the carets (typed or composed, not pasted); consecutive text joins into one step.</summary>
    public void RecordText(string text)
    {
        if (_recording is not { } steps || IsPlaying || text.Length == 0) return;
        if (steps.Count > 0 && steps[^1].Text is { } before) steps[^1] = new MacroStep(null, before + text);
        else steps.Add(new MacroStep(null, text));
    }

    /// <summary>
    /// Runs the steps in order through <see cref="ExecuteAsync"/>, the text steps through <paramref name="typeText"/>, and stops at the first step that is not handled.
    /// The caller groups the call into one undo step.
    /// </summary>
    public async ValueTask<MacroResult> PlayAsync(IReadOnlyList<MacroStep> steps, IWhenContext context, Func<string, bool> typeText, CancellationToken cancellationToken = default)
    {
        if (_recording is not null || IsPlaying) return new MacroResult(0, CommandOutcome.NotHandled);
        IsPlaying = true;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                CommandOutcome outcome;
                if (step.Command is { } id)
                {
                    StepArgument = step.Argument;
                    try { outcome = await ExecuteAsync(id, context, cancellationToken).ConfigureAwait(true); }
                    finally { StepArgument = null; }
                }
                else outcome = typeText(step.Text ?? "") ? CommandOutcome.Handled : CommandOutcome.NotHandled;
                if (outcome != CommandOutcome.Handled) return new MacroResult(i, outcome);
            }
            return new MacroResult(steps.Count, CommandOutcome.Handled);
        }
        finally
        {
            IsPlaying = false;
        }
    }

    /// <summary>Id of the last repeatable command that ran and was handled; null before the first one.</summary>
    public string? LastRepeatable { get; private set; }

    /// <summary>True while <see cref="RepeatLastAsync"/> runs a command, so a command that asks the user for parameters can reuse its last ones instead.</summary>
    public bool IsRepeating { get; private set; }

    /// <summary>Runs <see cref="LastRepeatable"/> again; <see cref="CommandOutcome.NotHandled"/> when nothing was run yet.</summary>
    public async ValueTask<CommandOutcome> RepeatLastAsync(IWhenContext context, CancellationToken cancellationToken = default)
    {
        if (LastRepeatable is not { } id || IsRepeating)
        {
            return CommandOutcome.NotHandled;
        }

        IsRepeating = true;
        try
        {
            return await ExecuteAsync(id, context, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsRepeating = false;
        }
    }

    /// <summary>
    /// Runs a command. When it is unavailable, disabled, has no handler or reports <see cref="CommandOutcome.NotHandled"/>,
    /// its fallback command (if declared) runs instead.
    /// </summary>
    public async ValueTask<CommandOutcome> ExecuteAsync(string idOrAlias, IWhenContext context, CancellationToken cancellationToken = default)
    {
        bool outermost = _depth == 0;
        if (outermost && _recording is not null && TryGet(idOrAlias, out var asked) && !asked.Macro && !asked.Id.StartsWith(MacroCommandPrefix, StringComparison.Ordinal))
        {
            RecordingRefused?.Invoke(asked.Id);
            return CommandOutcome.NotRecordable;
        }

        if (outermost) _argument = null;
        _depth++;
        try
        {
            return await ExecuteCoreAsync(idOrAlias, context, outermost, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _depth--;
        }
    }

    private async ValueTask<CommandOutcome> ExecuteCoreAsync(string idOrAlias, IWhenContext context, bool outermost, CancellationToken cancellationToken)
    {
        var outcome = CommandOutcome.Unknown;
        var name = idOrAlias;
        for (var depth = 0; depth < MaxFallbackDepth; depth++)
        {
            if (!TryGet(name, out var command))
            {
                return depth == 0 ? CommandOutcome.Unknown : outcome;
            }

            if (!command.IsEnabled(context))
            {
                outcome = CommandOutcome.Disabled;
            }
            else if (!_handlers.TryGetValue(command.Id, out var handler))
            {
                outcome = CommandOutcome.NoHandler;
            }
            else
            {
                // back on the caller's thread: a macro step is recorded there in the order the UI thread sees it, next to the typed text
                s_running.Value = command.Id;
                outcome = await handler(context, cancellationToken).ConfigureAwait(true);
            }

            if (outcome == CommandOutcome.Handled && command.Repeatable)
            {
                LastRepeatable = command.Id;
            }

            // only the outermost call is recorded: what a command runs inside itself (a fallback) is part of it
            if (outcome == CommandOutcome.Handled && outermost && command.Macro && _recording is { } steps && !IsPlaying)
            {
                steps.Add(new MacroStep(command.Id, null, _argument));
                _argument = null;
            }

            if (outcome == CommandOutcome.Handled || command.FallbackId is null)
            {
                return outcome;
            }

            name = command.FallbackId;
        }

        return outcome;
    }

    /// <summary>Menu entries whose own and command conditions hold, ordered by group, order and command id.</summary>
    public IReadOnlyList<MenuItemModel> GetMenu(string menuId, IWhenContext context)
    {
        var items = new List<MenuItemModel>();
        foreach (var (item, _) in _menus)
        {
            if (item.Menu != menuId || !_commands.TryGetValue(item.Command, out var command))
            {
                continue;
            }

            if (!command.IsAvailable(context) || !(item.When is null || WhenExpression.Parse(item.When).Evaluate(context)))
            {
                continue;
            }

            items.Add(new MenuItemModel(command.Id, command.TitleKey, item.Group, item.Order, command.IsEnabled(context), item.Submenu));
        }

        return items.OrderBy(i => i.Group, StringComparer.Ordinal).ThenBy(i => i.Order).ThenBy(i => i.CommandId, StringComparer.Ordinal).ToList();
    }

    /// <summary>Command ids that appear in at least one menu.</summary>
    public IReadOnlySet<string> CommandsInMenus() => _menus.Select(m => m.Item.Command).ToHashSet(StringComparer.Ordinal);

    /// <summary>Cross-checks references: menu items and fallbacks must point to registered commands.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var (item, source) in _menus)
        {
            if (!_commands.ContainsKey(item.Command))
            {
                errors.Add($"Menu '{item.Menu}' of '{source}' refers to unknown command '{item.Command}'.");
            }
        }

        foreach (var c in _commands.Values)
        {
            if (c.FallbackId is not null && !_commands.ContainsKey(c.FallbackId))
            {
                errors.Add($"Command '{c.Id}' has unknown fallback '{c.FallbackId}'.");
            }
        }

        return errors;
    }
}
