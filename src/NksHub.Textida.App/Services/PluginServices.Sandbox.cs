using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost;

namespace NksHub.Textida.App.Services;

/// <summary>The operating system limit plugin processes are started under (PZ.10.1). Off until the setting <c>plugins.sandbox</c> is on; see <see cref="ProcessSandbox"/> for what each level does.</summary>
internal sealed partial class PluginServices
{
    internal const string SandboxSetting = "plugins.sandbox";

    /// <summary>The environment variable that sets the level for every plugin (<c>off</c>, <c>contained</c>, <c>strict</c>); for measurements, it wins over the setting.</summary>
    internal const string SandboxVariable = "TEXTIDA_PLUGIN_SANDBOX";

    /// <summary>
    /// The level for a plugin. Those that start programs (Git, language servers, formatters) or use the network themselves (JSON schemas and package lookups, the wiki) are contained, because a strict
    /// level would stop them; every other plugin, built in or installed, is strict.
    /// </summary>
    internal ProcessSandboxLevel SandboxOf(PluginSpec spec)
    {
        if (Environment.GetEnvironmentVariable(SandboxVariable) is { Length: > 0 } forced)
            return Enum.TryParse<ProcessSandboxLevel>(forced, ignoreCase: true, out var level) ? level : ProcessSandboxLevel.Off;
        if (!_app.Workbench.Settings.GetBool(SandboxSetting)) return ProcessSandboxLevel.Off;
        return spec.Id is GitPluginId or LspPluginId or FormattersPluginId or JsonPluginId or WikiPluginId ? ProcessSandboxLevel.Contained : ProcessSandboxLevel.Strict;
    }
}
