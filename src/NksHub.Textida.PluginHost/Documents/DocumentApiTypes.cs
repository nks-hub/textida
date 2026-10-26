using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>Method names of the document API v1 (requests from the plugin to the editor).</summary>
public static class DocumentMethods
{
    public const string List = "document/list";
    public const string Info = "document/info";
    public const string Subscribe = "document/subscribe";
    public const string Unsubscribe = "document/unsubscribe";
    public const string ReadBytes = "document/readBytes";
    public const string ReadText = "document/readText";
    public const string Find = "document/find";
    public const string Selections = "document/selections";
    public const string OffsetToPosition = "document/offsetToPosition";
    public const string PositionToOffset = "document/positionToOffset";
    public const string MapRanges = "document/mapRanges";
    public const string SnapshotCreate = "snapshot/create";
    public const string SnapshotRelease = "snapshot/release";
    public const string EditApply = "edit/apply";
    public const string EditPropose = "edit/propose";
}

/// <summary>Names of the events (<c>$/event</c> payloads) the editor sends for subscribed documents. They carry ranges and revisions, never text.</summary>
public static class DocumentEvents
{
    public const string Changed = "document/changed";
    /// <summary>Events of one document were dropped (the plugin was too slow); the payload holds the current revision to read again from.</summary>
    public const string EventsLost = "document/eventsLost";
    public const string Withdrawn = "document/withdrawn";
}

public static class DocumentPermissions
{
    public const string EditDirect = "document.edit.direct";
}

/// <summary>JSON-RPC error codes of the document API (implementation-defined range -32000..-32099; <c>error.data</c> carries details).</summary>
public static class DocumentErrorCodes
{
    /// <summary>The revision is not the current one (data: <c>reason</c>, <c>currentRevision</c>), or the change log cannot map a position unambiguously.</summary>
    public const int RevisionConflict = -32020;
    public const int SnapshotExpired = -32021;
    public const int SnapshotQuotaExceeded = -32022;
    public const int PermissionDenied = -32023;
    public const int UnknownDocument = -32024;
    /// <summary>Outside the document, inside the BOM or not on a character boundary.</summary>
    public const int InvalidRange = -32025;
    /// <summary>The line index does not know the position yet, or the offset is not a position (data: <c>lines</c>, <c>complete</c>).</summary>
    public const int PositionUnknown = -32026;
    public const int EditsOverlap = -32027;
}

/// <summary>
/// Quotas and policy of the document API. Everything is required: sizes, counts and lifetimes are owner decisions, not code defaults.
/// </summary>
public sealed record DocumentApiOptions
{
    /// <summary>Largest range returned by one read.</summary>
    public required int MaxReadBytes { get; init; }
    public required int MaxFindMatches { get; init; }
    public required int MaxSnapshotsPerPlugin { get; init; }
    public required TimeSpan SnapshotLifetime { get; init; }
    /// <summary>Events waiting for one plugin; beyond it the events of a document are dropped and replaced by one <see cref="DocumentEvents.EventsLost"/>.</summary>
    public required int EventQueueCapacity { get; init; }
    /// <summary>Applied batch ids remembered per plugin, so a repeated batch is answered and not applied twice.</summary>
    public required int MaxRememberedBatches { get; init; }
    /// <summary>Whether the plugin holds the permission (for example <see cref="DocumentPermissions.EditDirect"/>).</summary>
    public required Func<string, string, bool> HasPermission { get; init; }
    public required IEditProposals Proposals { get; init; }
}

/// <summary>Runs work on the thread that owns the documents (the UI thread in the editor); documents are not thread safe.</summary>
public interface IDocumentOwnerThread
{
    Task<T> InvokeAsync<T>(Func<T> work);
}

/// <summary>Delivers one event to one plugin; false when the plugin could not take it. <see cref="PluginHost.SendEventAsync"/> fits.</summary>
public delegate Task<bool> PluginEventSender(string pluginId, string eventName, ReadOnlyMemory<byte> payloadJson);

/// <summary>An edit batch a plugin proposes; the user sees it before anything changes. <paramref name="Revision"/> is the revision the edits refer to.</summary>
public sealed record EditProposal(string PluginId, Guid DocumentId, long Revision, IReadOnlyList<ByteEdit> Edits, string? Title);

/// <summary>The change preview service (P2.5.2). It takes proposals the plugin made against a checked, current revision.</summary>
public interface IEditProposals
{
    /// <summary>Returns the id of the stored proposal.</summary>
    ValueTask<string> SubmitAsync(EditProposal proposal, CancellationToken cancellation);
}
