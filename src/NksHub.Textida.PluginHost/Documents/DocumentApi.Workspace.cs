using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Changes;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Documents;

/// <summary>
/// The API for a workspace edit: one batch of text edits in several documents or files and of file operations (create, rename, delete), shown in the change review as one
/// proposal and applied all or nothing. The whole batch is checked before anything is shown: an entry the plugin may not touch (no grant for the document or for files, a path outside
/// the workspace, a text edit by path of a file that is open and so has to be named as a document) or an entry that does not match the revision or hash it names refuses the whole batch.
/// A rename or delete of an open file is allowed: the editor moves the tab with the file, or closes it (and asks first when the tab holds unsaved work).
/// </summary>
public sealed partial class DocumentApi
{
    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private async Task<byte[]> WorkspaceEditAsync(string pluginId, JsonElement p, CancellationToken ct)
    {
        var ws = _options.Workspace ?? throw new RpcException(RpcErrorCodes.MethodNotFound, "This editor takes no workspace edits.");
        if (_options.Permissions.IsDelegated(pluginId))
            throw new RpcException(DocumentErrorCodes.PermissionDenied, "A command another plugin started cannot write documents.")
            { DataJson = $"{{\"permission\":\"{DocumentPermissions.EditPropose}\",\"reason\":\"Delegated\"}}" };
        var title = p.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
        var changes = Need(p, "changes");
        if (changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0) throw Invalid("Parameter 'changes' must be a list with at least one change.");
        if (changes.GetArrayLength() > ws.MaxChanges)
            throw Fail(DocumentErrorCodes.WorkspaceQuotaExceeded, $"A workspace edit may hold {ws.MaxChanges} changes.", w => w.WriteNumber("limit", ws.MaxChanges));
        var proposal = await _owner.InvokeAsync(() => BuildWorkspaceProposal(pluginId, title ?? pluginId, changes, ws));
        var decision = await ws.Service.SubmitAsync(new WorkspaceEditRequest(pluginId, proposal), ct);
        return Obj(w =>
        {
            w.WriteString("proposal", decision.Id);
            w.WriteString("decision", decision.Outcome.ToString().ToLowerInvariant());
            w.WriteNumber("accepted", decision.Accepted);
            w.WriteNumber("total", decision.Total);
        });
    }

    private ChangeProposal BuildWorkspaceProposal(string pluginId, string title, JsonElement changes, WorkspaceEditOptions ws)
    {
        var roots = ws.Roots().Select(r => Path.GetFullPath(r)).ToList();
        var files = new List<FileChange>();
        var taken = new HashSet<string>(PathComparer);
        void Take(string key, int index)
        {
            if (!taken.Add(key)) throw Invalid($"Change {index} touches '{key}', which an earlier change of the batch already touches.");
        }
        int index = 0;
        long bytes = 0;
        foreach (var item in changes.EnumerateArray())
        {
            var change = NeedString(item, "kind") switch
            {
                "edit" => WorkspaceTextEdit(pluginId, item, index, ws, roots),
                "create" => WorkspaceCreate(pluginId, item, index, ws, roots),
                "rename" => WorkspaceRename(pluginId, item, index, ws, roots),
                "delete" => WorkspaceDelete(pluginId, item, index, ws, roots),
                _ => throw Invalid($"Change {index}: 'kind' must be edit, create, rename or delete."),
            };
            Take(change.Key, index);
            if (change.Path is { } path && change.Key != path) Take(path, index);
            if (change.Operation?.NewPath is { } target) Take(target, index);
            if (change.Operation is not null && ChangeApplier.CheckOperation(change) is { } blocked) throw WorkspaceConflict(index, blocked.Message);
            bytes += NewTextBytes(change);
            if (bytes > ws.MaxTotalBytes)
                throw Fail(DocumentErrorCodes.WorkspaceQuotaExceeded, $"A workspace edit may hold {ws.MaxTotalBytes} bytes of new text and new files together.", w => w.WriteNumber("limitBytes", ws.MaxTotalBytes));
            files.Add(change);
            index++;
        }
        return ChangeBuilder.Proposal(title, "plugin:" + pluginId, [.. files]) with { Atomic = true };
    }

    /// <summary>Bytes of text a change brings: the replacements of an edit, the content of a new file.</summary>
    private static long NewTextBytes(FileChange change) =>
        (change.Operation?.Content?.Length ?? 0) + change.Hunks.Sum(h => (long)Encoding.UTF8.GetByteCount(h.NewText));

    private static RpcException WorkspaceConflict(int index, string message) =>
        Fail(DocumentErrorCodes.RevisionConflict, $"Change {index}: {message}", w =>
        {
            w.WriteString("reason", "SourceChanged");
            w.WriteNumber("index", index);
        });

    private static RpcException Denied(string permission, string reason, string message) => PluginPermissions.Denied(permission, reason, message);

    /// <summary>The full path of a file the plugin may touch, or an error that refuses the batch.</summary>
    private string WorkspacePath(string pluginId, JsonElement item, string name, IReadOnlyList<string> roots)
    {
        var raw = NeedString(item, name);
        if (!Path.IsPathFullyQualified(raw)) throw Invalid($"Parameter '{name}' must be an absolute path.");
        if (!_options.Permissions.IsGranted(pluginId, DocumentPermissions.WorkspaceFiles, GrantScope.All))
            throw Denied(DocumentPermissions.WorkspaceFiles, "NotGranted", $"The plugin does not hold the '{DocumentPermissions.WorkspaceFiles}' permission, so it may only edit documents it was given.");
        var full = Path.GetFullPath(raw);
        if (!roots.Any(root => PathMove.IsInside(full, root)))
            throw Denied(DocumentPermissions.WorkspaceFiles, "OutsideWorkspace", $"The path '{full}' is outside the workspace.");
        // a link on the way (a folder above the file, or the file) may not lead out of the workspace; the links of the workspace folder itself are followed too
        if (!PathMove.TryRealPath(full, out var real) || !roots.Any(root => PathMove.TryRealPath(root, out var realRoot) && PathMove.IsInside(real, realRoot)))
            throw Denied(DocumentPermissions.WorkspaceFiles, "Link", $"'{full}' reaches a place outside the workspace through a link, or its links cannot be followed.");
        // shortcut: a link as the file itself is refused even when it stays inside, because the write would go through it; the link may also change between this check and the write
        if (File.Exists(full) && new FileInfo(full).LinkTarget is not null) throw Denied(DocumentPermissions.WorkspaceFiles, "Link", $"'{full}' is a link; name the file it points to.");
        return full;
    }

    private static string Sha(JsonElement item) => NeedString(item, "sha256");

    private static RpcException HashMismatch(int index, string path) => WorkspaceConflict(index, $"'{path}' is not the content the plugin saw (its hash differs).");

    private static List<TextReplacement> ParseTextEdits(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0) throw Invalid("Parameter 'edits' must be a list with at least one edit.");
        List<TextReplacement> edits = [];
        foreach (var item in array.EnumerateArray())
        {
            long start = NeedLong(item, "start"), end = NeedLong(item, "end");
            if (start < 0 || end < start) throw Invalid($"The range [{start}, {end}) is not valid.");
            edits.Add(new TextReplacement(new ByteRange(start, end), NeedString(item, "text")));
        }
        var sorted = edits.OrderBy(x => x.Range.Start).ThenBy(x => x.Range.IsEmpty ? 0 : 1).ToList();
        for (int i = 1; i < sorted.Count; i++)
            if (sorted[i - 1].Range.End > sorted[i].Range.Start || (sorted[i - 1].Range.IsEmpty && sorted[i].Range.IsEmpty && sorted[i - 1].Range.Start == sorted[i].Range.Start))
                throw EditFailure(ConflictReason.EditsOverlap, 0);
        return edits;
    }

    private FileChange WorkspaceTextEdit(string pluginId, JsonElement item, int index, WorkspaceEditOptions ws, IReadOnlyList<string> roots)
    {
        var edits = ParseTextEdits(Need(item, "edits"));
        try
        {
            if (item.TryGetProperty("document", out _))
            {
                var d = FindWritable(NeedDocumentId(item)).Doc;
                if (!_options.Permissions.IsGranted(pluginId, DocumentPermissions.EditPropose, GrantScope.OfDocument(d.Id)))
                    throw Denied(DocumentPermissions.EditPropose, "NotGranted", $"The plugin does not hold the '{DocumentPermissions.EditPropose}' permission for the document of change {index}.");
                long revision = NeedLong(item, "revision");
                if (d.Revision != revision) throw Conflict(revision > d.Revision ? "FutureRevision" : "Stale", d.Revision, index);
                GuardSecrets(pluginId, d, [.. edits.Select(e => new ByteEdit(e.Range, Array.Empty<byte>()))]);
                foreach (var edit in edits) d.Codec.Encode(edit.NewText);
                return ChangeBuilder.ForDocument(d, edits);
            }
            var path = WorkspacePath(pluginId, item, "path", roots);
            if (ws.IsOpen(path)) throw Invalid($"Change {index}: '{path}' is open in the editor; name its document instead of its path.");
            if (SecretValues.IsSecretFile(path)) throw Denied(DocumentPermissions.SecretsReveal, "Secret", $"'{path}' is a secrets file; a plugin cannot edit it without seeing it.");
            if (!File.Exists(path)) throw Invalid($"Change {index}: '{path}' does not exist.");
            var change = ChangeBuilder.ForFile(path, edits);
            if (!string.Equals(change.BaseFingerprint!.Sha256, Sha(item), StringComparison.OrdinalIgnoreCase)) throw HashMismatch(index, path);
            return change;
        }
        catch (ArgumentException) { throw EditFailure(ConflictReason.InvalidRange, 0); }
        catch (UnrepresentableCharactersException) { throw Invalid($"Change {index}: the text cannot be written in the encoding of the file."); }
        catch (Exception e) when (e is NotSupportedException or InvalidOperationException or IOException or UnauthorizedAccessException) { throw Invalid($"Change {index}: {e.Message}"); }
    }

    private FileChange Operation(string path, FileOperation operation, FileFingerprint? fingerprint) =>
        new(path, path, null, null, fingerprint, TextCodec.Utf8, [], operation);

    private FileChange WorkspaceCreate(string pluginId, JsonElement item, int index, WorkspaceEditOptions ws, IReadOnlyList<string> roots)
    {
        var path = WorkspacePath(pluginId, item, "path", roots);
        bool hasText = item.TryGetProperty("text", out var text), hasBytes = item.TryGetProperty("bytes", out var bytes);
        if (hasText == hasBytes) throw Invalid($"Change {index}: a create has either 'text' or 'bytes'.");
        if ((hasText ? text : bytes).ValueKind != JsonValueKind.String) throw Invalid($"Change {index}: '{(hasText ? "text" : "bytes")}' must be a string.");
        byte[] content;
        try { content = hasText ? new UTF8Encoding(false).GetBytes(text.GetString()!) : Convert.FromBase64String(bytes.GetString()!); }
        catch (FormatException) { throw Invalid($"Change {index}: 'bytes' is not valid base64."); }
        if (content.Length > ws.MaxFileBytes) throw Invalid($"Change {index}: a new file may hold {ws.MaxFileBytes} bytes.");
        return Operation(path, new FileOperation(FileOperationKind.Create, Content: content), null);
    }

    private FileChange WorkspaceRename(string pluginId, JsonElement item, int index, WorkspaceEditOptions ws, IReadOnlyList<string> roots)
    {
        var path = WorkspacePath(pluginId, item, "path", roots);
        var target = WorkspacePath(pluginId, item, "to", roots);
        if (string.Equals(path, target, StringComparison.Ordinal)) throw Invalid($"Change {index}: the new name is the old one.");
        FileFingerprint? print = null;
        if (item.TryGetProperty("sha256", out _))
        {
            print = FileFingerprint.TryRead(path) ?? throw WorkspaceConflict(index, $"'{path}' cannot be read.");
            if (!string.Equals(print.Sha256, Sha(item), StringComparison.OrdinalIgnoreCase)) throw HashMismatch(index, path);
        }
        return Operation(path, new FileOperation(FileOperationKind.Rename, target), print);
    }

    private FileChange WorkspaceDelete(string pluginId, JsonElement item, int index, WorkspaceEditOptions ws, IReadOnlyList<string> roots)
    {
        var path = WorkspacePath(pluginId, item, "path", roots);
        var print = FileFingerprint.TryRead(path) ?? throw WorkspaceConflict(index, $"'{path}' is gone or cannot be read.");
        if (!string.Equals(print.Sha256, Sha(item), StringComparison.OrdinalIgnoreCase)) throw HashMismatch(index, path);
        return Operation(path, new FileOperation(FileOperationKind.Delete), print);
    }
}
