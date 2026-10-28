namespace NksHub.Textida.Core.Agents.Hooks;

/// <summary>Which product reads a hooks file, by where the file lives.</summary>
public static class HookFiles
{
    /// <summary>Codex reads <c>hooks.json</c> from <c>.codex</c> folders; every other file is taken as a Claude Code settings file or a plugin <c>hooks.json</c>.</summary>
    public static HookProduct ProductOf(string? path) =>
        path is not null && path.Replace('\\', '/').Split('/').Any(part => part.Equals(".codex", StringComparison.OrdinalIgnoreCase)) ? HookProduct.Codex : HookProduct.ClaudeCode;

    /// <summary>Codex also keeps hooks in <c>[hooks]</c> tables of <c>config.toml</c>; the editor changes JSON files only.</summary>
    public static bool IsToml(string? path) => path is not null && path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase);
}
