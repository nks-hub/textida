using System.Text.Json;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.PluginHost.Mcp;

/// <summary>
/// <c>openFile</c>, <c>getOpenEditors</c>, <c>getWorkspaceFolders</c>, <c>checkDocumentDirty</c> and the <c>at_mentioned</c> notification. Names, arguments and results are those of the community
/// description of the protocol (https://github.com/coder/claudecode.nvim, PROTOCOL.md, "Available MCP Tools"; the 0-based lines of <c>at_mentioned</c> are what its Neovim client sends).
/// What the description leaves open is decided here: an agent learns about tabs the user shared and nothing else, and it can open only files inside the folders the lock file lists or tabs that are shared.
/// </summary>
public sealed partial class IdeBridge
{
    /// <summary>The notification the user starts to hand the agent a file or a range of lines as context. Params: filePath, lineStart, lineEnd (0-based, inclusive); the lines are left out for a whole file.</summary>
    public const string AtMentionMethod = "at_mentioned";

    private static StringComparison PathComparison => OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static string Json(Action<Utf8JsonWriter> body) => System.Text.Encoding.UTF8.GetString(DocumentApi.Obj(body));

    /// <summary>The tabs of the window the agent may reach, in the order the editor lists them.</summary>
    private async Task<IReadOnlyList<IdeEditor>> SharedEditorsAsync(McpToolContext c)
    {
        var list = _options.Editors ?? throw new ToolFailure("This editor does not list its tabs.");
        var reachable = (await c.ListSharedAsync().ConfigureAwait(false)).EnumerateArray().Select(d => d.GetProperty("id").GetGuid()).ToHashSet();
        return [.. (await list(c.Cancellation).ConfigureAwait(false)).Where(e => reachable.Contains(e.Document))];
    }

    private static string UriOf(IdeEditor editor) => editor.Path is null ? "untitled:" + editor.Document.ToString("N") : new Uri(editor.Path).AbsoluteUri;

    /// <summary>The absolute path of a file on this computer; null for a relative path, a network path (a connection to a server the agent names) or a device path.</summary>
    private static string? LocalFullPath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || (OperatingSystem.IsWindows() && (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)))) return null;
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private bool InsideWorkspace(string full)
    {
        foreach (var folder in _folders)
        {
            if (LocalFullPath(folder) is not { } root) continue;
            var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (full.StartsWith(prefix, PathComparison)) return true;
        }
        return false;
    }

    // ---- getOpenEditors, checkDocumentDirty, getWorkspaceFolders ----

    private async Task<string> OpenEditorsAsync(McpToolContext c, JsonElement args)
    {
        var editors = await SharedEditorsAsync(c).ConfigureAwait(false);
        Guid? active;
        lock (_gate) active = _active;
        return Json(w =>
        {
            w.WriteStartArray("tabs");
            foreach (var editor in editors)
            {
                w.WriteStartObject();
                w.WriteString("uri", UriOf(editor));
                w.WriteBoolean("isActive", editor.Document == active);
                w.WriteString("label", editor.Label);
                w.WriteString("languageId", editor.LanguageId);
                w.WriteBoolean("isDirty", editor.IsDirty);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    private async Task<string> CheckDirtyAsync(McpToolContext c, JsonElement args)
    {
        var asked = McpToolContext.Text(args, "filePath");
        var full = LocalFullPath(asked);
        var editor = full is null ? null : (await SharedEditorsAsync(c).ConfigureAwait(false)).FirstOrDefault(e => e.Path is { } p && string.Equals(p, full, PathComparison));
        return Json(w =>
        {
            w.WriteBoolean("success", editor is not null);
            if (editor is null)
            {
                w.WriteString("message", "Document not open: " + asked);
                return;
            }
            w.WriteString("filePath", editor.Path);
            w.WriteBoolean("isDirty", editor.IsDirty);
            w.WriteBoolean("isUntitled", false);
        });
    }

    private Task<string> WorkspaceFoldersAsync(McpToolContext c, JsonElement args)
    {
        var folders = _folders;
        return Task.FromResult(Json(w =>
        {
            w.WriteBoolean("success", true);
            w.WriteStartArray("folders");
            foreach (var folder in folders)
            {
                w.WriteStartObject();
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
                w.WriteString("name", name.Length > 0 ? name : folder);
                w.WriteString("uri", Uri.TryCreate(folder, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : folder);
                w.WriteString("path", folder);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            if (folders.Count > 0) w.WriteString("rootPath", folders[0]);
        }));
    }

    // ---- openFile ----

    private async Task<string> OpenFileAsync(McpToolContext c, JsonElement args)
    {
        var open = _options.OpenFile ?? throw new ToolFailure("This editor does not open files for the agent.");
        var request = new IdeOpenFileRequest(McpToolContext.Text(args, "filePath"), McpToolContext.OptionalBool(args, "preview") ?? false, McpToolContext.OptionalText(args, "startText"),
            McpToolContext.OptionalText(args, "endText"), McpToolContext.OptionalBool(args, "selectToEndOfLine") ?? false, McpToolContext.OptionalBool(args, "makeFrontmost") ?? true);
        var full = LocalFullPath(request.Path) ?? throw new ToolFailure("'filePath' must be the absolute path of a file on this computer.");
        bool shared = (await SharedEditorsAsync(c).ConfigureAwait(false)).Any(e => e.Path is { } p && string.Equals(p, full, PathComparison));
        if (!shared && !InsideWorkspace(full))
            throw new ToolFailure("The editor opens only files in the folders of its workspace (getWorkspaceFolders lists them) and tabs the user shared with you.");
        // What is learned about a file (its language, its size in lines) is only told about a tab the user shared.
        if (!shared && !request.MakeFrontmost)
            throw new ToolFailure("A file the user has not shared can only be opened as the front tab (makeFrontmost true); the editor does not describe it to you.");
        IdeOpenedFile opened;
        try { opened = await open(request with { Path = full }, c.Cancellation).ConfigureAwait(false); }
        catch (IdeOpenRefusedException e) { throw new ToolFailure(e.Message); }
        if (request.MakeFrontmost) return "Opened file: " + opened.Path;
        return Json(w =>
        {
            w.WriteBoolean("success", true);
            w.WriteString("filePath", opened.Path);
            w.WriteString("languageId", opened.LanguageId);
            w.WriteNumber("lineCount", opened.LineCount);
        });
    }

    // ---- at_mentioned ----

    /// <summary>
    /// The user hands the agent a file, or the lines of a range of it, as context. Nothing but the path and the line numbers goes out, and only for a tab shared with this window's bridge.
    /// An empty range (<paramref name="start"/> equal to <paramref name="end"/>) is the whole file. Returns whether an agent got it.
    /// </summary>
    public async Task<bool> PublishAtMentionAsync(Guid document, long start, long end, CancellationToken cancellation = default)
    {
        if (_options.Scope is { } scope && !scope(document)) return false;
        McpPeer[] peers;
        lock (_gate) peers = [.. _peers];
        if (peers.Length == 0) return false;
        var context = new McpToolContext(_options.Server, cancellation, _options.Scope);
        byte[] payload;
        try
        {
            var info = await context.CallAsync(DocumentMethods.Info, document).ConfigureAwait(false);
            if (info.GetProperty("path").GetString() is not { } path) return false;
            long revision = info.GetProperty("revision").GetInt64(), length = info.GetProperty("byteLength").GetInt64();
            start = Math.Clamp(start, 0, length);
            end = Math.Clamp(end, start, length);
            (int Line, int Column)? first = null, last = null;
            if (end > start)
            {
                first = await PositionAsync(context, document, revision, start).ConfigureAwait(false);
                last = await PositionAsync(context, document, revision, end).ConfigureAwait(false);
            }
            payload = DocumentApi.Obj(w =>
            {
                w.WriteString("filePath", path);
                if (first is not { } f || last is not { } l) return;
                w.WriteNumber("lineStart", f.Line);
                w.WriteNumber("lineEnd", l.Column == 0 && l.Line > f.Line ? l.Line - 1 : l.Line); // a range that ends at the start of a line does not take that line
            });
        }
        catch (RpcException) { return false; } // not shared, withdrawn or gone
        await Task.WhenAll(peers.Select(p => p.NotifyAsync(AtMentionMethod, payload))).ConfigureAwait(false);
        return true;
    }

    private static async Task<(int Line, int Column)> PositionAsync(McpToolContext context, Guid document, long revision, long offset)
    {
        var at = await context.CallAsync(DocumentMethods.OffsetToPosition, document, w => { w.WriteNumber("revision", revision); w.WriteNumber("offset", offset); }).ConfigureAwait(false);
        return (at.GetProperty("line").GetInt32(), at.GetProperty("column").GetInt32());
    }
}
