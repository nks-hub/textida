using System.Text;

namespace NksHub.Textida.Core.Tests;

internal sealed class TempFile : IDisposable
{
    // Own folder per file: saves put their temp file next to the target, and other suites count temp files in the temp root.
    private readonly string _dir = Directory.CreateTempSubdirectory("textida-test-").FullName;

    public string Path { get; }

    public TempFile(byte[]? content = null)
    {
        Path = System.IO.Path.Combine(_dir, "file.bin");
        File.WriteAllBytes(Path, content ?? []);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }
}

internal static class TestDir
{
    /// <summary>
    /// Deletes a folder that held a program a test started or killed: the system keeps the image file of an ended program locked for a moment, so the delete is repeated for a few seconds
    /// (on a loaded machine that moment is long). A folder that is still locked after that is left to the temp cleanup.
    /// </summary>
    public static void Delete(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= 50) return;
                Thread.Sleep(100);
            }
        }
    }
}

internal static class Gen
{
    /// <summary>Text made only of line-break related pieces, so CR, LF, CRLF and empty lines are frequent.</summary>
    public static string LineSoup(Random r, int pieces)
    {
        string[] alphabet = ["a", "b", "\n", "\r\n", "\r", "\n", "\r\n", "", "\n\n", "\r\r", "ěš", "x y"];
        var sb = new StringBuilder();
        for (int i = 0; i < pieces; i++) sb.Append(alphabet[r.Next(alphabet.Length)]);
        return sb.ToString();
    }

    /// <summary>Random string with 1 to 4 byte UTF-8 characters.</summary>
    public static string Mixed(Random r, int chars, bool astral = true)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < chars; i++)
        {
            switch (r.Next(astral ? 5 : 4) + (astral ? 0 : 0))
            {
                case 0: sb.Append((char)('a' + r.Next(26))); break;
                case 1: sb.Append("ěščřžýáíé"[r.Next(9)]); break;
                case 2: sb.Append((char)(0x4E00 + r.Next(500))); break;
                case 3 when astral: sb.Append(char.ConvertFromUtf32(0x1F600 + r.Next(50))); break;
                default: sb.Append(' '); break;
            }
        }
        return sb.ToString();
    }

    public static byte[] RandomBytes(Random r, int n)
    {
        var b = new byte[n];
        r.NextBytes(b);
        return b;
    }
}
