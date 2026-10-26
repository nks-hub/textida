using System.Diagnostics;
using System.Threading.Channels;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.Core.Search;

/// <summary>A running search: hits arrive in batches on <see cref="Batches"/>, <see cref="Completion"/> yields the summary. Never throws for a cancelled search.</summary>
public sealed class SearchRun
{
    private readonly CancellationTokenSource _cts;

    internal SearchRun(ChannelReader<SearchBatch> batches, Task<SearchSummary> completion, CancellationTokenSource cts)
    {
        Batches = batches;
        Completion = completion;
        _cts = cts;
    }

    public ChannelReader<SearchBatch> Batches { get; }
    public Task<SearchSummary> Completion { get; }

    /// <summary>Asks the workers to stop; they notice within a read chunk or 256 matches.</summary>
    public void Cancel() => _cts.Cancel();
}

/// <summary>
/// Search over folders and open documents. Files on disk are read chunk by chunk in parallel, each in the encoding detected
/// from its content; open documents are searched in the revision of their snapshot, so unsaved changes count and a later edit cannot
/// invalidate a hit (map <see cref="SearchHit.Range"/> from <see cref="SearchSource.Revision"/> with <see cref="Document.MapRange"/>).
/// Hits of one file keep their order; files arrive in the order the workers finish them.
/// </summary>
public static class SearchWorker
{
    public static SearchRun Start(SearchPattern pattern, SearchScope scope, SearchSettings? settings = null, CancellationToken ct = default)
    {
        settings ??= new();
        var compiled = CompiledPattern.Create(pattern, settings.Engine);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var channel = Channel.CreateUnbounded<SearchBatch>(new UnboundedChannelOptions { SingleReader = true });
        var job = new Job(compiled, scope, settings, channel.Writer, cts);
        return new SearchRun(channel.Reader, Task.Run(job.RunAsync), cts);
    }

    /// <summary>Runs a search to the end and returns every hit; for callers that do not stream.</summary>
    public static async Task<(IReadOnlyList<SearchHit> Hits, SearchSummary Summary)> CollectAsync(SearchPattern pattern, SearchScope scope, SearchSettings? settings = null, CancellationToken ct = default)
    {
        var run = Start(pattern, scope, settings, ct);
        var hits = new List<SearchHit>();
        await foreach (var batch in run.Batches.ReadAllAsync(CancellationToken.None)) hits.AddRange(batch.Hits);
        return (hits, await run.Completion);
    }

    private sealed class Job
    {
        private readonly CompiledPattern _pattern;
        private readonly SearchScope _scope;
        private readonly SearchSettings _s;
        private readonly ChannelWriter<SearchBatch> _out;
        private readonly CancellationTokenSource _cts;
        private readonly FileWalkStats _walk = new();
        private readonly object _gate = new();
        private readonly List<SearchHit> _pending = [];
        private readonly List<string> _errors = [];
        private long _scanned, _matched, _total, _delivered, _binary, _unreadable;
        private int _dropped, _limit;

        public Job(CompiledPattern pattern, SearchScope scope, SearchSettings settings, ChannelWriter<SearchBatch> output, CancellationTokenSource cts)
        {
            _pattern = pattern;
            _scope = scope;
            _s = settings;
            _out = output;
            _cts = cts;
        }

        public async Task<SearchSummary> RunAsync()
        {
            var sw = Stopwatch.StartNew();
            using var timer = new Timer(_ => Flush(), null, _s.BatchInterval, _s.BatchInterval);
            try
            {
                var token = _cts.Token;
                var files = Channel.CreateBounded<FileEntry>(new BoundedChannelOptions(2048) { SingleWriter = true });
                var workers = Enumerable.Range(0, Math.Max(1, _s.Parallelism)).Select(_ => Task.Run(() => ConsumeAsync(files.Reader, token))).ToList();
                workers.Add(Task.Factory.StartNew(() => SearchBuffers(token), TaskCreationOptions.LongRunning));
                workers.Add(Task.Factory.StartNew(() => Produce(files.Writer, token), TaskCreationOptions.LongRunning));
                await Task.WhenAll(workers);
            }
            finally
            {
                timer.Dispose();
                Flush();
                _out.TryComplete();
            }
            var end = Volatile.Read(ref _limit) != 0 ? SearchEnd.LimitReached : _cts.IsCancellationRequested ? SearchEnd.Cancelled : SearchEnd.Completed;
            lock (_gate)
                return new SearchSummary(end, Interlocked.Read(ref _scanned), Interlocked.Read(ref _matched), Interlocked.Read(ref _total), Interlocked.Read(ref _delivered),
                    _dropped != 0, Interlocked.Read(ref _binary), Interlocked.Read(ref _unreadable), _walk, [.. _errors], sw.Elapsed);
        }

        private void Produce(ChannelWriter<FileEntry> writer, CancellationToken ct)
        {
            var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var live = new HashSet<string>((_scope.Buffers ?? []).Where(b => b.Path is not null).Select(b => System.IO.Path.GetFullPath(b.Path!)), comparer);
            try
            {
                foreach (var root in _scope.Roots)
                {
                    if (File.Exists(root))
                    {
                        var info = new FileInfo(root);
                        if (!live.Contains(info.FullName)) writer.WriteAsync(new FileEntry(info.FullName, info.Length, info.LastWriteTimeUtc), ct).AsTask().GetAwaiter().GetResult();
                        continue;
                    }
                    if (!Directory.Exists(root))
                    {
                        Error($"{root}: folder not found");
                        continue;
                    }
                    foreach (var entry in FileWalker.Walk(root, _s.Walk, _walk, ct))
                    {
                        if (live.Contains(entry.Path)) continue;
                        while (!writer.TryWrite(entry))
                        {
                            ct.ThrowIfCancellationRequested();
                            if (!writer.WaitToWriteAsync(ct).AsTask().GetAwaiter().GetResult()) return;
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                writer.TryComplete();
            }
        }

        private async Task ConsumeAsync(ChannelReader<FileEntry> reader, CancellationToken ct)
        {
            try
            {
                await foreach (var entry in reader.ReadAllAsync(ct)) ProcessFile(entry, ct);
            }
            catch (OperationCanceledException) { }
        }

        private void SearchBuffers(CancellationToken ct)
        {
            try
            {
                foreach (var b in _scope.Buffers ?? [])
                {
                    ct.ThrowIfCancellationRequested();
                    var snap = b.Snapshot;
                    var outcome = Run(new SnapshotChunkSource(snap), snap.Codec, b.Title, ct);
                    if (outcome is null) continue;
                    Deliver(new SearchSource(b.Path, snap.DocumentId, snap.Revision, b.Title, snap.Codec, snap.ByteLength, null), outcome);
                }
            }
            catch (OperationCanceledException) { }
        }

        private void ProcessFile(FileEntry entry, CancellationToken ct)
        {
            try
            {
                using var handle = File.OpenHandle(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);
                long length = RandomAccess.GetLength(handle);
                if (length > _s.Walk.MaxFileSize)
                {
                    _walk.AddTooLarge();
                    return;
                }
                var outcome = Run(new FileChunkSource(handle, length), null, entry.Path, ct);
                if (outcome is null) return;
                if (outcome.Binary)
                {
                    Interlocked.Increment(ref _binary);
                    return;
                }
                Deliver(new SearchSource(entry.Path, null, null, entry.Path, outcome.Codec!, length, entry.LastWriteUtc), outcome);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Interlocked.Increment(ref _unreadable);
            }
        }

        private ScanOutcome? Run(IChunkSource source, TextCodec? codec, string name, CancellationToken ct)
        {
            int keep = (int)Math.Max(0, Math.Min(int.MaxValue, _s.MaxResults - Interlocked.Read(ref _delivered)));
            try
            {
                return ContentScanner.Scan(source, _pattern, _s, codec, keep, ct);
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                Error($"{name}: the pattern took too long on this file");
                return null;
            }
        }

        private void Error(string message)
        {
            lock (_gate)
                if (_errors.Count < 100) _errors.Add(message);
        }

        private void Deliver(SearchSource source, ScanOutcome o)
        {
            Interlocked.Increment(ref _scanned);
            if (o.Count == 0) return;
            Interlocked.Increment(ref _matched);
            Interlocked.Add(ref _total, o.Count);
            _s.FileMatched?.Invoke(source, o.Count);

            int take = Reserve(o.Hits.Count);
            if (take < o.Count) Volatile.Write(ref _dropped, 1);
            if (take == 0) return;
            var hits = new List<SearchHit>(take);
            for (int i = 0; i < take; i++)
            {
                var h = o.Hits[i];
                hits.Add(new SearchHit(source, new ByteRange(h.Start, h.Start + h.Length), h.Line, h.Column, h.Preview));
            }
            lock (_gate)
            {
                _pending.AddRange(hits);
                if (_pending.Count >= _s.BatchSize) FlushLocked();
            }
        }

        /// <summary>Claims room for hits under <see cref="SearchSettings.MaxResults"/>; stops the search when the limit is reached and that is configured.</summary>
        private int Reserve(int want)
        {
            while (true)
            {
                long now = Interlocked.Read(ref _delivered);
                int take = (int)Math.Max(0, Math.Min(want, _s.MaxResults - now));
                if (take == 0 && want == 0) return 0;
                if (take > 0 && Interlocked.CompareExchange(ref _delivered, now + take, now) != now) continue;
                if (now + take >= _s.MaxResults && _s.StopAtLimit)
                {
                    Volatile.Write(ref _limit, 1);
                    _cts.Cancel();
                }
                return take;
            }
        }

        private void Flush()
        {
            lock (_gate) FlushLocked();
        }

        private void FlushLocked()
        {
            if (_pending.Count == 0) return;
            _out.TryWrite(new SearchBatch([.. _pending], Interlocked.Read(ref _scanned), Interlocked.Read(ref _total)));
            _pending.Clear();
        }
    }
}
