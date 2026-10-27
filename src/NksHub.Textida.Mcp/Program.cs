using NksHub.Textida.App.Startup;

namespace NksHub.Textida.Mcp;

/// <summary>
/// The program an MCP client starts (stdio transport). It holds no protocol of its own: it joins the client's standard input and output to the running editor over
/// the local pipe, where the MCP server of the editor answers. Bytes are passed through unchanged, so every message is still one line.
/// </summary>
public static class Program
{
    private const int ConnectTimeoutMilliseconds = 3000;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            await Console.Error.WriteLineAsync("usage: textida-mcp   (the channel can be chosen with the TEXTIDA_CHANNEL environment variable)");
            return 2;
        }
        var channel = IpcEndpoint.McpChannel(Environment.GetEnvironmentVariable("TEXTIDA_CHANNEL") is { Length: > 0 } c ? c : "main");
        Stream pipe;
        try { pipe = await IpcEndpoint.ConnectAsync(channel, TimeSpan.FromMilliseconds(ConnectTimeoutMilliseconds), CancellationToken.None); }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            await Console.Error.WriteLineAsync("textida-mcp: Textida is not running or its MCP server is switched off.");
            return 1;
        }

        await using (pipe)
        {
            var toEditor = Pump(Console.OpenStandardInput(), pipe);
            var toClient = Pump(pipe, Console.OpenStandardOutput());
            await Task.WhenAny(toEditor, toClient); // either side closing ends the session: stdin at EOF is the shutdown signal, the pipe closing means the user switched the server off
        }
        return 0;
    }

    private static async Task Pump(Stream from, Stream to)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (true)
            {
                int read = await from.ReadAsync(buffer);
                if (read == 0) return;
                await to.WriteAsync(buffer.AsMemory(0, read));
                await to.FlushAsync();
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { }
    }
}
