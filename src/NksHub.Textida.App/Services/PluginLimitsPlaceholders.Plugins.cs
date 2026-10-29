using NksHub.Textida.PluginHost.Manager;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The limits of a plugin folder. They are in a file of their own because the plugin validator program (<c>textida-plugin-validate</c>) compiles it as well: the editor, when it installs a plugin,
/// and the validator, when an author checks one, read the same numbers. EVERY NUMBER HERE IS A PLACEHOLDER waiting for the owner's decision, as in the rest of the class.
/// </summary>
internal static partial class PluginLimitsPlaceholders
{
    /// <summary>Placeholder, OPEN: largest grammar file of a data plugin in bytes. The manifest loader refuses a plugin with a larger one, whoever installs it.</summary>
    public const long GrammarMaxBytes = 1 << 20;

    /// <summary>Placeholder, OPEN: largest icon file (SVG) of a plugin in bytes. The manifest loader refuses a plugin with a larger one, whoever installs it.</summary>
    public const long IconMaxBytes = 16 << 10;

    /// <summary>Placeholder: largest size a plugin ZIP may unpack to, in bytes; the validator holds a plugin folder to the same bound. OPEN: the owner has not decided it.</summary>
    public const long PluginZipMaxUnpackedBytes = 256L << 20;

    /// <summary>Placeholder: most entries a plugin ZIP may hold; the validator holds a plugin folder to the same bound. OPEN: the owner has not decided it.</summary>
    public const int PluginZipMaxEntries = 2000;

    /// <summary>Placeholder, OPEN: seconds the plugin validator waits for the handshake of a plugin it starts, and for each of the checks after it (an unknown method, the shutdown). The owner has not decided it.</summary>
    public const int ValidatorPluginSeconds = 30;

    /// <summary>The bounds an installed plugin ZIP must keep.</summary>
    public static PluginInstallLimits PluginZip() => new(PluginZipMaxUnpackedBytes, PluginZipMaxEntries, IconMaxBytes);
}
