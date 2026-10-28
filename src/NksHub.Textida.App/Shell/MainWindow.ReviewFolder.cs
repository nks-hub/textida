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
/// files that hold secret values, files too large for a tab, binary files and files the history knows no content of.
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
        var started = await Task.Run(() => candidates.Select(p => BeginFolderReview(p, largest)).OfType<ReviewState>().Take(room).ToList());
        if (started.Count > 0 && WaitingFolderReviews().Count is > 0 and var waiting) SetStatusMessage(Loc.Plural(K.ReviewFolderWaiting, waiting));
    }

    /// <summary>Works on a pool thread. The review of a file starts when a base exists and the file differs from every content the history holds; null when it does not.</summary>
    private ReviewState? BeginFolderReview(string path, long largest)
    {
        try
        {
            if (_app.Reviews.Find(path) is not null) return null;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > largest) return null;
            var revisions = _app.History.List(path);
            if (revisions.FirstOrDefault(r => r.Source != HistorySource.External) is not { } baseRevision) return null;

            string sha;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var head = new byte[Math.Min(8192, fs.Length)];
                int read = fs.Read(head, 0, head.Length);
                if (head.AsSpan(0, read).Contains((byte)0)) return null; // a binary file has no lines to review
                fs.Position = 0;
                sha = Convert.ToHexString(SHA256.HashData(fs));
            }
            if (revisions.Any(r => string.Equals(r.Sha256, sha, StringComparison.OrdinalIgnoreCase))) return null; // content the history knows was seen before

            if (!TryPin(path, baseRevision.Id)) return null;
            var state = new ReviewState(path, baseRevision.Id, DateTime.UtcNow);
            try { _app.Reviews.Save(state); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                TryUnpin(path, baseRevision.Id);
                return null;
            }
            return state;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
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
