using NksHub.Textida.Core.Files;
using Xunit;

namespace NksHub.Textida.Core.Tests;

public class PrivateDirectoryTests
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [Fact]
    public void A_folder_of_a_fixed_name_in_tmp_is_used_only_when_it_is_a_real_folder_of_this_user_with_mode_0700_and_spill_files_are_owner_only()
    {
        if (!OperatingSystem.IsLinux()) return; // the check and its fallback exist on Linux only, where /tmp is shared
        string name = "textida-private-" + Guid.NewGuid().ToString("N")[..8];
        string fixedPath = Path.Combine(Path.GetTempPath(), name);
        var cleanup = new List<string> { fixedPath };
        try
        {
            string made = PrivateDirectory.Get(name, runtime: false);
            Assert.Equal(fixedPath, made);
            Assert.Equal(OwnerOnly, File.GetUnixFileMode(made));
            Assert.Equal(made, PrivateDirectory.Get(name, runtime: false)); // reused while it stays private

            // made by someone else first: here a folder others can enter, and a link to one
            File.SetUnixFileMode(fixedPath, OwnerOnly | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            string loose = PrivateDirectory.Get(name, runtime: false);
            cleanup.Add(loose);
            Assert.NotEqual(fixedPath, loose);
            Assert.Equal(OwnerOnly, File.GetUnixFileMode(loose));

            string linkName = name + "-link", link = Path.Combine(Path.GetTempPath(), linkName);
            string target = Directory.CreateTempSubdirectory("textida-target-").FullName;
            cleanup.Add(target);
            File.CreateSymbolicLink(link, target);
            try
            {
                string instead = PrivateDirectory.Get(linkName, runtime: false);
                cleanup.Add(instead);
                Assert.NotEqual(link, instead);
            }
            finally { File.Delete(link); }

            var spill = SpillFile.Create(loose, "test");
            try { Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Assert.Single(Directory.GetFiles(loose, "*.spill")))); }
            finally { spill.Dispose(); }
        }
        finally
        {
            foreach (var dir in cleanup) if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
