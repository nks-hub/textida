using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using Avalonia.Threading;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Files;
using NksHub.Textida.Core.History;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Review of changes from outside for the opened folder (P6.1.1): a file of the folder that another program changed while no tab shows it gets a review of its own, with the last content the user
/// saw or saved (a revision of the local history, pinned) as the base. The record is the same as that of a tab, so the file shows the marker and the changes by hunks when it is opened, and the
/// record survives a restart. The watch leaves out <c>.git</c>, <c>.codegraph</c>, what the ignore files of the folder ignore and the transcripts of agents (<see cref="ReviewIgnore"/>), and
/// files that hold secret values, files too large for a tab and binary files. A file created in the folder is reviewed as a whole-file addition; a changed file the history has no content of takes
/// the content it has at first sight as its base, and the status bar says so.
/// </summary>
internal sealed partial class MainWindow
{
    private FolderChangeWatcher? _folderWatcher;

    private int _ownWrites;
    private long _ownWriteUntil;

    private sealed class OwnWriteScope(MainWindow window) : IDisposable
    {
        public void Dispose()
        {
            window._ownWrites--;
            window._ownWriteUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * PluginLimitsPlaceholders.ReviewFolderOwnWriteSeconds;
        }
    }

    /// <summary>
    /// Marks the files this window writes itself while the scope is open and for a few seconds after it, so its watch of the folder does not take them for changes from outside.
    /// shortcut: a change from outside in that time is not reviewed either, and another window of the editor sees the writes as changes from outside; upgrade to a list of the paths that were written when that is felt.
    /// </summary>
    internal IDisposable OwnWrite()
    {
        _ownWrites++;
        return new OwnWriteScope(this);
    }

    private bool InOwnWrite => _ownWrites > 0 || Stopwatch.GetTimestamp() < _ownWriteUntil;

    private void StartFolderReview(FileTree tree)
    {
        StopFolderReview();
        var watcher = new FolderChangeWatcher(tree.Roots, TimeSpan.FromMilliseconds(PluginLimitsPlaceholders.ReviewFolderDelayMilliseconds), PluginLimitsPlaceholders.ReviewFolderMaxFiles);
        watcher.Changed += batch => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_folderWatcher, watcher)) _ = ReviewFolderChangesAsync(batch);
        });
        _folderWatcher = watcher;
        watcher.Start();
        if (WaitingFolderReviews().Count is > 0 and var waiting) SetStatusMessage(Loc.Plural(K.ReviewFolderWaiting, waiting));
    }

    private void StopFolderReview()
    {
        _folderWatcher?.Dispose();
        _folderWatcher = null;
    }

    /// <summary>The reviews of the opened folder that wait for a decision: the records of the profile for files below the folder.</summary>
    private List<ReviewState> WaitingFolderReviews() =>
        _fileTree is not { } tree ? [] : [.. _app.Reviews.List().Where(s => tree.Contains(s.Path) && File.Exists(s.Path))];

    /// <summary>
    /// Takes in a batch of files that changed in the folder: each one that no window shows, whose earlier content the history has and that now differs from everything the history knows
    /// gets a waiting review. A tab of a file keeps the bar of its own.
    /// </summary>
    internal async Task ReviewFolderChangesAsync(FolderChangeBatch batch)
    {
        if (InOwnWrite) return;
        var open = _app.Windows.SelectMany(w => w.Documents).Select(d => d.Path).OfType<string>().ToHashSet(PathComparer);
        var candidates = batch.Files.Where(p => !open.Contains(p) && !SecretValues.IsSecretFile(p)).ToList();
        if (batch.Dropped > 0) SetStatusMessage(Loc.Get(K.ReviewFolderDropped, ("count", batch.Dropped)));
        if (candidates.Count == 0) return;

        int room = Math.Max(0, PluginLimitsPlaceholders.ReviewFolderMaxWaiting - WaitingFolderReviews().Count);
        if (room == 0)
        {
            SetStatusMessage(Loc.Get(K.ReviewFolderFull, ("limit", PluginLimitsPlaceholders.ReviewFolderMaxWaiting)));
            return;
        }
        long largest = LargeFileBytes;
        var (started, taken) = await Task.Run(() =>
        {
            int waiting = 0, firstSight = 0;
            foreach (var p in candidates)
                switch (BeginFolderReview(p, largest, batch.IsCreated(p), waiting < room))
                {
                    case FolderSight.Waiting: waiting++; break;
                    case FolderSight.BaseTaken: firstSight++; break;
                }
            return (waiting, firstSight);
        });
        if (started > 0 && WaitingFolderReviews().Count is > 0 and var waitingNow) SetStatusMessage(Loc.Plural(K.ReviewFolderWaiting, waitingNow));
        else if (taken > 0) SetStatusMessage(Loc.Plural(K.ReviewFolderBaseTaken, taken));
    }

    private enum FolderSight { Skipped, Waiting, BaseTaken }

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Works on a pool thread. A review starts when a base exists and the file differs from every content the history holds. A file created in the folder that the history has never seen is a
    /// whole-file addition: its base is empty (only a byte order mark stays), so every line is a change. A changed file the history has never seen takes the content it has now as its base, which
    /// the next change is measured against.
    /// </summary>
    private FolderSight BeginFolderReview(string path, long largest, bool created, bool room)
    {
        try
        {
            if (_app.Reviews.Find(path) is not null) return FolderSight.Skipped;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > largest) return FolderSight.Skipped;
            var revisions = _app.History.List(path);

            string sha;
            byte[] head;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                head = new byte[Math.Min(8192, fs.Length)];
                head = head[..fs.Read(head, 0, head.Length)];
                if (head.AsSpan().Contains((byte)0)) return FolderSight.Skipped; // a binary file has no lines to review
                fs.Position = 0;
                sha = Convert.ToHexString(SHA256.HashData(fs));
            }

            bool Same(HistoryRevision r) => string.Equals(r.Sha256, sha, StringComparison.OrdinalIgnoreCase);
            var baseRevision = revisions.FirstOrDefault(r => r.Source != HistorySource.External);
            if (baseRevision is null)
            {
                bool added = created && revisions.Count == 0;
                var recorded = added ? _app.History.Record(path, head.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom : [], HistorySource.Initial) : _app.History.RecordFile(path, HistorySource.Initial);
                if (recorded.Revision is not { } first) return FolderSight.Skipped; // the history is off, the file is a secrets file or too large for it, or the write failed
                if (!added) return recorded.Status == HistoryStatus.Recorded ? FolderSight.BaseTaken : FolderSight.Skipped;
                if (Same(first)) return FolderSight.Skipped; // an empty new file: nothing to review yet, and the next write is measured against the empty base
                baseRevision = first;
            }
            else if (revisions.Any(Same)) return FolderSight.Skipped; // content the history knows was seen before

            if (!room || !TryPin(path, baseRevision.Id)) return FolderSight.Skipped;
            try { _app.Reviews.Save(new ReviewState(path, baseRevision.Id, DateTime.UtcNow)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                TryUnpin(path, baseRevision.Id);
                return FolderSight.Skipped;
            }
            return FolderSight.Waiting;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return FolderSight.Skipped; }
    }

    /// <summary>Lists the files of the opened folder that wait for the review of a change from outside; the chosen one opens with its changes.</summary>
    internal CommandOutcome ShowFolderReviews()
    {
        if (_fileTree is not { } tree)
        {
            SetStatusMessage(Loc.Get(K.ReviewFolderNoFolder));
            return CommandOutcome.Handled;
        }
        var waiting = WaitingFolderReviews();
        if (waiting.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.ReviewFolderNone));
            return CommandOutcome.Handled;
        }
        var rows = waiting.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase).Select(s => new QuickRow(DisplayPath(tree.Root, s.Path), null,
            Loc.Get(K.ReviewFolderDetail, ("when", s.CreatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))), null, true, null, s.Path, () => OpenFolderReview(s))).ToList();
        ShowChoice(Loc.Get(K.ReviewFolderWatermark), Loc.Get(K.A11yReviewFolder), rows, emptyText: Loc.Get(K.ReviewFolderNone));
        return CommandOutcome.Handled;
    }

    /// <summary>Opens the file in a tab; the tab takes the waiting review of the file and lists its changes.</summary>
    private void OpenFolderReview(ReviewState state)
    {
        DocTab tab;
        try { tab = OpenFile(state.Path, null); }
        catch (IOException e)
        {
            SetStatusMessage(e.Message);
            return;
        }
        if (tab.Doc.Review is null) SetStatusMessage(Loc.Get(K.ReviewSame)); // the file reads as it did: the record was dropped on opening
        else ShowReview(tab);
    }
}
