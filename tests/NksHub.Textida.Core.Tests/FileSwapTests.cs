using NksHub.Textida.Core.Documents;
using Xunit;

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
