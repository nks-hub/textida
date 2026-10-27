using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NksHub.Textida.App.Services;

/// <summary>A change from outside that waits for review: the file and the revision of the local history that holds the content from before the change.</summary>
internal sealed record ReviewState(string Path, string BaseRevisionId, DateTime CreatedUtc);

/// <summary>
/// The reviews that wait for a decision, one small file per document in the profile folder, so a review survives a restart or a crash. A file is written
/// to a temporary name and moved into place, so a crash leaves the old state or the new one, never half of one.
/// </summary>
internal sealed class ReviewStore(string directory)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private string FileOf(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (PathComparison == StringComparison.OrdinalIgnoreCase) full = full.ToUpperInvariant();
        return System.IO.Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)))[..32] + ".json");
    }

    public void Save(ReviewState state)
    {
        Directory.CreateDirectory(directory);
        string file = FileOf(state.Path), temp = file + ".tmp";
        using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var w = new Utf8JsonWriter(fs))
        {
            w.WriteStartObject();
            w.WriteNumber("version", 1);
            w.WriteString("path", System.IO.Path.GetFullPath(state.Path));
            w.WriteString("baseRevision", state.BaseRevisionId);
            w.WriteString("created", state.CreatedUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            w.WriteEndObject();
            w.Flush();
            fs.Flush(true);
        }
        File.Move(temp, file, overwrite: true);
    }

    /// <summary>The pending review of the file, or null when there is none or its record is unreadable.</summary>
    public ReviewState? Find(string path)
    {
        string file = FileOf(path);
        if (!File.Exists(file)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            var root = doc.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) return null;
            string stored = root.GetProperty("path").GetString()!;
            if (!string.Equals(stored, System.IO.Path.GetFullPath(path), PathComparison)) return null;
            return new ReviewState(stored, root.GetProperty("baseRevision").GetString()!, DateTime.Parse(root.GetProperty("created").GetString()!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    public void Remove(string path)
    {
        try { File.Delete(FileOf(path)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a leftover record is dropped when its revision is found missing */ }
    }
}
