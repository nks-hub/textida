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

    public JournalSession Session { get; }
    public JournalLocationResult Location { get; }
    public string? Directory { get; }
    public bool PreviousRunEndedCleanly { get; }
    public bool IsActive => Session.IsActive;
    public RecoveryResult Recovery => Session.Recovery;

    /// <summary>True when the last run left unsaved or open work behind without having ended normally.</summary>
    public bool CrashedLastTime => !PreviousRunEndedCleanly && Recovery.Documents.Count > 0;

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
        var session = JournalSession.Start(location.Directory, BackendByName);
        return new JournalService(session, location, clean, location.Directory);
    }

    /// <summary>Starts journaling a new or freshly opened document; a recovered one is already tracked.</summary>
    public void Track(OpenDocument doc)
    {
        if (doc.JournalId is not null) return;
        doc.JournalId = Session.Track(doc.Doc);
    }

    public void Untrack(OpenDocument doc)
    {
        Session.Untrack(doc.Doc);
        doc.JournalId = null;
    }

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
