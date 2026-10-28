using System.Globalization;
using System.Text;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.App.Services;

/// <summary>The purposes output is kept apart by; each has its own file, so one noisy source cannot bury another.</summary>
internal enum OutputChannel { Tasks, Search, Remote }

/// <summary>One line of a channel: when it was written, by which source (a tool name, a search) and the text.</summary>
internal readonly record struct OutputLine(DateTime Time, string Source, string Text);

/// <summary>
/// Output of tasks and searches, one append-only file per channel under the profile. Every line is a single write to the file, so what a
/// source wrote before it crashed (or before the app itself did) is still there on the next read; a half-written last line is shown as plain text.
/// A tool runner appends the lines it reads from its child process here.
/// </summary>
internal sealed class OutputChannels
{
    private const long MaxFileBytes = 4L * 1024 * 1024;
    private const long KeptBytes = 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string _directory;
    private readonly object _gate = new();

    public OutputChannels(string directory)
    {
        _directory = directory;
        foreach (var channel in Enum.GetValues<OutputChannel>()) Trim(PathOf(channel));
    }

    /// <summary>Raised on the writing thread after a line was stored.</summary>
    public event Action<OutputChannel, OutputLine>? Appended;

    /// <summary>Raised after a channel was emptied.</summary>
    public event Action<OutputChannel>? Cleared;

    /// <summary>The folder with the channel files, the log folder of the About window. It exists after the first line was written.</summary>
    public string Folder => _directory;

    public string PathOf(OutputChannel channel) => Path.Combine(_directory, channel.ToString().ToLowerInvariant() + ".log");

    /// <summary>Stores the text, one stored line per text line, with the secrets of any URI in it masked. Failure to write never reaches the caller: output must not break the work that produced it.</summary>
    public void Append(OutputChannel channel, string source, string text)
    {
        var now = DateTime.Now;
        source = Clean(source);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines) sb.Append(now.ToString("O", CultureInfo.InvariantCulture)).Append('\t').Append(source).Append('\t').Append(line.Replace('\0', ' ')).Append('\n');
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                using var fs = new FileStream(PathOf(channel), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 1);
                fs.Write(Utf8.GetBytes(sb.ToString()));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        foreach (var line in lines) Appended?.Invoke(channel, new OutputLine(now, source, line));
    }

    /// <summary>The last <paramref name="maxLines"/> lines of the channel, oldest first.</summary>
    public IReadOnlyList<OutputLine> Read(OutputChannel channel, int maxLines = 2000)
    {
        var all = new Queue<OutputLine>();
        try
        {
            using var fs = new FileStream(PathOf(channel), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Utf8);
            while (reader.ReadLine() is { } raw)
            {
                all.Enqueue(Parse(raw));
                if (all.Count > maxLines) all.Dequeue();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* no file yet: nothing was written */ }
        return [.. all];
    }

    public void Clear(OutputChannel channel)
    {
        try
        {
            lock (_gate) File.Delete(PathOf(channel));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        Cleared?.Invoke(channel);
    }

    private static OutputLine Parse(string raw)
    {
        var parts = raw.Split('\t', 3);
        return parts.Length == 3 && DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
            ? new OutputLine(time, parts[1], parts[2])
            : new OutputLine(default, "", raw);
    }

    private static string Clean(string source) => source.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Keeps the end of a file that grew past the limit, starting at a line boundary.</summary>
    private static void Trim(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxFileBytes) return;
            byte[] tail = new byte[KeptBytes];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(-KeptBytes, SeekOrigin.End);
                fs.ReadExactly(tail);
            }
            int start = Array.IndexOf(tail, (byte)'\n') + 1;
            File.WriteAllBytes(path, tail.AsSpan(start).ToArray());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* an oversized log is trimmed on a later start */ }
    }
}
