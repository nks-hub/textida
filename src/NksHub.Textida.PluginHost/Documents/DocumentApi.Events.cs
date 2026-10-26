using System.Buffers;
using System.Text.Json;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.PluginHost.Documents;

public sealed partial class DocumentApi
{
    private readonly record struct Queued(string Name, Guid Document, byte[] Payload);

    /// <summary>Events waiting for one plugin. Documents whose events were dropped are in <see cref="Lost"/> until the plugin was told.</summary>
    private sealed class Outbox(string plugin)
    {
        public string Plugin { get; } = plugin;
        public readonly object Gate = new();
        public readonly Queue<Queued> Queue = new();
        public readonly HashSet<Guid> Lost = [];
        public readonly SemaphoreSlim Signal = new(0);
        public Task Pump = Task.CompletedTask;
    }

    private readonly Dictionary<string, Outbox> _outboxes = [];
    private readonly CancellationTokenSource _stop = new();

    private Outbox BoxFor(string plugin)
    {
        lock (_gate)
        {
            if (_outboxes.TryGetValue(plugin, out var box)) return box;
            box = new Outbox(plugin);
            _outboxes[plugin] = box;
            box.Pump = Task.Run(() => PumpAsync(box));
            return box;
        }
    }

    private void ClearOutbox(string plugin)
    {
        Outbox? box;
        lock (_gate) _outboxes.TryGetValue(plugin, out box);
        if (box is null) return;
        lock (box.Gate)
        {
            box.Queue.Clear();
            box.Lost.Clear();
        }
    }

    /// <summary>Runs on the thread that changed the document, so it only queues. Ranges and revisions go out, never text.</summary>
    private void OnChanged(Exposed e, ChangeSet change)
    {
        string[] subscribers;
        lock (_gate) subscribers = [.. e.Subscribers];
        if (subscribers.Length == 0) return;
        var payload = Obj(w =>
        {
            w.WriteString("document", e.Doc.Id);
            w.WriteNumber("revision", change.Revision);
            w.WriteString("kind", change.Kind.ToString());
            if (change.Source is { } source) w.WriteString("source", source); else w.WriteNull("source");
            w.WriteStartArray("edits");
            foreach (var s in change.Edits)
            {
                w.WriteStartObject();
                w.WriteNumber("start", s.Start);
                w.WriteNumber("deleted", s.Deleted);
                w.WriteNumber("inserted", s.Inserted);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
        foreach (var plugin in subscribers) Enqueue(plugin, e.Doc.Id, DocumentEvents.Changed, payload);
    }

    /// <summary>A full queue drops the event and marks the document lost; later events of it are dropped too (a gap is never hidden) until the plugin was told.</summary>
    private void Enqueue(string plugin, Guid document, string name, byte[] payload)
    {
        var box = BoxFor(plugin);
        lock (box.Gate)
        {
            if (box.Lost.Contains(document)) return;
            if (box.Queue.Count >= _options.EventQueueCapacity) box.Lost.Add(document);
            else box.Queue.Enqueue(new Queued(name, document, payload));
        }
        box.Signal.Release();
    }

    private async Task PumpAsync(Outbox box)
    {
        try
        {
            while (true)
            {
                await box.Signal.WaitAsync(_stop.Token).ConfigureAwait(false);
                while (await NextAsync(box).ConfigureAwait(false) is { } item) await DeliverAsync(box, item).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>The queued events in order; once they are out, one <see cref="DocumentEvents.EventsLost"/> per lost document with the revision it has now.</summary>
    private async Task<Queued?> NextAsync(Outbox box)
    {
        Guid lost;
        lock (box.Gate)
        {
            if (box.Queue.TryDequeue(out var item)) return item;
            if (box.Lost.Count == 0) return null;
            lost = box.Lost.First();
            box.Lost.Remove(lost);
        }
        Exposed? e;
        lock (_gate) _exposed.TryGetValue(lost, out e);
        if (e is null) return new Queued(DocumentEvents.Withdrawn, lost, Obj(w => w.WriteString("document", lost)));
        var revision = await _owner.InvokeAsync(() => e.Doc.Revision).ConfigureAwait(false);
        return new Queued(DocumentEvents.EventsLost, lost, Obj(w =>
        {
            w.WriteString("document", lost);
            w.WriteNumber("revision", revision);
        }));
    }

    private async Task DeliverAsync(Outbox box, Queued item)
    {
        try { await _send(box.Plugin, item.Name, item.Payload).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw; }
        catch (Exception) when (item.Name == DocumentEvents.Changed)
        {
            // A failing send must not end the pump; the plugin learns about the gap instead.
            lock (box.Gate) box.Lost.Add(item.Document);
            box.Signal.Release();
        }
        catch (Exception) { }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        List<Exposed> exposed;
        List<Outbox> boxes;
        lock (_gate)
        {
            exposed = [.. _exposed.Values];
            boxes = [.. _outboxes.Values];
        }
        foreach (var e in exposed) e.Doc.Changed -= e.Handler;
        await Task.WhenAll(boxes.Select(b => b.Pump)).ConfigureAwait(false);
        _stop.Dispose();
    }
}
