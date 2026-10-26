using System.Diagnostics;
using NksPad.Core.Documents;
using NksPad.Core.Journal;

namespace CrashHarness;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "child") return Child(args[1], int.Parse(args[2]));
        int iterations = 200, parallel = 4, seed0 = 1000;
        string? work = null;
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            switch (args[i])
            {
                case "--iterations": iterations = int.Parse(args[i + 1]); break;
                case "--parallel": parallel = int.Parse(args[i + 1]); break;
                case "--seed": seed0 = int.Parse(args[i + 1]); break;
                case "--work": work = args[i + 1]; break;
            }
        }
        if (args.Length == 0 || args[0] != "run")
        {
            Console.Error.WriteLine("usage: run [--iterations N] [--parallel N] [--seed N] [--work DIR] | child DIR SEED");
            return 2;
        }
        return Verifier.RunAll(iterations, parallel, seed0, work ?? Path.Combine(Path.GetTempPath(), "nkspad-crash-" + Guid.NewGuid().ToString("N")[..8]));
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
            stdout.WriteLine($"{op} {JournalFormat.NowMs()}");
            int roll = timing.Next(100);
            if (roll < 85) Thread.Sleep(timing.Next(2));
            else if (roll < 97) Thread.Sleep(5 + timing.Next(35));
            else Thread.Sleep(100 + timing.Next(700));
        }
        return 0;
    }
}
