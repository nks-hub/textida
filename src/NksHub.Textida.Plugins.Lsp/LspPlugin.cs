using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Ui;
using NksHub.Textida.PluginSdk;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugins.Lsp;

/// <summary>
/// The built-in language server client. A server is started on the first command for a language in a project (its root found by the marker files of the language, then by <c>.git</c> or <c>.textida</c>), told about the documents the editor shows (with the revision as the
/// version, as ranges when the server wants them) and asked for definitions and references; what it finds wrong is published through the diagnostics API of the editor.
/// A server that ends by itself is reported; the next command starts it again and hands it every document that is still open.
/// It also answers the completion requests of the editor for the documents it follows, and renames a symbol: the server's workspace edit goes to the editor as one batch for the change review.
/// </summary>
public sealed class LspPlugin : PluginBase
{
    public const string StartCommand = "lsp.start";
    public const string DefinitionCommand = "lsp.definition";
    public const string ReferencesCommand = "lsp.references";
    public const string RestartCommand = "lsp.restart";
    public const string StopCommand = "lsp.stop";
    public const string RenameCommand = "lsp.rename";
    public const string Source = "lsp";
    public const string LocationsPanel = "lsp:locations";

    private enum State { Stopped, Running, Crashed, NotFound }

    /// <summary>One server process, for the documents of one language in one project root with one server configuration.</summary>
    private sealed class Slot(string language, string root, ServerLaunch? declared)
    {
        public readonly string Language = language;
        public readonly string Root = root;
        public readonly SemaphoreSlim Lifecycle = new(1, 1);
        public readonly ConcurrentDictionary<Guid, Doc> Docs = new();
        public LspServer? Server;
        public State State;
        /// <summary>The server a plugin manifest declares for the language, as the command that made the slot sent it; it wins over the catalog.</summary>
        public readonly ServerLaunch? Declared = declared;
        /// <summary>Changes when a document joins or the last one leaves, so a pending idle stop knows it is out of date.</summary>
        public int IdleTicket;
    }

    private sealed class Doc(Guid id, Slot slot, string uri, string? path)
    {
        public readonly Guid Id = id;
        /// <summary>The server slot the document belongs to; it changes only when a command brings another language or server configuration for the document.</summary>
        public volatile Slot Slot = slot;
        public readonly string Uri = uri;
        public readonly string? Path = path;
        /// <summary>Held while the text, the revision or the version of this document change or are read together.</summary>
        public readonly SemaphoreSlim Gate = new(1, 1);
        /// <summary>The text the server has, and the revision of the editor it was read at.</summary>
        public string Text = "";
        public long Revision = -1;
        /// <summary>The version the server was last told (the revision of the text it holds).</summary>
        public long Version;
        public bool Open;
        public TextCodec Codec = TextCodec.Utf8;
        public int Bom;
        public bool Dirty;
        public bool Pumping;
    }

    /// <summary>The document cannot be handed to a language server (too large, bytes the encoding does not allow); <see cref="Exception.Message"/> is the status.</summary>
    private sealed class Withheld(string status) : Exception(status);

    private sealed class ServerGone : Exception;

    private readonly LspSettings _settings;
    private readonly ServerCatalog _catalog;
    // shortcut: one process per project root, even for a server that supports several workspace folders; sharing would need that proven per server, and no server of the catalog is checked for it.
    private readonly ConcurrentDictionary<string, Slot> _slots = new();
    private readonly ConcurrentDictionary<Guid, Doc> _docs = new();
    private volatile string _ui = "en";

    public LspPlugin(LspSettings settings, ServerCatalog catalog) : base("lsp", apiVersion: 1)
    {
        _settings = settings;
        _catalog = catalog;
        On("command/execute", ExecuteAsync);
        On(DocumentRequests.Completion, CompleteAsync);
    }

    /// <summary>Offers the editor to ask this plugin for completion; sent on every start, so a restarted process answers again.</summary>
    protected override async ValueTask OnResyncAsync(JsonElement state)
    {
        try { await CallAsync(DocumentMethods.CompletionRegister, "{\"enabled\":true}").ConfigureAwait(false); }
        catch (Exception e) when (e is RpcException or IOException) { /* the editor refused: the plugin still navigates */ }
    }

    private async Task<JsonElement> CallAsync(string method, string paramsJson, CancellationToken ct = default) =>
        (await Rpc.RequestAsync(method, Encoding.UTF8.GetBytes(paramsJson), null, ct).ConfigureAwait(false)).Clone();

    private static string Quote(string text) => LspServer.Encode(text);

    private async Task NotifyAsync(NoticeLevel level, string key, params (string, string)[] arguments)
    {
        try { await CallAsync(UiMethods.Notify, $"{{\"level\":\"{level.ToString().ToLowerInvariant()}\",\"message\":\"{Quote(Texts.Get(_ui, key, arguments))}\"}}").ConfigureAwait(false); }
        catch (Exception e) when (e is RpcException or IOException) { /* a notice that cannot be shown must not break the work */ }
    }

    private async Task<byte[]> TellAsync(string status, NoticeLevel level, string key, params (string, string)[] arguments)
    {
        await NotifyAsync(level, key, arguments).ConfigureAwait(false);
        return Status(status);
    }

    /// <summary>The status bar item: whether the server the last command was for (its language and project) was not found, runs, or is off (stopped, crashed, not startable).</summary>
    private async Task ShowStateAsync(Slot slot)
    {
        var key = slot.State switch { State.Running => "statusRunning", State.NotFound => "statusNotFound", _ => "statusStopped" };
        try
        {
            await CallAsync(UiMethods.StatusSet,
                $"{{\"text\":\"{Quote(Texts.Get(_ui, key))}\",\"tooltip\":\"{Quote(Texts.Get(_ui, "statusTip", ("language", slot.Language), ("root", slot.Root)))}\"}}").ConfigureAwait(false);
        }
        catch (Exception e) when (e is RpcException or IOException) { /* a status that cannot be shown must not break the work */ }
    }

    private static byte[] Status(string status) => Encoding.UTF8.GetBytes($"{{\"status\":\"{status}\"}}");

    private static string? Text(JsonElement p, string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private sealed record Request(string Command, string Document, long Revision, long Offset, string Language);

    // ---- commands ----

    private async ValueTask<byte[]?> ExecuteAsync(JsonElement p, CancellationToken ct)
    {
        var r = new Request(p.GetProperty("command").GetString()!, p.GetProperty("document").GetString()!, p.GetProperty("revision").GetInt64(),
            p.TryGetProperty("offset", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt64() : 0, Text(p, "lspLanguage") ?? "");
        if (r.Command is not (StartCommand or DefinitionCommand or ReferencesCommand or RestartCommand or StopCommand or RenameCommand)) throw new RpcException(RpcErrorCodes.InvalidParams, "Unknown command.");
        _ui = Text(p, "language") ?? "en";
        if (r.Language.Length == 0) return await TellAsync("noLanguage", NoticeLevel.Warning, "noLanguage").ConfigureAwait(false);
        try
        {
            var declared = p.TryGetProperty("lspServer", out var launch) && launch.ValueKind == JsonValueKind.Object ? ServerCatalog.ParseLaunch(launch) with { Language = r.Language } : null;
            var doc = await TrackAsync(r.Language, declared, r.Document, ct).ConfigureAwait(false);
            var slot = doc.Slot;
            if (r.Command == StopCommand)
            {
                await StopServerAsync(slot).ConfigureAwait(false);
                await ShowStateAsync(slot).ConfigureAwait(false);
                return await TellAsync("stopped", NoticeLevel.Info, "stopped", ("language", slot.Language)).ConfigureAwait(false);
            }
            await EnsureSizeAsync(doc, ct).ConfigureAwait(false); // a file that is too large never starts a server
            if (await EnsureServerAsync(slot, r.Command == RestartCommand, ct).ConfigureAwait(false) is { } failure) return Status(failure);
            if (slot.Server is not { } server) return Status("crashed");
            try
            {
                await SyncAsync(doc, ct).ConfigureAwait(false);
            }
            catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict) { return await TellAsync("stale", NoticeLevel.Warning, "stale").ConfigureAwait(false); }
            if (doc.Revision != r.Revision) return await TellAsync("stale", NoticeLevel.Warning, "stale").ConfigureAwait(false);
            return r.Command switch
            {
                DefinitionCommand => await NavigateAsync(slot, server, doc, r, references: false, ct).ConfigureAwait(false),
                ReferencesCommand => await NavigateAsync(slot, server, doc, r, references: true, ct).ConfigureAwait(false),
                RenameCommand => await RenameAsync(server, doc, r, ct).ConfigureAwait(false),
                RestartCommand => await TellAsync("restarted", NoticeLevel.Info, "restarted", ("language", slot.Language), ("count", slot.Docs.Count.ToString())).ConfigureAwait(false),
                _ => Status("ok"),
            };
        }
        catch (Withheld w)
        {
            return w.Message switch
            {
                "tooLarge" => await TellAsync("tooLarge", NoticeLevel.Warning, "tooLarge", ("max", _settings.MaxBytes.ToString())).ConfigureAwait(false),
                "unreadable" => await TellAsync("unreadable", NoticeLevel.Warning, "unreadable").ConfigureAwait(false),
                _ => await TellAsync("invalidBytes", NoticeLevel.Warning, "invalidBytes").ConfigureAwait(false),
            };
        }
        catch (Exception e) when (e is ServerGone or RpcConnectionClosedException) { return Status("crashed"); } // the end of the server is reported by its own handler
        catch (RpcTimeoutException) { return await TellAsync("timeout", NoticeLevel.Warning, "timeout").ConfigureAwait(false); }
        catch (RpcException e) when (e.Code == DocumentErrorCodes.RevisionConflict) { return await TellAsync("stale", NoticeLevel.Warning, "stale").ConfigureAwait(false); }
        catch (RpcException e) when (e.Code is DocumentErrorCodes.UnknownDocument or DocumentErrorCodes.InvalidRange or DocumentErrorCodes.PermissionDenied or RpcErrorCodes.InvalidParams)
        {
            return await TellAsync("failed", NoticeLevel.Error, "failed", ("reason", e.Message)).ConfigureAwait(false);
        }
        catch (RpcException e) { return await TellAsync("failed", NoticeLevel.Error, "failed", ("reason", e.Message)).ConfigureAwait(false); } // an error the server answered with
    }

    // ---- documents ----

    /// <summary>The slot for documents of <paramref name="language"/> at <paramref name="path"/>: one per language, project root and server configuration, so two projects never share a process.</summary>
    private Slot SlotFor(string language, string? path, ServerLaunch? declared)
    {
        var folder = path is null ? null : Path.GetDirectoryName(Path.GetFullPath(path));
        var root = ProjectRoots.Find(folder, language, _settings.RootSearchLevels) ?? folder ?? Directory.GetCurrentDirectory();
        var key = string.Join('\0', language, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? root.ToUpperInvariant() : root, declared?.Fingerprint ?? "");
        return _slots.GetOrAdd(key, _ => new Slot(language, root, declared));
    }

    /// <summary>The document, followed from now on. A document that is already followed moves to the slot a command with another language or server configuration asks for.</summary>
    private async Task<Doc> TrackAsync(string language, ServerLaunch? declared, string id, CancellationToken ct)
    {
        var guid = Guid.Parse(id);
        if (_docs.TryGetValue(guid, out var known))
        {
            var target = SlotFor(language, known.Path, declared);
            if (target != known.Slot) await MoveAsync(known, target).ConfigureAwait(false);
            return known;
        }
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{id}\"}}", ct).ConfigureAwait(false);
        var path = info.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var slot = SlotFor(language, path, declared);
        var doc = new Doc(guid, slot, path is null ? "untitled:" + guid.ToString("N") : PathUri.FromPath(path), path);
        await CallAsync(DocumentMethods.Subscribe, $"{{\"document\":\"{id}\"}}", ct).ConfigureAwait(false);
        _docs[guid] = doc;
        slot.Docs[guid] = doc;
        Interlocked.Increment(ref slot.IdleTicket);
        return doc;
    }

    /// <summary>Takes the document away from its server (it is closed there and its diagnostics go) and hands it to <paramref name="target"/>, which gets it with the next synchronisation.</summary>
    private async Task MoveAsync(Doc d, Slot target)
    {
        var previous = d.Slot;
        await d.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (previous.Server is { Alive: true } server) await CloseAsync(d, server).ConfigureAwait(false);
            d.Open = false;
            previous.Docs.TryRemove(d.Id, out _);
            d.Slot = target;
            target.Docs[d.Id] = d;
            Interlocked.Increment(ref target.IdleTicket);
        }
        finally { d.Gate.Release(); }
        StopWhenIdle(previous);
    }

    private async Task EnsureSizeAsync(Doc d, CancellationToken ct)
    {
        var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{d.Id}\"}}", ct).ConfigureAwait(false);
        if (info.GetProperty("byteLength").GetInt64() > _settings.MaxBytes) throw new Withheld("tooLarge");
    }

    /// <summary>
    /// Brings the server to the text the editor has now: <c>didOpen</c> for a document it does not know, otherwise one <c>didChange</c> (a range, or the whole text for a server that
    /// wants that) with the revision as the version. The whole text is read for each synchronisation; a revision that moved meanwhile is a conflict and the next event repeats it.
    /// </summary>
    private async Task SyncAsync(Doc d, CancellationToken ct)
    {
        await d.Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (d.Slot.Server is not { Alive: true } server) throw new ServerGone();
            var info = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{d.Id}\"}}", ct).ConfigureAwait(false);
            long revision = info.GetProperty("revision").GetInt64(), length = info.GetProperty("byteLength").GetInt64();
            if (length > _settings.MaxBytes)
            {
                await CloseAsync(d, server).ConfigureAwait(false);
                throw new Withheld("tooLarge");
            }
            if (d.Open && d.Revision == revision) return;
            var codec = TextCodec.ByName(info.GetProperty("codec").GetString()!) ?? TextCodec.Utf8;
            int bom = (int)info.GetProperty("bomLength").GetInt64();
            var read = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{d.Id}\",\"revision\":{revision},\"start\":{bom},\"end\":{length}}}", ct).ConfigureAwait(false);
            if (read.GetProperty("invalid").GetArrayLength() > 0)
            {
                await CloseAsync(d, server).ConfigureAwait(false);
                throw new Withheld("invalidBytes");
            }
            var text = read.GetProperty("text").GetString()!;
            if (!d.Open)
            {
                await server.NotifyAsync("textDocument/didOpen",
                    $"{{\"textDocument\":{{\"uri\":\"{Quote(d.Uri)}\",\"languageId\":\"{Quote(d.Slot.Language)}\",\"version\":{revision},\"text\":\"{Quote(text)}\"}}}}").ConfigureAwait(false);
                d.Version = revision;
                d.Open = true;
            }
            else if (TextChange.Between(d.Text, text) is { } change)
            {
                string edit;
                if (server.Incremental)
                {
                    var map = new LineMap(d.Text);
                    var (startLine, startCharacter) = map.PositionOf(change.Start);
                    var (endLine, endCharacter) = map.PositionOf(change.OldEnd);
                    edit = $"{{\"range\":{{\"start\":{{\"line\":{startLine},\"character\":{startCharacter}}},\"end\":{{\"line\":{endLine},\"character\":{endCharacter}}}}},\"text\":\"{Quote(change.Inserted)}\"}}";
                }
                else edit = $"{{\"text\":\"{Quote(text)}\"}}";
                await server.NotifyAsync("textDocument/didChange", $"{{\"textDocument\":{{\"uri\":\"{Quote(d.Uri)}\",\"version\":{revision}}},\"contentChanges\":[{edit}]}}").ConfigureAwait(false);
                d.Version = revision;
            }
            d.Text = text;
            d.Revision = revision;
            d.Codec = codec;
            d.Bom = bom;
        }
        finally { d.Gate.Release(); }
    }

    /// <summary>Tells the server the document is closed and takes its diagnostics away. Call with the gate held.</summary>
    private async Task CloseAsync(Doc d, LspServer server)
    {
        if (!d.Open) return;
        d.Open = false;
        d.Text = "";
        d.Revision = -1;
        try { await server.NotifyAsync("textDocument/didClose", $"{{\"textDocument\":{{\"uri\":\"{Quote(d.Uri)}\"}}}}").ConfigureAwait(false); }
        catch (IOException) { }
        await ClearDiagnosticsAsync(d).ConfigureAwait(false);
    }

    private async Task ClearDiagnosticsAsync(Doc d)
    {
        try { await CallAsync(DocumentMethods.DiagnosticsPublish, $"{{\"document\":\"{d.Id}\",\"revision\":{Math.Max(d.Revision, 0)},\"source\":\"{Source}\",\"diagnostics\":[]}}").ConfigureAwait(false); }
        catch (Exception e) when (e is RpcException or IOException) { /* the document is gone or the editor is: nothing is left to take away */ }
    }

    /// <summary>An event of a document the plugin follows. Events carry no text; the document is read again, so a lost event or a burst of them costs nothing more.</summary>
    protected override ValueTask OnEventAsync(JsonElement eventPayload)
    {
        if (!eventPayload.TryGetProperty("name", out var name) || !eventPayload.TryGetProperty("payload", out var payload)
            || payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("document", out var id) || !Guid.TryParse(id.GetString(), out var guid)
            || !_docs.TryGetValue(guid, out var doc))
            return ValueTask.CompletedTask;
        switch (name.GetString())
        {
            case DocumentEvents.Changed or DocumentEvents.EventsLost:
                Kick(doc);
                break;
            case DocumentEvents.Withdrawn:
                _ = ForgetAsync(doc);
                break;
        }
        return ValueTask.CompletedTask;
    }

    private void Kick(Doc d)
    {
        lock (d)
        {
            d.Dirty = true;
            if (d.Pumping) return;
            d.Pumping = true;
        }
        _ = Task.Run(() => PumpAsync(d));
    }

    private async Task PumpAsync(Doc d)
    {
        while (true)
        {
            lock (d)
            {
                if (!d.Dirty)
                {
                    d.Pumping = false;
                    return;
                }
                d.Dirty = false;
            }
            try { await SyncAsync(d, CancellationToken.None).ConfigureAwait(false); }
            catch (RpcException e) when (e.Code == DocumentErrorCodes.UnknownDocument) { await ForgetAsync(d).ConfigureAwait(false); }
            catch (Exception e) when (e is RpcException or Withheld or ServerGone or IOException) { /* a conflict repeats with the next event; the rest is reported by commands */ }
        }
    }

    private async Task ForgetAsync(Doc d)
    {
        _docs.TryRemove(d.Id, out _);
        d.Slot.Docs.TryRemove(d.Id, out _);
        await d.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (d.Open && d.Slot.Server is { Alive: true } server)
            {
                d.Open = false;
                try { await server.NotifyAsync("textDocument/didClose", $"{{\"textDocument\":{{\"uri\":\"{Quote(d.Uri)}\"}}}}").ConfigureAwait(false); }
                catch (IOException) { }
            }
        }
        finally { d.Gate.Release(); }
        StopWhenIdle(d.Slot);
    }

    /// <summary>Ends the server of a slot that has had no document for <see cref="LspSettings.IdleStopMs"/>; a document that joins meanwhile cancels it.</summary>
    private void StopWhenIdle(Slot slot)
    {
        if (_settings.IdleStopMs <= 0 || !slot.Docs.IsEmpty) return;
        int ticket = Interlocked.Increment(ref slot.IdleTicket);
        _ = Task.Run(async () =>
        {
            await Task.Delay(_settings.IdleStopMs).ConfigureAwait(false);
            await slot.Lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (Volatile.Read(ref slot.IdleTicket) != ticket || slot.Server is null) return;
                await StopServerCoreAsync(slot).ConfigureAwait(false);
                await ShowStateAsync(slot).ConfigureAwait(false);
            }
            finally { slot.Lifecycle.Release(); }
        });
    }

    // ---- the server ----

    /// <summary>Starts the server of the slot when none runs (or restarts it) and hands it every document of the slot. Null when it runs, otherwise the status to answer with.</summary>
    private async Task<string?> EnsureServerAsync(Slot slot, bool restart, CancellationToken ct)
    {
        await slot.Lifecycle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (restart) await StopServerCoreAsync(slot).ConfigureAwait(false);
            if (slot.Server is { Alive: true }) return null;
            bool found;
            var launch = slot.Declared is { } declared ? ServerCatalog.Locate(declared, out found) : _catalog.Resolve(slot.Language, out found);
            if (launch is null || !found)
            {
                slot.State = State.NotFound;
                await ShowStateAsync(slot).ConfigureAwait(false);
                await NotifyAsync(NoticeLevel.Warning, launch is null ? "noLanguage" : "noServer", ("language", slot.Language), ("program", launch?.Program ?? "")).ConfigureAwait(false);
                return launch is null ? "noLanguage" : "noServer";
            }
            LspServer server;
            try { server = await LspServer.StartAsync(launch, slot.Root, _settings, (method, p) => OnServerNotificationAsync(slot, method, p), ct).ConfigureAwait(false); }
            catch (ServerStartException e)
            {
                slot.State = State.Crashed;
                await ShowStateAsync(slot).ConfigureAwait(false);
                await NotifyAsync(NoticeLevel.Error, "startFailed", ("language", slot.Language), ("reason", e.Message)).ConfigureAwait(false);
                return "startFailed";
            }
            slot.Server = server;
            slot.State = State.Running;
            await ShowStateAsync(slot).ConfigureAwait(false);
            server.Ended += detail => _ = OnEndedAsync(slot, server, detail);
            if (!server.Alive) return "crashed";
            // a server that was not there when the documents changed must get all of them again, as they are now
            foreach (var d in slot.Docs.Values) d.Open = false;
            foreach (var d in slot.Docs.Values.ToList())
            {
                try { await SyncAsync(d, ct).ConfigureAwait(false); }
                catch (RpcException e) when (e.Code == DocumentErrorCodes.UnknownDocument) { await ForgetAsync(d).ConfigureAwait(false); }
                catch (Exception e) when (e is RpcException or Withheld) { /* the document is synchronised by its next event, or reported by the command that asks for it */ }
                catch (Exception e) when (e is ServerGone or IOException) { return "crashed"; }
            }
            return null;
        }
        finally { slot.Lifecycle.Release(); }
    }

    private async Task StopServerAsync(Slot slot)
    {
        await slot.Lifecycle.WaitAsync().ConfigureAwait(false);
        try { await StopServerCoreAsync(slot).ConfigureAwait(false); }
        finally { slot.Lifecycle.Release(); }
    }

    private async Task StopServerCoreAsync(Slot slot)
    {
        var server = Interlocked.Exchange(ref slot.Server, null);
        slot.State = State.Stopped;
        foreach (var d in slot.Docs.Values)
        {
            d.Open = false;
            await ClearDiagnosticsAsync(d).ConfigureAwait(false);
        }
        if (server is not null) await server.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Ends every server: the editor is closing the plugin.</summary>
    public async Task StopAllAsync()
    {
        foreach (var slot in _slots.Values)
            if (Interlocked.Exchange(ref slot.Server, null) is { } server) await server.StopAsync().ConfigureAwait(false);
    }

    /// <summary>The server ended by itself: its diagnostics are no longer true, the user is told, and the documents wait to be handed to the next server.</summary>
    private async Task OnEndedAsync(Slot slot, LspServer server, string detail)
    {
        if (Interlocked.CompareExchange(ref slot.Server, null, server) != server) return;
        slot.State = State.Crashed;
        await ShowStateAsync(slot).ConfigureAwait(false);
        foreach (var d in slot.Docs.Values)
        {
            d.Open = false;
            await ClearDiagnosticsAsync(d).ConfigureAwait(false);
        }
        await NotifyAsync(NoticeLevel.Warning, "crashed", ("language", slot.Language), ("detail", detail)).ConfigureAwait(false);
    }

    // ---- diagnostics ----

    private ValueTask OnServerNotificationAsync(Slot slot, string method, JsonElement p) =>
        method == "textDocument/publishDiagnostics" ? new ValueTask(PublishAsync(slot, p)) : ValueTask.CompletedTask;

    private static DiagnosticSeverityName Severity(JsonElement item) =>
        item.TryGetProperty("severity", out var s) && s.ValueKind == JsonValueKind.Number
            ? s.GetInt32() switch { 2 => DiagnosticSeverityName.Warning, 3 => DiagnosticSeverityName.Information, 4 => DiagnosticSeverityName.Hint, _ => DiagnosticSeverityName.Error }
            : DiagnosticSeverityName.Error;

    private enum DiagnosticSeverityName { Error, Warning, Information, Hint }

    private string Limited(string text)
    {
        if (text.Length == 0) return Texts.Get(_ui, "noMessage");
        if (text.Length <= _settings.MaxMessageChars) return text;
        int cut = Math.Max(_settings.MaxMessageChars - 1, 0);
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    /// <summary>Turns <c>publishDiagnostics</c> into a publication for the revision the server's text was read at; a message for a version the server has left behind is ignored.</summary>
    private async Task PublishAsync(Slot slot, JsonElement p)
    {
        var uri = p.GetProperty("uri").GetString()!;
        var local = PathUri.ToPath(uri);
        var d = slot.Docs.Values.FirstOrDefault(x => x.Uri == uri || (x.Path is not null && local is not null && PathUri.SamePath(local, x.Path)));
        if (d is null) return;
        await d.Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!d.Open || (p.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number && v.GetInt64() != d.Version)) return;
            var map = new LineMap(d.Text);
            var items = new List<(int Start, int End, DiagnosticSeverityName Severity, string Message, string? Code)>();
            foreach (var item in p.GetProperty("diagnostics").EnumerateArray())
            {
                if (items.Count >= _settings.MaxDiagnostics) break;
                var range = item.GetProperty("range");
                int start = map.OffsetOf(range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32());
                int end = map.OffsetOf(range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32());
                string? code = item.TryGetProperty("code", out var c) ? c.ValueKind == JsonValueKind.String ? c.GetString() : c.ValueKind == JsonValueKind.Number ? c.GetRawText() : null : null;
                items.Add((start, Math.Max(start, end), Severity(item), Limited(item.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString()! : ""), code));
            }
            var bytes = ByteMap.ToBytes(d.Codec, d.Bom, d.Text, items.SelectMany(i => new[] { i.Start, i.End }));
            var json = new StringBuilder($"{{\"document\":\"{d.Id}\",\"revision\":{d.Revision},\"source\":\"{Source}\",\"diagnostics\":[");
            for (int n = 0; n < items.Count; n++)
            {
                var item = items[n];
                if (n > 0) json.Append(',');
                json.Append($"{{\"start\":{bytes[item.Start]},\"end\":{bytes[item.End]},\"severity\":\"{item.Severity.ToString().ToLowerInvariant()}\",\"message\":\"{Quote(item.Message)}\"");
                if (!string.IsNullOrEmpty(item.Code)) json.Append($",\"code\":\"{Quote(Limited(item.Code))}\"");
                json.Append('}');
            }
            json.Append("]}");
            await CallAsync(DocumentMethods.DiagnosticsPublish, json.ToString()).ConfigureAwait(false);
        }
        catch (Exception e) when (e is RpcException or IOException or KeyNotFoundException or InvalidOperationException) { /* the document moved on or is gone; the next publication replaces this one */ }
        finally { d.Gate.Release(); }
    }

    // ---- definition and references ----

    private sealed record Place(string Path, int Line, int Character, int EndLine, int EndCharacter, long Start, long End, bool InDocument, string LineText);

    private sealed record Loaded(string Text, LineMap Map, TextCodec Codec, int Bom, string? Sha = null);

    /// <summary>The places of a <c>Location</c>, <c>Location[]</c> or <c>LocationLink[]</c> answer as (uri, start line, start column, end line, end column).</summary>
    private static List<(string Uri, int Line, int Character, int EndLine, int EndCharacter)> Locations(JsonElement answer)
    {
        var list = new List<(string, int, int, int, int)>();
        void Add(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object) return;
            string? uri;
            JsonElement range;
            if (item.TryGetProperty("targetUri", out var target))
            {
                uri = target.GetString();
                range = item.TryGetProperty("targetSelectionRange", out var selection) ? selection : item.GetProperty("targetRange");
            }
            else
            {
                uri = Text(item, "uri");
                range = item.GetProperty("range");
            }
            if (uri is null) return;
            var s = range.GetProperty("start");
            var e = range.GetProperty("end");
            list.Add((uri, s.GetProperty("line").GetInt32(), s.GetProperty("character").GetInt32(), e.GetProperty("line").GetInt32(), e.GetProperty("character").GetInt32()));
        }
        if (answer.ValueKind == JsonValueKind.Array) foreach (var item in answer.EnumerateArray()) Add(item);
        else Add(answer);
        return list;
    }

    /// <summary>The file a place is in: the text the plugin holds for a document it follows, otherwise the file on disk read as UTF-8 (<paramref name="strict"/>: null for bytes that are not valid UTF-8); null when it cannot be read or is larger than the limit.</summary>
    private Loaded? Load(string path, bool strict = false)
    {
        foreach (var d in _docs.Values)
            if (d.Path is not null && PathUri.SamePath(d.Path, path) && d.Open) return new Loaded(d.Text, new LineMap(d.Text), d.Codec, d.Bom);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > _settings.MaxBytes) return null;
            var bytes = File.ReadAllBytes(path);
            int bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            var text = new UTF8Encoding(false, strict).GetString(bytes, bom, bytes.Length - bom);
            return new Loaded(text, new LineMap(text), TextCodec.Utf8, bom, Convert.ToHexString(SHA256.HashData(bytes)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException) { return null; }
    }

    private List<Place> Resolve(Doc current, List<(string Uri, int Line, int Character, int EndLine, int EndCharacter)> raw)
    {
        var loaded = new Dictionary<string, Loaded?>();
        var offsets = new Dictionary<string, List<(int Start, int End)>>();
        var kept = new List<(string Path, int Line, int Character, int EndLine, int EndCharacter, int Start, int End)>();
        foreach (var (uri, line, character, endLine, endCharacter) in raw)
        {
            var path = PathUri.ToPath(uri) ?? (uri == current.Uri ? current.Path : null);
            if (path is null) continue;
            if (!loaded.TryGetValue(path, out var file)) loaded[path] = file = Load(path);
            int start = file?.Map.OffsetOf(line, character) ?? -1, end = file?.Map.OffsetOf(endLine, endCharacter) ?? -1;
            kept.Add((path, line, character, endLine, endCharacter, start, Math.Max(start, end)));
            if (file is null) continue;
            if (!offsets.TryGetValue(path, out var list)) offsets[path] = list = [];
            list.Add((start, Math.Max(start, end)));
        }
        var bytes = offsets.ToDictionary(x => x.Key, x => ByteMap.ToBytes(loaded[x.Key]!.Codec, loaded[x.Key]!.Bom, loaded[x.Key]!.Text, x.Value.SelectMany(o => new[] { o.Start, o.End })));
        var places = new List<Place>();
        foreach (var k in kept)
        {
            var file = loaded[k.Path];
            bool inDocument = current.Path is not null && PathUri.SamePath(k.Path, current.Path);
            places.Add(file is null
                ? new Place(k.Path, k.Line, k.Character, k.EndLine, k.EndCharacter, -1, -1, inDocument, "")
                : new Place(k.Path, k.Line, k.Character, k.EndLine, k.EndCharacter, bytes[k.Path][k.Start], bytes[k.Path][k.End], inDocument, file.Map.LineText(k.Line).Trim()));
        }
        return places;
    }

    /// <summary>The LSP <c>textDocument</c> and <c>position</c> members for a byte offset of <paramref name="revision"/>, and the character offset in the text the server holds.</summary>
    private async Task<(string Json, int Caret)> PositionAtAsync(Doc d, long revision, long offset, CancellationToken ct)
    {
        var before = await CallAsync(DocumentMethods.ReadText, $"{{\"document\":\"{d.Id}\",\"revision\":{revision},\"start\":{d.Bom},\"end\":{Math.Max(offset, d.Bom)}}}", ct).ConfigureAwait(false);
        int caret = before.GetProperty("text").GetString()!.Length;
        var (line, character) = new LineMap(d.Text).PositionOf(caret);
        return ($"\"textDocument\":{{\"uri\":\"{Quote(d.Uri)}\"}},\"position\":{{\"line\":{line},\"character\":{character}}}", caret);
    }

    private async Task<byte[]> NavigateAsync(Slot slot, LspServer server, Doc d, Request r, bool references, CancellationToken ct)
    {
        if (!(references ? server.References : server.Definition)) return await TellAsync("notSupported", NoticeLevel.Info, "notSupported").ConfigureAwait(false);
        var (position, _) = await PositionAtAsync(d, r.Revision, r.Offset, ct).ConfigureAwait(false);
        var answer = await server.RequestAsync(references ? "textDocument/references" : "textDocument/definition",
            references ? $"{{{position},\"context\":{{\"includeDeclaration\":true}}}}" : $"{{{position}}}", _settings.RequestTimeoutMs, ct).ConfigureAwait(false);
        var raw = Locations(answer);
        if (raw.Count == 0) return await TellAsync("none", NoticeLevel.Info, references ? "noReferences" : "noDefinition").ConfigureAwait(false);
        bool truncated = raw.Count > _settings.MaxLocations;
        var places = Resolve(d, truncated ? raw.Take(_settings.MaxLocations).ToList() : raw);
        var now = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{d.Id}\"}}", ct).ConfigureAwait(false);
        if (now.GetProperty("revision").GetInt64() != r.Revision || d.Revision != r.Revision) return await TellAsync("stale", NoticeLevel.Warning, "stale").ConfigureAwait(false);

        bool revealed = false;
        if (places.Count == 1 && places[0].InDocument && places[0].Start >= 0)
        {
            try
            {
                await CallAsync(DocumentMethods.Reveal, $"{{\"document\":\"{d.Id}\",\"revision\":{r.Revision},\"start\":{places[0].Start},\"end\":{places[0].End}}}", ct).ConfigureAwait(false);
                revealed = true;
            }
            catch (RpcException) { /* no text view to show it in: the answer still names the place */ }
        }
        else if (places.Count > 1)
        {
            var items = string.Join(',', places.Select(x =>
                $"{{\"id\":\"{Quote($"{x.Line}:{x.Character}:{x.EndLine}:{x.EndCharacter}|{x.Path}")}\",\"label\":\"{Quote(Limited($"{x.Path}:{x.Line + 1}:{x.Character + 1}"))}\",\"detail\":\"{Quote(Limited(x.LineText))}\"}}"));
            var title = Texts.Get(_ui, references ? "referencesTitle" : "definitionsTitle", ("count", raw.Count.ToString()));
            await CallAsync(UiMethods.PanelSet, $"{{\"panel\":\"{LocationsPanel}\",\"title\":\"{Quote(title)}\",\"kind\":\"list\",\"items\":[{items}],\"reveal\":true}}", ct).ConfigureAwait(false);
        }
        if (truncated) await NotifyAsync(NoticeLevel.Warning, "truncated", ("max", _settings.MaxLocations.ToString())).ConfigureAwait(false);
        var locations = string.Join(',', places.Select(x =>
            $"{{\"path\":\"{Quote(x.Path)}\",\"line\":{x.Line},\"character\":{x.Character},\"endLine\":{x.EndLine},\"endCharacter\":{x.EndCharacter},\"start\":{x.Start},\"end\":{x.End},\"inDocument\":{(x.InDocument ? "true" : "false")}}}"));
        return Encoding.UTF8.GetBytes($"{{\"status\":\"ok\",\"count\":{raw.Count},\"shown\":{places.Count},\"revealed\":{(revealed ? "true" : "false")},\"locations\":[{locations}]}}");
    }

    // ---- completion ----

    private static readonly string[] KindNames =
    [
        "", "text", "method", "function", "constructor", "field", "variable", "class", "interface", "module", "property", "unit", "value", "enum", "keyword", "snippet",
        "color", "file", "reference", "folder", "enumMember", "constant", "struct", "event", "operator", "typeParameter",
    ];

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static (int Start, int End) WordAround(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        int start = offset, end = offset;
        while (start > 0 && IsWord(text[start - 1])) start--;
        while (end < text.Length && IsWord(text[end])) end++;
        return (start, end);
    }

    private static (int Start, int End)? CharRange(LineMap map, JsonElement range) =>
        range.ValueKind == JsonValueKind.Object && range.TryGetProperty("start", out var s) && range.TryGetProperty("end", out var e)
            ? (map.OffsetOf(s.GetProperty("line").GetInt32(), s.GetProperty("character").GetInt32()), map.OffsetOf(e.GetProperty("line").GetInt32(), e.GetProperty("character").GetInt32()))
            : null;

    /// <summary>
    /// Answers <c>completion/request</c>: the items of the server for the caret, in bytes of the revision they were asked for. A document the plugin does not follow, a server that is not running
    /// or offers no completion, an error, or a document that moved on while the server worked give no items; the editor does not start a server for completion.
    /// </summary>
    private async ValueTask<byte[]?> CompleteAsync(JsonElement p, CancellationToken ct)
    {
        long asked = p.GetProperty("revision").GetInt64();
        byte[] Nothing(long revision) => Encoding.UTF8.GetBytes($"{{\"revision\":{revision},\"items\":[]}}");
        if (!Guid.TryParse(Text(p, "document"), out var id) || !_docs.TryGetValue(id, out var d) || d.Slot.Server is not { Alive: true, Completion: true } server) return Nothing(asked);
        try
        {
            await SyncAsync(d, ct).ConfigureAwait(false);
            string text;
            long revision;
            TextCodec codec;
            int bom;
            await d.Gate.WaitAsync(ct).ConfigureAwait(false);
            try { (text, revision, codec, bom) = (d.Text, d.Revision, d.Codec, d.Bom); }
            finally { d.Gate.Release(); }
            if (revision != asked) return Nothing(revision);
            var (position, caret) = await PositionAtAsync(d, asked, p.GetProperty("offset").GetInt64(), ct).ConfigureAwait(false);
            var answer = await server.RequestAsync("textDocument/completion", $"{{{position},\"context\":{{\"triggerKind\":1}}}}", _settings.RequestTimeoutMs, ct).ConfigureAwait(false);
            var list = answer.ValueKind == JsonValueKind.Array ? answer : answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("items", out var items) ? items : default;
            if (list.ValueKind != JsonValueKind.Array) return Nothing(asked);
            var now = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{d.Id}\"}}", ct).ConfigureAwait(false);
            if (now.GetProperty("revision").GetInt64() != asked) return Nothing(asked); // the answer is for a text that is gone

            var map = new LineMap(text);
            var (prefix, _) = WordAround(text, caret);
            var parsed = new List<(string Label, string? Detail, string? Kind, (int Start, int End) Range, string Text, List<((int Start, int End) Range, string Text)> Extra)>();
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || Text(item, "label") is not { Length: > 0 } label) continue;
                (int Start, int End) range = (prefix, caret);
                string insert = Text(item, "insertText") ?? label;
                if (item.TryGetProperty("textEdit", out var edit) && edit.ValueKind == JsonValueKind.Object)
                {
                    var r = CharRange(map, edit.TryGetProperty("range", out var plain) ? plain : edit.TryGetProperty("insert", out var inserted) ? inserted : default);
                    if (r is null) continue;
                    range = r.Value;
                    insert = Text(edit, "newText") ?? "";
                }
                var extra = new List<((int, int) Range, string Text)>();
                if (item.TryGetProperty("additionalTextEdits", out var more) && more.ValueKind == JsonValueKind.Array)
                    foreach (var e in more.EnumerateArray())
                        if (CharRange(map, e.TryGetProperty("range", out var er) ? er : default) is { } extraRange) extra.Add((extraRange, Text(e, "newText") ?? ""));
                string? kind = item.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.Number && k.GetInt32() is > 0 and var n && n < KindNames.Length ? KindNames[n] : null;
                parsed.Add((Limited(label), Text(item, "detail") is { Length: > 0 } detail ? Limited(detail) : null, kind, range, insert, extra));
            }
            var bytes = ByteMap.ToBytes(codec, bom, text, parsed.SelectMany(i => new[] { i.Range.Start, i.Range.End }.Concat(i.Extra.SelectMany(x => new[] { x.Range.Start, x.Range.End }))));
            var json = new StringBuilder($"{{\"revision\":{asked},\"items\":[");
            for (int i = 0; i < parsed.Count; i++)
            {
                var item = parsed[i];
                if (i > 0) json.Append(',');
                json.Append($"{{\"label\":\"{Quote(item.Label)}\",\"start\":{bytes[item.Range.Start]},\"end\":{bytes[item.Range.End]},\"text\":\"{Quote(item.Text)}\"");
                if (item.Detail is not null) json.Append($",\"detail\":\"{Quote(item.Detail)}\"");
                if (item.Kind is not null) json.Append($",\"kind\":\"{item.Kind}\"");
                json.Append(",\"edits\":[").AppendJoin(',', item.Extra.Select(x => $"{{\"start\":{bytes[x.Range.Start]},\"end\":{bytes[x.Range.End]},\"text\":\"{Quote(x.Text)}\"}}")).Append("]}");
            }
            return Encoding.UTF8.GetBytes(json.Append("]}").ToString());
        }
        catch (Exception e) when (e is RpcException or RpcTimeoutException or ServerGone or Withheld or IOException or KeyNotFoundException or InvalidOperationException) { return Nothing(asked); }
    }

    // ---- rename ----

    private sealed class FileEdits(string uri)
    {
        public readonly string Uri = uri;
        public readonly List<((int Line, int Character, int EndLine, int EndCharacter) Range, string Text)> Edits = [];
    }

    private static string PathOf(string uri) => PathUri.ToPath(uri) ?? throw new Withheld("unreadable");

    /// <summary>
    /// Asks the user for the new name, has the server rename and sends everything it answers (edits of open documents by revision, of other files by their hash, and the creating, renaming
    /// and deleting of files) to the editor as one workspace edit. The editor shows all of it in the change review and applies what the user accepts as one step.
    /// </summary>
    private async Task<byte[]> RenameAsync(LspServer server, Doc d, Request r, CancellationToken ct)
    {
        if (!server.Rename) return await TellAsync("notSupported", NoticeLevel.Info, "notSupported").ConfigureAwait(false);
        var (position, caret) = await PositionAtAsync(d, r.Revision, r.Offset, ct).ConfigureAwait(false);
        var (wordStart, wordEnd) = WordAround(d.Text, caret);
        var current = d.Text[wordStart..wordEnd];
        var asked = await CallAsync(UiMethods.Input,
            $"{{\"title\":\"{Quote(Texts.Get(_ui, "renameTitle"))}\",\"label\":\"{Quote(Texts.Get(_ui, "renameLabel"))}\",\"value\":\"{Quote(current)}\"}}", ct).ConfigureAwait(false);
        if (asked.GetProperty("value").ValueKind != JsonValueKind.String || asked.GetProperty("value").GetString()!.Trim() is not { Length: > 0 } name || name == current) return Status("cancelled");

        var answer = await server.RequestAsync("textDocument/rename", $"{{{position},\"newName\":\"{Quote(name)}\"}}", _settings.RequestTimeoutMs, ct).ConfigureAwait(false);
        if (answer.ValueKind != JsonValueKind.Object) return await TellAsync("none", NoticeLevel.Info, "noRename").ConfigureAwait(false);
        var now = await CallAsync(DocumentMethods.Info, $"{{\"document\":\"{d.Id}\"}}", ct).ConfigureAwait(false);
        if (now.GetProperty("revision").GetInt64() != r.Revision || d.Revision != r.Revision) return await TellAsync("stale", NoticeLevel.Warning, "stale").ConfigureAwait(false);

        var changes = await ChangesOfAsync(d, answer, ct).ConfigureAwait(false);
        if (changes.Count == 0) return await TellAsync("none", NoticeLevel.Info, "noRename").ConfigureAwait(false);
        var title = Texts.Get(_ui, "renameBatch", ("old", current), ("new", name));
        var decision = await CallAsync(DocumentMethods.WorkspaceEdit, $"{{\"title\":\"{Quote(title)}\",\"changes\":[{string.Join(',', changes)}]}}", ct).ConfigureAwait(false);
        var outcome = decision.GetProperty("decision").GetString()!;
        return Encoding.UTF8.GetBytes($"{{\"status\":\"{(outcome is "applied" or "partial" ? "ok" : outcome)}\",\"decision\":\"{outcome}\",\"accepted\":{decision.GetProperty("accepted").GetInt32()},\"total\":{decision.GetProperty("total").GetInt32()}}}");
    }

    /// <summary>The <c>changes</c> of the workspace edit API for a server's <c>WorkspaceEdit</c> (<c>documentChanges</c> or <c>changes</c>), one entry per file or file operation, in the order the server gave them.</summary>
    private async Task<List<string>> ChangesOfAsync(Doc current, JsonElement edit, CancellationToken ct)
    {
        var order = new List<object>();
        var byUri = new Dictionary<string, FileEdits>();
        void Add(string uri, JsonElement edits)
        {
            if (!byUri.TryGetValue(uri, out var file))
            {
                byUri[uri] = file = new FileEdits(uri);
                order.Add(file);
            }
            foreach (var e in edits.EnumerateArray())
            {
                var range = e.GetProperty("range");
                var s = range.GetProperty("start");
                var en = range.GetProperty("end");
                file.Edits.Add(((s.GetProperty("line").GetInt32(), s.GetProperty("character").GetInt32(), en.GetProperty("line").GetInt32(), en.GetProperty("character").GetInt32()), Text(e, "newText") ?? ""));
            }
        }
        if (edit.TryGetProperty("documentChanges", out var documentChanges) && documentChanges.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in documentChanges.EnumerateArray())
            {
                switch (Text(item, "kind"))
                {
                    case "create":
                        order.Add($"{{\"kind\":\"create\",\"path\":\"{Quote(PathOf(Text(item, "uri") ?? ""))}\",\"text\":\"\"}}");
                        break;
                    case "rename":
                        order.Add($"{{\"kind\":\"rename\",\"path\":\"{Quote(PathOf(Text(item, "oldUri") ?? ""))}\",\"to\":\"{Quote(PathOf(Text(item, "newUri") ?? ""))}\"}}");
                        break;
                    case "delete":
                    {
                        var path = PathOf(Text(item, "uri") ?? "");
                        order.Add($"{{\"kind\":\"delete\",\"path\":\"{Quote(path)}\",\"sha256\":\"{Load(path)?.Sha ?? throw new Withheld("unreadable")}\"}}");
                        break;
                    }
                    default:
                    {
                        var document = item.GetProperty("textDocument");
                        var uri = Text(document, "uri") ?? throw new Withheld("unreadable");
                        // a version other than the one the server was told means the text it renamed in is not the text of the editor
                        var followed = _docs.Values.FirstOrDefault(x => x.Open && x.Uri == uri);
                        if (followed is not null && document.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number && version.GetInt64() != followed.Version)
                            throw new RpcException(DocumentErrorCodes.RevisionConflict, "stale");
                        Add(uri, item.GetProperty("edits"));
                        break;
                    }
                }
            }
        }
        else if (edit.TryGetProperty("changes", out var plain) && plain.ValueKind == JsonValueKind.Object)
            foreach (var file in plain.EnumerateObject()) Add(file.Name, file.Value);

        var result = new List<string>();
        foreach (var item in order)
            result.Add(item is FileEdits file ? await RenderEditsAsync(current, file, ct).ConfigureAwait(false) : (string)item);
        return result;
    }

    private async Task<string> RenderEditsAsync(Doc current, FileEdits file, CancellationToken ct)
    {
        var path = PathUri.ToPath(file.Uri) ?? (file.Uri == current.Uri ? current.Path : null);
        var open = _docs.Values.FirstOrDefault(x => x.Open && (x.Uri == file.Uri || (path is not null && x.Path is not null && PathUri.SamePath(path, x.Path))));
        string text, target;
        TextCodec codec;
        int bom;
        if (open is not null)
        {
            await SyncAsync(open, ct).ConfigureAwait(false);
            await open.Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                (text, codec, bom) = (open.Text, open.Codec, open.Bom);
                target = $"\"kind\":\"edit\",\"document\":\"{open.Id}\",\"revision\":{open.Revision}";
            }
            finally { open.Gate.Release(); }
        }
        else
        {
            if (path is null || Load(path, strict: true) is not { } loaded) throw new Withheld("unreadable");
            (text, codec, bom) = (loaded.Text, loaded.Codec, loaded.Bom);
            target = $"\"kind\":\"edit\",\"path\":\"{Quote(path)}\",\"sha256\":\"{loaded.Sha}\"";
        }
        var map = new LineMap(text);
        var ranges = file.Edits.Select(e => (Start: map.OffsetOf(e.Range.Line, e.Range.Character), End: map.OffsetOf(e.Range.EndLine, e.Range.EndCharacter))).ToList();
        var bytes = ByteMap.ToBytes(codec, bom, text, ranges.SelectMany(x => new[] { x.Start, x.End }));
        var edits = string.Join(',', file.Edits.Select((e, i) => $"{{\"start\":{bytes[ranges[i].Start]},\"end\":{bytes[Math.Max(ranges[i].Start, ranges[i].End)]},\"text\":\"{Quote(e.Text)}\"}}"));
        return $"{{{target},\"edits\":[{edits}]}}";
    }
}
