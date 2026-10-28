namespace NksHub.Textida.PluginSdk;

/// <summary>The plugin API this version of the SDK implements.</summary>
public static class PluginApi
{
    /// <summary>
    /// The plugin API version: the number the editor offers in <c>initialize</c> and a plugin names in its answer (<see cref="PluginBase"/> takes it as <c>apiVersion</c>).
    /// A plugin states the range of versions it works with in <c>api.min</c> and <c>api.max</c> of its manifest; the editor starts it only when its own version is inside the range.
    /// </summary>
    public const int Version = 1;
}
