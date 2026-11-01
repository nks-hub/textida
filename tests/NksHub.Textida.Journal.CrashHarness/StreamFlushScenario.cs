using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Journal;

namespace CrashHarness;

internal static class StreamFlushScenario
{
    public static int Child(string directory, string point)
    {
        using var document = Document.Open(Path.Combine(directory, "data.bin"), PagedBackendFactory.Instance);
        using var journal = JournalSession.Start(Path.Combine(directory, "journal"),
            name => name == "paged" ? PagedBackendFactory.Instance : null,
            new JournalOptions { Fsync = FsyncPolicy.Manual });
        journal.Track(document);
        if (!document.Insert(100, "inserted revision"u8.ToArray(), new() { RawBytes = true }).Applied)
            throw new InvalidOperationException("The crash scenario could not insert its edit.");
        journal.Flush();

        void Hold()
        {
            Console.WriteLine(point);
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }

        document.BeforeStreamFlush = _ => { if (point == "before-flush") Hold(); };
        document.StreamSaveStep = (step, _) =>
        {
            if (step == point || step == "temp-written" && point == "after-flush") Hold();
        };
        document.SaveAsync().GetAwaiter().GetResult();
        return 0;
    }
}
