using NksHub.Textida.Core.Files;

namespace NksHub.Textida.Plugins.Git;

/// <summary>What <c>git status</c> says, in the few states the file tree shows.</summary>
public static class TreeStatus
{
    /// <summary>The state of one line of the status: the two letters in front of the path (space unchanged, <c>?</c> untracked, <c>!</c> ignored).</summary>
    public static GitFileState Classify(StatusEntry e)
    {
        if (e.Index == '!') return GitFileState.Ignored;
        if (e.Index == '?') return GitFileState.New;
        if (StatusCounts.IsConflict(e)) return GitFileState.Conflict;
        if (e.Index == 'D' || e.Tree == 'D') return GitFileState.Deleted;
        if (e.Index is 'A' or 'C' || e.Tree == 'A') return GitFileState.New; // added to the index, copied, or marked to be added
        return GitFileState.Modified; // changed, renamed, type changed
    }

    /// <summary>
    /// The entries as the tree wants them: the state and the path relative to the folder the repository was opened from (<paramref name="prefix"/> is the path of that folder inside
    /// the working tree, with a closing slash, empty at the top). An entry outside that folder, and the folder itself, are left out.
    /// </summary>
    public static List<(GitFileState State, string Path)> Of(IEnumerable<StatusEntry> entries, string prefix)
    {
        var list = new List<(GitFileState, string)>();
        foreach (var e in entries)
        {
            if (!e.Path.StartsWith(prefix, StringComparison.Ordinal) || e.Path.Length == prefix.Length) continue;
            list.Add((Classify(e), e.Path[prefix.Length..]));
        }
        return list;
    }
}
