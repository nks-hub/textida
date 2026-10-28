using System.Text.Encodings.Web;
using System.Text.Json;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Core.Agents.Transcripts;

/// <summary>
/// Hides the values of secrets files wherever a transcript shows them (P8b.3.5). The decision what a value is comes from <see cref="SecretValues"/>, the one filter of the editor: the text a tool
/// returned for a file it reads or writes (when <see cref="SecretValues.IsSecretFile"/> says the file is secret) is masked by the position of the values, and every value found that way is learned.
/// A learned value is then replaced wherever else it occurs, also written as it is inside JSON, so the panel, a raw line and the results of a search never show it.
/// A value seen first in a later line cannot be hidden in an earlier one that was shown before; values the caller learned from the files of the workspace are known from the start.
/// </summary>
public sealed class TranscriptMasker(int minValueLength)
{
    // bound of the implementation; shortcut: one pass per value over a text, upgrade to a multi-pattern search when a transcript holds hundreds of secrets
    private const int MaxKnownValues = 2048;

    private static readonly JavaScriptEncoder Relaxed = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    private static readonly char[] CommandSeparators = [' ', '\t', '\r', '\n', '"', '\'', '`', '|', ';', '&', '<', '>', '(', ')', '=', ','];

    private readonly object _gate = new();
    private readonly HashSet<string> _known = new(StringComparer.Ordinal);
    private volatile string[] _needles = [];

    public int KnownValues
    {
        get { lock (_gate) return _known.Count; }
    }

    /// <summary>True when a path, or a shell command that names a file, points at a secrets file.</summary>
    public static bool TargetsSecretFile(string? pathOrCommand)
    {
        if (string.IsNullOrWhiteSpace(pathOrCommand)) return false;
        if (SecretValues.IsSecretFile(pathOrCommand)) return true;
        return pathOrCommand.Split(CommandSeparators, StringSplitOptions.RemoveEmptyEntries).Any(t => SecretValues.IsSecretFile(t));
    }

    /// <summary>Learns one value; a value shorter than the limit is not learned, because it would hide ordinary text.</summary>
    public void Learn(string value)
    {
        if (value.Length < minValueLength) return;
        lock (_gate)
        {
            if (_known.Count >= MaxKnownValues || !_known.Add(value)) return;
            var needles = new List<string>(_needles.Length + 2);
            needles.AddRange(_needles);
            needles.Add(value);
            // written as it is, as JSON text inside a line, and as JSON text inside the JSON text of a call (the arguments of a Codex function call are a string of JSON)
            string escaped = JsonEncodedText.Encode(value, Relaxed).ToString();
            if (escaped != value) needles.Add(escaped);
            string twice = JsonEncodedText.Encode(escaped, Relaxed).ToString();
            if (twice != escaped) needles.Add(twice);
            needles.Sort((a, b) => b.Length.CompareTo(a.Length));
            _needles = [.. needles];
        }
    }

    /// <summary>Learns the values of the text of a secrets file (the text of a <c>.env</c> file).</summary>
    public void LearnFile(string text)
    {
        foreach (var span in SecretValues.Find(text)) Learn(ValueOf(text, span));
    }

    private static string ValueOf(string text, SecretSpan span)
    {
        var raw = text.Substring(span.Start, span.Length);
        return raw.Length >= 2 && raw[0] is '"' or '\'' or '`' && raw[^1] == raw[0] ? raw[1..^1] : raw;
    }

    /// <summary>Text that is the content of a secrets file: its values are hidden by position and learned.</summary>
    public string MaskSecretFile(string text)
    {
        var spans = SecretValues.Find(text);
        if (spans.Count == 0) return Mask(text);
        foreach (var span in spans) Learn(ValueOf(text, span));
        return Mask(SecretValues.Mask(text, spans));
    }

    /// <summary>Hides every learned value in <paramref name="text"/>; JSON text is masked the same way, the escaped form of a value is learned with the value.</summary>
    public string Mask(string text)
    {
        var needles = _needles;
        if (needles.Length == 0 || text.Length == 0) return text;
        foreach (var needle in needles)
            if (text.Contains(needle, StringComparison.Ordinal)) text = text.Replace(needle, SecretValues.Placeholder, StringComparison.Ordinal);
        return text;
    }
}
