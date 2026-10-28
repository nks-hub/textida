using System.Diagnostics;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;
using NksHub.Textida.Core.Tools;

namespace CrashHarness;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "guard-parent") return GuardParent(args[1], tracked: true);
        if (args.Length == 2 && args[0] == "guard-watched-parent") return GuardParent(args[1], tracked: false);
        if (args is ["guard-sleep"])
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }
        if (args is ["guard-watch"]) return GuardWatch();
        if (args.Length >= 3 && args[0] == "child") return Child(args[1], int.Parse(args[2]));
        if (args.Length >= 3 && args[0] == "inplace-child") return InPlaceChild(args[1], args[2]);
        if (args.Length >= 4 && args[0] == "batch-child") return BatchScenario.Child(args[1], int.Parse(args[2]), args[3]);
        if (args.Length >= 6 && args[0] == "large-child") return LargeScenario.Child(args[1], int.Parse(args[2]), args[3], long.Parse(args[4]), int.Parse(args[5]));
        if (args.Length == 2 && args[0] == "dump")
        {
            using var reader = new JournalReader(args[1]);
            while (reader.TryRead(out var rec)) Console.WriteLine($"{rec.Seq} {rec.Type} {rec.Payload.Length}B {(rec.Type is RecordType.DocEdit ? JournalRecordsView.Edit(rec.Payload) : "")}");
            Console.WriteLine($"tail {reader.Tail}");
            return 0;
        }
        int iterations = 200, parallel = 4, seed0 = 1000;
        string? work = null;
        long size = 6L << 20;
        int burst = 0;
        string[]? points = null;
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--iterations": iterations = int.Parse(args[i + 1]); break;
                case "--parallel": parallel = int.Parse(args[i + 1]); break;
                case "--seed": seed0 = int.Parse(args[i + 1]); break;
                case "--work": work = args[i + 1]; break;
                case "--size": size = long.Parse(args[i + 1]); break;
                case "--burst": burst = int.Parse(args[i + 1]); break;
                case "--points": points = args[i + 1].Split(','); break;
            }
        }
        if (args.Length > 0 && args[0] == "batch")
            return BatchScenario.RunAll(iterations == 200 ? 20 : iterations, parallel, seed0, work ?? Path.Combine(Path.GetTempPath(), "nkspad-batch-" + Guid.NewGuid().ToString("N")[..8]));
        if (args.Length > 0 && args[0] == "large")
            return LargeScenario.RunAll(iterations == 200 ? 6 : iterations, parallel, seed0, work ?? Path.Combine(Path.GetTempPath(), "textida-large-" + Guid.NewGuid().ToString("N")[..8]), size, burst, points);
        if (args.Length == 0 || args[0] != "run")
        {
            Console.Error.WriteLine("usage: run|batch|large [--iterations N] [--parallel N] [--seed N] [--work DIR] [--size BYTES] [--burst N] | child DIR SEED | batch-child DIR SEED POINT | large-child DIR SEED POINT SIZE BURST");
            return 2;
        }
        return Verifier.RunAll(iterations, parallel, seed0, work ?? Path.Combine(Path.GetTempPath(), "textida-crash-" + Guid.NewGuid().ToString("N")[..8]));
    }

    /// <summary>
    /// Stand-in for an editor whose children must not outlive it: starts a child that sleeps (<c>guard-sleep</c>, or <c>guard-watch</c> when not <paramref name="tracked"/>), writes its process id to
    /// <paramref name="pidFile"/> and waits to be killed. A tracked child is put under <see cref="ProcessLaunch.Track"/>; the other one only gets the environment of a plugin process that watches its parent.
    /// </summary>
    private static int GuardParent(string pidFile, bool tracked)
    {
        var info = ProcessLaunch.CreateStartInfo(Environment.ProcessPath!, [typeof(Program).Assembly.Location, tracked ? "guard-sleep" : "guard-watch"]);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        if (!tracked)
        {
            ChildProcessGuard.PassTo(info);
            ParentWatch.Apply(info, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
        }
        using var child = Process.Start(info)!;
        if (tracked) ProcessLaunch.Track(child);
        File.WriteAllText(pidFile + ".parent", Environment.ProcessId.ToString());
        File.WriteAllText(pidFile + ".tmp", child.Id.ToString());
        File.Move(pidFile + ".tmp", pidFile);
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /// <summary>A plugin process that ends by itself when its parent is gone: exit code 0 then, 3 when the environment asks it for no watch.</summary>
    private static int GuardWatch()
    {
        if (ParentWatch.FromEnvironment() is not { } watch) return 3;
        watch.WaitUntilGoneAsync(CancellationToken.None).GetAwaiter().GetResult();
        return 0;
    }

    /// <summary>
    /// Overwrites bytes of DIR/data.bin (overlapping and distant ranges), saves in place and waits to be killed at <paramref name="point"/>:
    /// <c>saving</c> (intent durable, nothing written), <c>saved-unflushed</c> (file patched, save record only in memory) or <c>saved-flushed</c>.
    /// Prints the point name when it is reached.
    /// </summary>
    private static int InPlaceChild(string dir, string point)
    {
        using var doc = Document.Open(Path.Combine(dir, "data.bin"), PagedBackendFactory.Instance);
        using var session = JournalSession.Start(Path.Combine(dir, "journal"), Scene.Resolve, new JournalOptions { Fsync = FsyncPolicy.Manual });
        session.Track(doc);
        session.Flush();
        var raw = new EditOptions { RawBytes = true };
        doc.Replace(new ByteRange(100, 108), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, raw);
        doc.Replace(new ByteRange(104, 112), new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 }, raw); // overlaps the first: replaying twice must not change the result
        doc.Replace(new ByteRange(3_000_000, 3_000_004), new byte[] { 0xA, 0xB, 0xC, 0xD }, raw);
        session.Flush();
        void Hold()
        {
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            stdout.WriteLine(point);
            Thread.Sleep(Timeout.Infinite);
        }
        if (point == "saving") doc.Saving += (_, _) => Hold();
        else doc.Saved += (_, _) => { if (point == "saved-flushed") session.Flush(); Hold(); };
        doc.Save();
        return 0;
    }

    /// <summary>Edits three documents with a journal until the parent kills the process. Prints "ready T" and then "OP T" after every operation.</summary>
    private static int Child(string dir, int seed)
    {
        using var scene = Scene.Create(Path.Combine(dir, "data"), seed);
        using var session = JournalSession.Start(Path.Combine(dir, "journal"), Scene.Resolve, new JournalOptions { FlushInterval = TimeSpan.FromMilliseconds(500) });
        var ids = scene.Docs.Select(session.Track).ToArray();
        session.Flush();
        var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        stdout.WriteLine($"ready {JournalFormat.NowMs()}");

        var rng = new Random(seed);
        var timing = new Random(seed * 31 + 7);
        void UpdateSession(int op)
        {
            var pick = new Random(op);
            SessionTab Tab(int d)
            {
                var doc = scene.Docs[d];
                var a = doc.CreateAnchor(doc.ByteLength == 0 ? 0 : pick.NextInt64(0, doc.ByteLength));
                var b = doc.CreateAnchor(Math.Min(doc.ByteLength, a.Offset + pick.Next(10)));
                return SessionTab.Capture(ids[d], doc, d == 0 ? "Untitled 1" : null, [(a, b)]);
            }
            session.UpdateSession(new SessionModel([new([Tab(0), Tab(1)], ids[1]), new([Tab(2)], ids[2])], op % 2));
        }

        var watchdog = Stopwatch.StartNew();
        for (int op = 0; watchdog.Elapsed < TimeSpan.FromMinutes(2); op++)
        {
            scene.RunOp(rng, op, UpdateSession);
            if (!session.IsActive && !File.Exists(Path.Combine(dir, "fault.txt"))) File.WriteAllText(Path.Combine(dir, "fault.txt"), $"op {op}: {session.Fault}");
            stdout.WriteLine($"{op} {JournalFormat.NowMs()}");
            int roll = timing.Next(100);
            if (roll < 85) Thread.Sleep(timing.Next(2));
            else if (roll < 97) Thread.Sleep(5 + timing.Next(35));
            else Thread.Sleep(100 + timing.Next(700));
        }
        return 0;
    }
}

internal static class JournalRecordsView
{
    public static string Edit(byte[] payload)
    {
        var m = typeof(JournalFormat).Assembly.GetType("NksHub.Textida.Core.Journal.JournalRecords")!.GetMethod("DecodeEdit")!;
        var e = (EditRecord)m.Invoke(null, [payload])!;
        return $"doc {e.DocId.ToString()[..4]} rev {e.Revision} {e.Kind} new={e.NewUndoEntry} codec={e.Codec} edits={e.Edits.Count}{(e.List is { } list ? $" list={list.Count} edits in {list.File}" : "")}";
    }
}
