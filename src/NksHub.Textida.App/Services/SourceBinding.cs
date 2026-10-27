using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Documents;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Where the text of a tab comes from (P6.2.1) and what a save is conditional on (P6.2.3). The URI is kept whole, in memory, because the plugin needs it; whatever is shown
/// or written down uses <see cref="MaskedUri"/>. <see cref="Revision"/> is the revision of the source the text was read at or last written at; null: the file did not exist.
/// </summary>
internal sealed class SourceBinding(string uri, FileSource source, string? revision, bool fromCache, DateTimeOffset? cachedAt)
{
    public string Uri { get; } = uri;
    public FileSource Source { get; } = source;
    public string? Revision { get; set; } = revision;
    /// <summary>The source could not be reached and the tab shows the cached copy of <see cref="CachedAt"/>.</summary>
    public bool FromCache { get; set; } = fromCache;
    public DateTimeOffset? CachedAt { get; set; } = cachedAt;
    public string MaskedUri => UriMask.Mask(Uri);
}
