using NksHub.Textida.Core.Documents;
using NksHub.Textida.Core.Files;
using NksHub.Textida.Document.Contract;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class FileLocksTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-locks-").FullName;
    private string Lockdir => Path.Combine(_dir, "locks");
    private string File1 => Path.Combine(_dir, "doc.txt");

    public FileLocksTests() => File.WriteAllText(File1, "content");

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void FreeFileIsWritableAndHoldsTheEditorLockUntilDisposed()
    {
        var a = new FileLocks(Lockdir, "A");
        var b = new FileLocks(Lockdir, "B");
        using (var mine = a.Acquire(File1))
        {
            Assert.Equal(LockState.Writable, mine.State);
            Assert.False(mine.IsReadOnly);
            using var other = b.Acquire(File1);
            Assert.Equal(LockState.ReadOnlyOtherInstance, other.State);
            Assert.True(other.IsReadOnly);
            Assert.Equal("A", other.Owner!.Instance);
            Assert.Equal(Environment.ProcessId, other.Owner.ProcessId);
        }
        using var afterRelease = b.Acquire(File1);
        Assert.Equal(LockState.Writable, afterRelease.State);
        Assert.Empty(Directory.GetFiles(_dir, "*.lck")); // nothing is written next to the document
    }

    [Fact]
    public void TheSameInstanceOpeningAFileItHoldsStaysWritable()
    {
        var a = new FileLocks(Lockdir, "A");
        using var first = a.Acquire(File1);
        using var second = a.Acquire(File1);
        Assert.Equal(LockState.Writable, second.State);
        second.Dispose(); // only the first result releases
        Assert.Equal(LockState.ReadOnlyOtherInstance, new FileLocks(Lockdir, "B").Acquire(File1).State);
    }

    [Fact]
    public void SharingLockOfAnotherProgramOpensReadOnly()
    {
        var locks = new FileLocks(Lockdir, "A");
        using (new FileStream(File1, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var r = locks.Acquire(File1);
            Assert.Equal(LockState.ReadOnlyLockedByProcess, r.State);
            Assert.True(r.IsReadOnly);
        }
        using (new FileStream(File1, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            using var r = locks.Acquire(File1); // a cooperative writer is no lock
            Assert.Equal(LockState.Writable, r.State);
        }
    }

    [Fact]
    public void LckFileOfAnotherEditorIsIgnoredAndLeftAlone()
    {
        string lck = File1 + ".lck";
        File.WriteAllText(lck, "locked by someone");
        var stamp = File.GetLastWriteTimeUtc(lck);
        using var r = new FileLocks(Lockdir, "A").Acquire(File1);
        Assert.Equal(LockState.Writable, r.State);
        Assert.Equal("locked by someone", File.ReadAllText(lck));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(lck));
    }

    [Fact]
    public void ConcurrentWriteModeIgnoresLocksButEverySaveChecksTheDiskFirst()
    {
        var a = new FileLocks(Lockdir, "A");
        var b = new FileLocks(Lockdir, "B");
        using var holder = a.Acquire(File1);
        using var concurrent = b.Acquire(File1, concurrentWrite: true);
        Assert.Equal(LockState.ConcurrentWrite, concurrent.State);
        Assert.False(concurrent.IsReadOnly);
        Assert.True(concurrent.SaveChecksDiskFirst);

        using var doc = Doc.Open(File1, ReferenceBackendFactory.Instance);
        doc.Insert(doc.ByteLength, "!"u8.ToArray());
        File.WriteAllText(File1, "written by the other instance meanwhile");
        File.SetLastWriteTimeUtc(File1, DateTime.UtcNow.AddMinutes(1));

        var result = doc.Save(overwriteExternalChanges: false);
        Assert.False(result.Saved);
        Assert.NotNull(result.Conflict);
        Assert.Equal("written by the other instance meanwhile", File.ReadAllText(File1));
    }
}
