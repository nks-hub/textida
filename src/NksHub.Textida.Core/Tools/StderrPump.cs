namespace NksHub.Textida.Core.Tools;

/// <summary>
/// Reads the standard error of a child process to its end, so the pipe never fills and blocks the child, and keeps the last lines for the report of how the child ended.
/// The one loop behind the plugin host, the agent client, the MCP client and the language server client.
/// </summary>
public static class StderrPump
{
    /// <summary>
    /// Reads <paramref name="reader"/> line by line until the end of the stream, or until the stream is closed or disposed under it (the child ended, the client was disposed).
    /// Each line goes through <paramref name="filter"/> (for example to mask a secret) and is cut to <paramref name="maxLineChars"/>; then it is added to <paramref name="tail"/>, which keeps at most
    /// <paramref name="capacity"/> lines (access is under <c>lock (tail)</c>), and handed to <paramref name="sink"/>. A sink that throws does not stop the reading.
    /// </summary>
    public static async Task RunAsync(TextReader reader, Queue<string> tail, int capacity, Action<string>? sink = null, Func<string, string>? filter = null, int maxLineChars = int.MaxValue)
    {
        try
        {
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } raw)
            {
                var line = filter is null ? raw : filter(raw);
                if (line.Length > maxLineChars) line = line[..maxLineChars];
                lock (tail)
                {
                    tail.Enqueue(line);
                    while (tail.Count > capacity) tail.Dequeue();
                }
                try { sink?.Invoke(line); }
                catch (Exception) { /* a failing log sink must not stop the pump: the pipe would fill and block the child */ }
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
}
