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
    public async Task A_save_keeps_the_mode_of_the_file_and_a_new_file_gets_the_default_one()
    {
        if (OperatingSystem.IsWindows()) return; // ReplaceFile keeps the ACL; Unix modes only
        var dir = Directory.CreateTempSubdirectory("textida-swap-");
        try
        {
            const UnixFileMode Secret = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            const UnixFileMode Script = Secret | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
            foreach (var mode in new[] { Secret, Script })
            {
                var target = Path.Combine(dir.FullName, $"{(int)mode}.txt");
                File.WriteAllText(target, "old\n");
                File.SetUnixFileMode(target, mode);
                using (var temp = FileSwap.CreateTemp(target + ".probe.tmp", target))
                    Assert.Equal(Secret, File.GetUnixFileMode(temp.Name)); // nobody else reads the content on its way in
                File.Delete(target + ".probe.tmp");

                using var doc = Doc.Open(target, ReferenceBackendFactory.Instance);
                doc.Insert(0, "new "u8.ToArray());
                Assert.True((await doc.SaveAsync()).Saved);
                Assert.Equal("new old\n", File.ReadAllText(target));
                Assert.Equal(mode, File.GetUnixFileMode(target));
            }

            var reference = Path.Combine(dir.FullName, "reference.txt");
            File.WriteAllText(reference, "");
            var fresh = Path.Combine(dir.FullName, "fresh.txt");
            using (var doc = Doc.Open(Path.Combine(dir.FullName, $"{(int)Secret}.txt"), ReferenceBackendFactory.Instance))
                Assert.True(doc.Save(fresh).Saved);
            Assert.Equal(File.GetUnixFileMode(reference), File.GetUnixFileMode(fresh));
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
