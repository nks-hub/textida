using System.Text;

namespace NksHub.Textida.Tests;

/// <summary>The transcript of the size of the target of P8b.3.3 and P8b.3.5: 1.2 GiB of turns with one tool result of 54 MiB in the middle.</summary>
internal static class TranscriptFile
{
    public const int GiantBytes = 54 << 20;

    private static byte[] Block(out int lines)
    {
        var sb = new StringBuilder();
        sb.Append("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"Question about the build\"}}\n");
        lines = 1;
        string text = new('a', 800);
        for (int i = 0; i < 60; i++)
        {
            sb.Append("{\"type\":\"assistant\",\"message\":{\"id\":\"m").Append(i).Append("\",\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"").Append(text).Append("\"}]}}\n");
            lines++;
        }
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>Writes the file; returns its number of lines and the zero based number of the line with the giant tool result.</summary>
    public static long Write(string path, out long giantLine)
    {
        long target = Math.Max(4L * GiantBytes, (long)(1.2 * (1L << 30) * HugeText.Scale)), written = 0, line = 0; // a dry run with TEXTIDA_HUGE_SCALE shrinks the file but keeps the giant line

        giantLine = -1;
        var block = Block(out int blockLines);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        while (written < target)
        {
            if (giantLine < 0 && written >= target / 2)
            {
                giantLine = line++;
                var head = Encoding.UTF8.GetBytes("{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"tool_use_id\":\"big\",\"type\":\"tool_result\",\"content\":\"");
                var tail = Encoding.UTF8.GetBytes("\"}]}}\n");
                var chunk = new byte[1 << 20];
                Array.Fill(chunk, (byte)'x');
                stream.Write(head);
                for (int sent = 0; sent < GiantBytes; sent += chunk.Length) stream.Write(chunk, 0, Math.Min(chunk.Length, GiantBytes - sent));
                stream.Write(tail);
                written += head.Length + GiantBytes + tail.Length;
            }
            stream.Write(block);
            written += block.Length;
            line += blockLines;
        }
        return line;
    }
}
