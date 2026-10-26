namespace NksHub.Textida.PluginHost;

/// <summary>Joins the APIs behind the host's single <see cref="PluginApiCallback"/>: a method goes to the first API whose prefix it starts with.</summary>
public static class PluginApiRouter
{
    public static PluginApiCallback Create(PluginApiCallback fallback, params (string Prefix, PluginApiCallback Api)[] routes) =>
        (pluginId, method, parameters, cancellation) =>
        {
            foreach (var (prefix, api) in routes)
                if (method.StartsWith(prefix, StringComparison.Ordinal)) return api(pluginId, method, parameters, cancellation);
            return fallback(pluginId, method, parameters, cancellation);
        };
}
