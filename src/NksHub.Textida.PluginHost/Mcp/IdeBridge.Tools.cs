using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Mcp;

/// <summary>The tools of the bridge, the selection feed and the diffs. Names, arguments and results follow the community description of the protocol (see the class remarks).</summary>
public sealed partial class IdeBridge
{
    /// <summary>The notification that tells the agent where the selection is. Params: text, filePath, fileUrl, selection {start, end, isEmpty}; lines and characters count from 0.</summary>
    public const string SelectionMethod = "selection_changed";

    private const string TabClosed = "TAB_CLOSED";
    private const string FileSaved = "FILE_SAVED";
    private const string DiffRejected = "DIFF_REJECTED";

    private readonly Dictionary<string, CancellationTokenSource> _diffs = [];
    private readonly SemaphoreSlim _publishing = new(1, 1);
    private long _publishSequence;
    private Guid? _active, _latest;

    private sealed record SelectionData(string Path, string Text, int StartLine, int StartCharacter, int EndLine, int EndCharacter)
    {
        public bool IsEmpty => StartLine == EndLine && StartCharacter == EndCharacter;
    }

    private McpToolDefinition[] BuildTools() =>
    [
        new("getDiagnostics", "Diagnostics",
            "Returns the errors and warnings of the shared tabs, for one file when 'uri' is given: an array of {uri, diagnostics: [{message, severity, range, source}]}.",
            "{\"type\":\"object\",\"properties\":{\"uri\":{\"type\":\"string\"}}}", true, McpTools.IdeDiagnostics),
        new("openDiff", "Open a diff",
            "Shows the change of a file in the change review of the editor and waits for the user. Answers FILE_SAVED when the user applied all of it and DIFF_REJECTED when the user rejected it. " +
            "The file must be open in the editor and shared by the user. Nothing is written to the disk.",
            "{\"type\":\"object\",\"properties\":{\"old_file_path\":{\"type\":\"string\"},\"new_file_path\":{\"type\":\"string\"},\"new_file_contents\":{\"type\":\"string\"},\"tab_name\":{\"type\":\"string\"}}," +
            "\"required\":[\"old_file_path\",\"new_file_contents\",\"tab_name\"]}", false, OpenDiffAsync),
        new("close_tab", "Close a diff", "Closes the diff with this name; an open diff then ends as rejected.",
            "{\"type\":\"object\",\"properties\":{\"tab_name\":{\"type\":\"string\"}},\"required\":[\"tab_name\"]}", false, CloseTab),
        new("closeAllDiffTabs", "Close all diffs", "Closes every open diff; they end as rejected.", "{\"type\":\"object\",\"properties\":{}}", false, CloseAllDiffs),
        new("getCurrentSelection", "Current selection", "Returns the selection in the tab the user works in, when that tab is shared.", "{\"type\":\"object\",\"properties\":{}}", true, CurrentSelectionAsync),
        new("getLatestSelection", "Latest selection", "Returns the selection in the shared tab the user worked in last.", "{\"type\":\"object\",\"properties\":{}}", true, LatestSelectionAsync),
    ];

    // ---- selection ----

    /// <summary>
    /// Tells every connected agent where the selection is in <paramref name="document"/>, and remembers it as the one the user works in. The text is read as the assistant of the user
    /// (<see cref="McpServer.ClientId"/>), so a document the user has not shared, a withdrawn share, a document of another window and hidden <c>.env</c> values are handled by the same
    /// rules as any other read; for such a document nothing is sent. A document without a file has no path to give and is not announced. Returns whether a notification went out.
    /// </summary>
    public async Task<bool> PublishSelectionAsync(Guid document, long start, long end, CancellationToken cancellation = default)
    {
        if (_options.Scope is { } scope && !scope(document)) return false;
        // One at a time and in the order they were asked, so the agent never ends up with an older selection than the user has; a selection that a newer one overtakes is not sent at all.
        long mine = Interlocked.Increment(ref _publishSequence);
        await _publishing.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (mine != Interlocked.Read(ref _publishSequence)) return false;
            McpPeer[] peers;
            lock (_gate) peers = [.. _peers];
            var context = new McpToolContext(_options.Server, cancellation, _options.Scope);
            SelectionData? data = null;
            bool hasFile;
            try
            {
                if (peers.Length == 0) hasFile = (await context.CallAsync(DocumentMethods.Info, document).ConfigureAwait(false)).GetProperty("path").ValueKind == JsonValueKind.String; // nobody to tell: only learn whether the tab may be remembered
                else hasFile = (data = await ReadSelectionAsync(context, document, start, end).ConfigureAwait(false)) is not null;
            }
            catch (RpcException) { return false; } // not shared, withdrawn, gone or the text moved on: the agent learns nothing
            lock (_gate)
            {
                if (mine != Interlocked.Read(ref _publishSequence)) return false; // the user moved on while the text was read
                _active = hasFile ? document : null;
                if (hasFile) _latest = document;
            }
            if (data is null) return false;
            var payload = SelectionParams(data);
            await Task.WhenAll(peers.Select(p => p.NotifyAsync(SelectionMethod, payload))).ConfigureAwait(false);
            return true;
        }
        finally { _publishing.Release(); }
    }

    /// <summary>The user moved to a tab that is not shared with the bridge: <c>getCurrentSelection</c> finds no active editor until a shared tab is selected again.</summary>
    public void ClearActive()
    {
        lock (_gate)
        {
            Interlocked.Increment(ref _publishSequence); // a selection of the tab the user just left must not make it active again
            _active = null;
        }
    }

    private async Task PushActiveSelectionAsync(McpPeer peer)
    {
        Guid? document;
        lock (_gate) document = _active;
        if (document is null) return;
        try
        {
            var data = await LiveSelectionAsync(new McpToolContext(_options.Server, _stop.Token, _options.Scope), document.Value).ConfigureAwait(false);
            if (data is not null) await peer.NotifyAsync(SelectionMethod, SelectionParams(data)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException) { /* the bridge is closing */ }
    }

    /// <summary>The primary selection of the document as it is now; null when the document is not shared (any more), has no file or its line index does not reach the selection.</summary>
    private async Task<SelectionData?> LiveSelectionAsync(McpToolContext context, Guid document)
    {
        try
        {
            var taken = await context.CallAsync(DocumentMethods.Selections, document).ConfigureAwait(false);
            int primary = taken.GetProperty("primary").GetInt32();
            if (primary < 0) return null;
            var item = taken.GetProperty("selections")[primary];
            long anchor = item.GetProperty("anchor").GetInt64(), caret = item.GetProperty("caret").GetInt64();
            return await ReadSelectionAsync(context, document, Math.Min(anchor, caret), Math.Max(anchor, caret)).ConfigureAwait(false);
        }
        catch (RpcException) { return null; }
    }

    private async Task<SelectionData?> ReadSelectionAsync(McpToolContext context, Guid document, long start, long end)
    {
        var info = await context.CallAsync(DocumentMethods.Info, document).ConfigureAwait(false);
        if (info.GetProperty("path").GetString() is not { } path) return null;
        long revision = info.GetProperty("revision").GetInt64();
        long length = info.GetProperty("byteLength").GetInt64();
        start = Math.Clamp(start, 0, length);
        end = Math.Clamp(end, start, Math.Min(length, start + _options.Server.Options.MaxRangeBytes));
        var read = await context.CallAsync(DocumentMethods.ReadText, document, w =>
        {
            w.WriteNumber("revision", revision);
            w.WriteNumber("start", start);
            w.WriteNumber("end", end);
            w.WriteBoolean("snap", true);
        }).ConfigureAwait(false);
        var from = await context.CallAsync(DocumentMethods.OffsetToPosition, document, w => { w.WriteNumber("revision", revision); w.WriteNumber("offset", read.GetProperty("start").GetInt64()); }).ConfigureAwait(false);
        var to = await context.CallAsync(DocumentMethods.OffsetToPosition, document, w => { w.WriteNumber("revision", revision); w.WriteNumber("offset", read.GetProperty("end").GetInt64()); }).ConfigureAwait(false);
        return new SelectionData(path, read.GetProperty("text").GetString() ?? "",
            from.GetProperty("line").GetInt32(), from.GetProperty("column").GetInt32(), to.GetProperty("line").GetInt32(), to.GetProperty("column").GetInt32());
    }

    private static void WriteRange(Utf8JsonWriter w, SelectionData s, bool withEmpty)
    {
        w.WriteStartObject("selection");
        w.WriteStartObject("start"); w.WriteNumber("line", s.StartLine); w.WriteNumber("character", s.StartCharacter); w.WriteEndObject();
        w.WriteStartObject("end"); w.WriteNumber("line", s.EndLine); w.WriteNumber("character", s.EndCharacter); w.WriteEndObject();
        if (withEmpty) w.WriteBoolean("isEmpty", s.IsEmpty);
        w.WriteEndObject();
    }

    private static byte[] SelectionParams(SelectionData s) => DocumentApi.Obj(w =>
    {
        w.WriteString("text", s.Text);
        w.WriteString("filePath", s.Path);
        if (Uri.TryCreate(s.Path, UriKind.Absolute, out var uri)) w.WriteString("fileUrl", uri.AbsoluteUri);
        WriteRange(w, s, withEmpty: true);
    });

    private static string SelectionResult(SelectionData? s, string missing) => System.Text.Encoding.UTF8.GetString(DocumentApi.Obj(w =>
    {
        w.WriteBoolean("success", s is not null);
        if (s is null)
        {
            w.WriteString("message", missing);
            return;
        }
        w.WriteString("text", s.Text);
        w.WriteString("filePath", s.Path);
        WriteRange(w, s, withEmpty: false);
    }));

    private async Task<string> CurrentSelectionAsync(McpToolContext c, JsonElement args)
    {
        Guid? document;
        lock (_gate) document = _active;
        return SelectionResult(document is { } id ? await LiveSelectionAsync(c, id).ConfigureAwait(false) : null, "No active editor found");
    }

    private async Task<string> LatestSelectionAsync(McpToolContext c, JsonElement args)
    {
        Guid? document;
        lock (_gate) document = _latest;
        return SelectionResult(document is { } id ? await LiveSelectionAsync(c, id).ConfigureAwait(false) : null, "No selection available");
    }

    // ---- diffs ----

    private async Task<string> OpenDiffAsync(McpToolContext c, JsonElement args)
    {
        var show = _options.ShowDiff ?? throw new ToolFailure("This editor does not show diffs.");
        var request = new IdeDiffRequest(McpToolContext.Text(args, "old_file_path"), McpToolContext.OptionalText(args, "new_file_path"),
            McpToolContext.Text(args, "new_file_contents", allowEmpty: true), McpToolContext.Text(args, "tab_name"));
        using var own = CancellationTokenSource.CreateLinkedTokenSource(c.Cancellation);
        CancellationTokenSource? replaced;
        lock (_gate)
        {
            // a diff with the name of one that is still open replaces it; any other adds to those waiting for the user
            int waiting = c.Options.Proposals.PendingCount + _diffs.Count;
            if (waiting >= c.Options.MaxPendingProposals && !_diffs.ContainsKey(request.TabName))
                throw new ToolFailure($"{waiting} changes are waiting for the decision of the user, which is the limit. Wait until the user has decided on some before opening more.");
            _diffs.Remove(request.TabName, out replaced);
            _diffs[request.TabName] = own;
        }
        Cancel(replaced);
        IdeDiffOutcome outcome;
        try { outcome = await show(request, own.Token).ConfigureAwait(false); }
        catch (IdeDiffRefusedException e) { throw new ToolFailure(e.Message); }
        finally
        {
            lock (_gate)
            {
                if (_diffs.TryGetValue(request.TabName, out var current) && ReferenceEquals(current, own)) _diffs.Remove(request.TabName);
            }
        }
        return outcome switch
        {
            IdeDiffOutcome.Saved => FileSaved,
            IdeDiffOutcome.Rejected => DiffRejected,
            IdeDiffOutcome.Partial => throw new ToolFailure("The user applied only some of the changes. Read the file again."),
            IdeDiffOutcome.Stale => throw new ToolFailure("The file changed in the editor before the user decided. Read it again and propose the change again."),
            _ => throw new ToolFailure("The editor could not apply the change."),
        };
    }

    private Task<string> CloseTab(McpToolContext c, JsonElement args)
    {
        CancelDiffs(McpToolContext.Text(args, "tab_name"));
        return Task.FromResult(TabClosed);
    }

    private Task<string> CloseAllDiffs(McpToolContext c, JsonElement args) => Task.FromResult($"CLOSED_{CancelDiffs(null)}_DIFF_TABS");

    /// <summary>Ends the diff with this name, or every diff when the name is null; returns how many there were.</summary>
    private int CancelDiffs(string? tabName)
    {
        List<CancellationTokenSource> taken = [];
        lock (_gate)
        {
            if (tabName is null)
            {
                taken.AddRange(_diffs.Values);
                _diffs.Clear();
            }
            else if (_diffs.Remove(tabName, out var one)) taken.Add(one);
        }
        foreach (var diff in taken) Cancel(diff);
        return taken.Count;
    }

    private static void Cancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { /* the diff ended on its own meanwhile */ }
    }
}
