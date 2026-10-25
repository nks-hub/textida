using System.Text;

namespace NksPad.Core.Tests;

internal sealed class TempFile : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nkspad-test-" + Guid.NewGuid().ToString("N") + ".bin");

    public TempFile(byte[]? content = null)
    {
        File.WriteAllBytes(Path, content ?? []);
    }

    public void Dispose()
    {
        try { File.Delete(Path); } catch (IOException) { }
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
