using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Permissions;

public enum ScopeKind { All, Document, Plugin, Any }

/// <summary>
/// What a grant covers: everything, one document (by its identity, never "the active tab") or one plugin (the target of a command).
/// <see cref="Any"/> is only used to ask "does the plugin hold the permission for at least one scope".
/// </summary>
public readonly record struct GrantScope(ScopeKind Kind, string? Id = null)
{
    public static GrantScope All => new(ScopeKind.All);
    public static GrantScope Any => new(ScopeKind.Any);
    public static GrantScope OfDocument(Guid id) => new(ScopeKind.Document, id.ToString("N"));
    public static GrantScope OfPlugin(string pluginId) => new(ScopeKind.Plugin, pluginId);

    /// <summary>Whether a grant of this scope allows an action on <paramref name="target"/>.</summary>
    public bool Covers(GrantScope target) => Kind == ScopeKind.All || target.Kind == ScopeKind.Any || this == target;
}

public sealed record PermissionGrant(string PluginId, string Permission, GrantScope Scope);

/// <summary>Permissions outside the document API.</summary>
public static class PluginPermissionNames
{
    /// <summary>Run a command of another plugin. The scope is the target plugin.</summary>
    public const string CommandExecute = "command.execute";

    /// <summary>
    /// What a manifest may ask for; the user grants each one to the plugin in the plugin manager, for all documents. <see cref="DocumentPermissions.SecretsReveal"/> (one document)
    /// and <see cref="CommandExecute"/> (one target plugin) have a narrower scope and are granted when needed, not declared.
    /// </summary>
    public static IReadOnlyList<string> Declarable { get; } =
    [
        DocumentPermissions.Read, DocumentPermissions.EditPropose, DocumentPermissions.EditDirect,
        DocumentPermissions.ClipboardWrite, DocumentPermissions.Virtual, DocumentPermissions.WorkspaceFiles,
    ];
}

/// <summary>
/// Grants per plugin, permission and scope. A request enters through <see cref="Enter"/>: no grant is a <see cref="DocumentErrorCodes.PermissionDenied"/> error,
/// never an empty result. Revoking a grant cancels every request in flight that the grant allowed and no other grant still allows.
/// Permissions are a consent record, not a sandbox: the plugin process runs with the rights of the user.
/// </summary>
public sealed class PluginPermissions
{
    private readonly object _gate = new();
    private readonly HashSet<PermissionGrant> _grants = [];
    private readonly HashSet<PermissionLease> _leases = [];
    private readonly Dictionary<string, int> _delegated = [];

    /// <summary>A grant was removed; the argument is the plugin id.</summary>
    public event Action<string>? Revoked;

    public void Grant(string pluginId, string permission, GrantScope scope)
    {
        if (scope.Kind == ScopeKind.Any) throw new ArgumentException("A grant cannot have the scope Any.", nameof(scope));
        lock (_gate) _grants.Add(new PermissionGrant(pluginId, permission, scope));
    }

    public bool Revoke(string pluginId, string permission, GrantScope scope) =>
        RevokeWhere(g => g.PluginId == pluginId && g.Permission == permission && g.Scope == scope, pluginId);

    public bool RevokeAll(string pluginId) => RevokeWhere(g => g.PluginId == pluginId, pluginId);

    public IReadOnlyList<PermissionGrant> GrantsOf(string pluginId)
    {
        lock (_gate) return [.. _grants.Where(g => g.PluginId == pluginId)];
    }

    public bool IsGranted(string pluginId, string permission, GrantScope target)
    {
        lock (_gate) return Holds(pluginId, permission, target);
    }

    private bool Holds(string pluginId, string permission, GrantScope target) =>
        _grants.Any(g => g.PluginId == pluginId && g.Permission == permission && g.Scope.Covers(target));

    private bool RevokeWhere(Predicate<PermissionGrant> match, string pluginId)
    {
        List<PermissionLease> cancel;
        lock (_gate)
        {
            if (_grants.RemoveWhere(match) == 0) return false;
            cancel = [.. _leases.Where(l => l.PluginId == pluginId && !Holds(l.PluginId, l.Permission, l.Target))];
            foreach (var l in cancel) l.Revoked = true;
        }
        foreach (var l in cancel) l.Cancel();
        Revoked?.Invoke(pluginId);
        return true;
    }

    /// <summary>
    /// Checks the grant and registers the request so a revoke can cancel it. Dispose the lease when the request ends.
    /// </summary>
    /// <exception cref="RpcException">Code <see cref="DocumentErrorCodes.PermissionDenied"/>.</exception>
    public PermissionLease Enter(string pluginId, string permission, GrantScope target, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (!Holds(pluginId, permission, target)) throw Denied(permission, "NotGranted", $"The plugin does not hold the '{permission}' permission for this target.");
            var lease = new PermissionLease(this, pluginId, permission, target, cancellation);
            _leases.Add(lease);
            return lease;
        }
    }

    internal void Release(PermissionLease lease)
    {
        lock (_gate) _leases.Remove(lease);
    }

    internal static RpcException Denied(string permission, string reason, string message) =>
        new(DocumentErrorCodes.PermissionDenied, message) { DataJson = $"{{\"permission\":\"{permission}\",\"reason\":\"{reason}\"}}" };

    /// <summary>
    /// Marks the plugin as running a command another plugin asked for. While it runs, the plugin may not write documents directly:
    /// a permitted command cannot pass a write on. It fails closed, so a user action in the same plugin that overlaps in time waits for a retry.
    /// </summary>
    public IDisposable BeginDelegation(string pluginId)
    {
        lock (_gate) _delegated[pluginId] = _delegated.GetValueOrDefault(pluginId) + 1;
        return new Delegation(this, pluginId);
    }

    public bool IsDelegated(string pluginId)
    {
        lock (_gate) return _delegated.GetValueOrDefault(pluginId) > 0;
    }

    private sealed class Delegation(PluginPermissions owner, string pluginId) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 1) return;
            lock (owner._gate) owner._delegated[pluginId]--;
        }
    }
}

/// <summary>One request that a grant allowed. <see cref="Token"/> is cancelled when the grant is revoked.</summary>
public sealed class PermissionLease : IDisposable
{
    private readonly PluginPermissions _owner;
    private readonly CancellationTokenSource _cts;

    internal PermissionLease(PluginPermissions owner, string pluginId, string permission, GrantScope target, CancellationToken cancellation)
    {
        _owner = owner;
        PluginId = pluginId;
        Permission = permission;
        Target = target;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
    }

    public string PluginId { get; }
    public string Permission { get; }
    public GrantScope Target { get; }
    public CancellationToken Token => _cts.Token;
    public volatile bool Revoked;

    internal void Cancel()
    {
        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    /// <summary>The error a request gets when its grant was revoked while it ran.</summary>
    public RpcException RevokedError() => PluginPermissions.Denied(Permission, "Revoked", $"The '{Permission}' permission was revoked while the request ran.");

    public void Dispose()
    {
        _owner.Release(this);
        _cts.Dispose();
    }
}
