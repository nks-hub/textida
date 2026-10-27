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

    public bool Hidden { get; }

    public IReadOnlyList<string> Aliases { get; }

    /// <summary>Manifest id the command came from.</summary>
    public string Source { get; }

    public bool IsAvailable(IWhenContext context) => When?.Evaluate(context) ?? true;

    public bool IsEnabled(IWhenContext context) => IsAvailable(context) && (Enablement?.Evaluate(context) ?? true);
}

/// <summary>A resolved menu entry.</summary>
public sealed record MenuItemModel(string CommandId, string TitleKey, string Group, int Order, bool Enabled);

/// <summary>What a handler reports. A handler that declines returns <see cref="NotHandled"/>.</summary>
public enum CommandOutcome
{
    Handled,
    NotHandled,
    Disabled,
    Unknown,
    NoHandler,
}

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
                outcome = await handler(context, cancellationToken).ConfigureAwait(false);
            }

            if (outcome == CommandOutcome.Handled && command.Repeatable)
            {
                LastRepeatable = command.Id;
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

            items.Add(new MenuItemModel(command.Id, command.TitleKey, item.Group, item.Order, command.IsEnabled(context)));
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
