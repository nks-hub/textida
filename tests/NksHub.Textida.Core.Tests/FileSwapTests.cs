using NksHub.Textida.Core.Documents;
using NksHub.Textida.Document.Contract;
using Xunit;
using Doc = NksHub.Textida.Core.Documents.Document;

namespace NksHub.Textida.Core.Tests;

public class FileSwapTests
{
    [Fact]
    public void Replace_waits_out_a_short_lock_on_the_target()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("textida-swap-");
        try
        {
            var target = Path.Combine(dir.FullName, "a.txt");
            var temp = Path.Combine(dir.FullName, "a.tmp");
            File.WriteAllText(target, "old");
            File.WriteAllText(temp, "new");
            // A reader without FileShare.Delete blocks File.Replace, as a virus scanner does right after a write.
            var holder = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = Task.Delay(60).ContinueWith(_ => holder.Dispose());

            FileSwap.Replace(temp, target);

            release.Wait();
            Assert.Equal("new", File.ReadAllText(target));
            Assert.False(File.Exists(temp));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Replace_leaves_no_backup_and_no_temp()
    {
        var dir = Directory.CreateTempSubdirectory("textida-swap-");
        try
        {
            var target = Path.Combine(dir.FullName, "a.txt");
            var temp = Path.Combine(dir.FullName, "a.textida-1.tmp");
            File.WriteAllText(target, "old");
            File.WriteAllText(temp, "new");

            FileSwap.Replace(temp, target);

            Assert.Equal("new", File.ReadAllText(target));
            Assert.Equal(["a.txt"], Directory.GetFiles(dir.FullName).Select(Path.GetFileName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_replace_that_died_after_parking_the_target_gets_the_original_back()
    {
        var dir = Directory.CreateTempSubdirectory("textida-swap-");
        try
        {
            var target = Path.Combine(dir.FullName, "a.txt");
            var temp = Path.Combine(dir.FullName, "a.textida-1.tmp");
            // the state ReplaceFile leaves when the process dies between its two renames
            File.WriteAllText(FileSwap.BackupOf(temp), "old");
            File.WriteAllText(temp, "new");

            FileSwap.RecoverInterrupted(temp, target);

            Assert.Equal("old", File.ReadAllText(target));
            Assert.False(File.Exists(FileSwap.BackupOf(temp)));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Opening_a_file_whose_save_died_halfway_brings_back_the_parked_original()
    {
        var dir = Directory.CreateTempSubdirectory("textida-swap-");
        try
        {
            var target = Path.Combine(dir.FullName, "a.txt");
            File.WriteAllText(FileSwap.BackupOf(target + ".textida-1234abcd.tmp"), "saved before");
            File.WriteAllText(target + ".textida-1234abcd.tmp", "half way");
            File.WriteAllText(Path.Combine(dir.FullName, "b.txt.textida-99.old.tmp"), "other file");

            using var doc = Doc.Open(target, ReferenceBackendFactory.Instance);

            Assert.Equal("saved before", File.ReadAllText(target));
            Assert.False(File.Exists(FileSwap.BackupOf(target + ".textida-1234abcd.tmp")));
            Assert.True(File.Exists(Path.Combine(dir.FullName, "b.txt.textida-99.old.tmp")));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void TryDelete_waits_out_a_short_lock()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("textida-swap-");
        try
        {
            var temp = Path.Combine(dir.FullName, "a.tmp");
            File.WriteAllText(temp, "x");
            var holder = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read);
            var release = Task.Delay(60).ContinueWith(_ => holder.Dispose());

            FileSwap.TryDelete(temp);

            release.Wait();
            Assert.False(File.Exists(temp));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Replace_gives_up_on_a_lock_that_does_not_go_away()
    {
        if (!OperatingSystem.IsWindows()) return;
        var dir = Directory.CreateTempSubdirectory("textida-swap-");
        try
        {
            var target = Path.Combine(dir.FullName, "a.txt");
            var temp = Path.Combine(dir.FullName, "a.tmp");
            File.WriteAllText(target, "old");
            File.WriteAllText(temp, "new");
            using (new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => FileSwap.Replace(temp, target));
            Assert.Equal("old", File.ReadAllText(target));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
