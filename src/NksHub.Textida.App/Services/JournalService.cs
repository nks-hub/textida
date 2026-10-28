using NksHub.Textida.App.Editing;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The recovery journal of the running app: where it lives, what the previous run left in it and the tab layout.
/// A run that ended normally leaves a flag next to the journal; without it the previous run crashed or was killed.
/// </summary>
internal sealed class JournalService : IDisposable
{
    private const string CleanExitFlag = "clean-exit.flag";

    private JournalService(JournalSession session, JournalLocationResult location, bool previousRunEndedCleanly, string? directory)
    {
        Session = session;
        Location = location;
        PreviousRunEndedCleanly = previousRunEndedCleanly;
        Directory = directory;
    }

    /// <summary>
    /// Changes of one document above which start does not replay it but asks (bytes inserted and removed); read when the next run starts, so a test lowers it before
    /// <see cref="Start"/>. Placeholder, OPEN: <see cref="JournalPlaceholders.AskBeforeRecoverBytes"/>.
    /// </summary>
    internal static long AskBeforeRecoverBytes { get; set; } = JournalPlaceholders.AskBeforeRecoverBytes;

    public JournalSession Session { get; }
    public JournalLocationResult Location { get; }
    public string? Directory { get; }
    public bool PreviousRunEndedCleanly { get; }
    public bool IsActive => Session.IsActive;
    public RecoveryResult Recovery => Session.Recovery;

    /// <summary>True when the last run left unsaved or open work behind without having ended normally.</summary>
    public bool CrashedLastTime => !PreviousRunEndedCleanly && Recovery.Documents.Count > 0;

    /// <summary>Documents whose large changes wait for the choice to restore or discard them.</summary>
    public IReadOnlyList<RecoveredDocument> Pending => Session.Pending;

    /// <summary>
    /// Replays the large changes of a pending document on a thread of the pool; the document is tracked by the journal when this ends. A document of the text
    /// control belongs to the thread that creates it, so only a paged one is replayed off the UI thread; the others (small ones) are replayed on the calling thread.
    /// </summary>
    public Task<RecoveredDocument> RecoverPendingAsync(RecoveredDocument pending) =>
        pending.Backend == PagedBackendFactory.Instance.Capabilities.Name
            ? Task.Run(() => Session.RecoverPending(pending.DocId))
            : Task.FromResult(Session.RecoverPending(pending.DocId));

    /// <summary>Throws the large changes of a pending document away; the file of the document stays as it is.</summary>
    public Task<bool> DiscardPendingAsync(Guid docId) => Task.Run(() => Session.DiscardPending(docId));

    public static IBackendFactory? BackendByName(string name) => name switch
    {
        "avaloniaedit" => AvaloniaEditBackendFactory.Instance,
        "paged" => PagedBackendFactory.Instance,
        _ => null,
    };

    /// <summary>Resolves the journal location (primary, then fallback), recovers what earlier runs left and starts journaling.</summary>
    public static JournalService Start(string primary, string? fallback)
    {
        var location = JournalLocation.Resolve(primary, fallback);
        bool clean = false;
        if (location.Directory is { } dir)
        {
            var flag = Path.Combine(dir, CleanExitFlag);
            clean = File.Exists(flag);
            try { File.Delete(flag); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { clean = false; }
        }
        var session = JournalSession.Start(location.Directory, BackendByName, recovery: new RecoveryOptions { DeferAboveBytes = AskBeforeRecoverBytes, HashInBackgroundAboveBytes = PluginLimitsPlaceholders.OpenHashInBackgroundBytes });
        return new JournalService(session, location, clean, location.Directory);
    }

    /// <summary>Starts journaling a new or freshly opened document; a recovered one is already tracked.</summary>
    public void Track(OpenDocument doc)
    {
        if (doc.JournalId is not null || doc.IsResults || doc.IsVirtual) return;
        doc.JournalId = Session.Track(doc.Doc);
    }

    public void Untrack(OpenDocument doc)
    {
        if (doc.IsResults || doc.IsVirtual) return;
        Session.Untrack(doc.Doc);
        doc.JournalId = null;
    }

    public void Flush() => Session.Flush();

    public void UpdateSession(SessionModel model) => Session.UpdateSession(model);

    /// <summary>Makes everything durable and marks the run as ended normally.</summary>
    public void Shutdown()
    {
        if (Session.IsActive)
        {
            Session.Flush();
            if (Directory is { } dir)
            {
                try { File.WriteAllBytes(Path.Combine(dir, CleanExitFlag), []); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* without the flag the next start shows the restore bar once more */ }
            }
        }
        Dispose();
    }

    public void Dispose() => Session.Dispose();
}
