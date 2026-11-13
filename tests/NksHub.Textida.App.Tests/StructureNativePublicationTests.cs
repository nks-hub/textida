using System.Text;
using NksHub.Textida.App.Shell;

namespace NksHub.Textida.App.Tests;

public sealed class StructureNativePublicationTests
{
    [Fact]
    public void Publishing_keeps_shared_readers_on_their_original_complete_record()
    {
        string directory = Path.Combine(Path.GetTempPath(), "structure-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, "status.tmp"), final = Path.Combine(directory, "status.json");
        try
        {
            string original = "{\"message\":\"město 😀\",\"sequence\":0}";
            File.WriteAllText(temp, original, Encoding.UTF8);
            StructureNativeDiagnostic.PublishFile(temp, final);
            using var held = new FileStream(final, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            for (int sequence = 1; sequence <= 3; sequence++)
            {
                string next = "{\"message\":\"město 😀\",\"sequence\":" + sequence + "}";
                File.WriteAllText(temp, next, Encoding.UTF8);
                StructureNativeDiagnostic.PublishFile(temp, final);
                Assert.Equal(next, File.ReadAllText(final, Encoding.UTF8));
                Assert.False(File.Exists(temp));
                held.Position = 0;
                using var reader = new StreamReader(held, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                Assert.Equal(original, reader.ReadToEnd());
            }
        }
        finally
        {
            File.Delete(temp);
            File.Delete(final);
            Directory.Delete(directory);
        }
    }
}
