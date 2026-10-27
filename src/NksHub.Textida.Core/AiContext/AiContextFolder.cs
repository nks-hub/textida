using NksHub.Textida.Core.Encodings;
using NksHub.Textida.Core.Search;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.Core.AiContext;

/// <summary>The files of a folder as context sources, with what was left out and why.</summary>
public sealed record AiFolderContext(IReadOnlyList<AiSource> Sources, IReadOnlyList<AiOmission> Omissions);

/// <summary>
/// Reads a folder for <see cref="AiContextBuilder"/>: the walk follows the ignore files (<c>.gitignore</c>, <c>.ignore</c>) of <see cref="FileWalker"/>; secrets files
/// are named and never read; hidden files and folders, binary files, files over the size limit of the walk and unreadable files are left out and counted.
/// </summary>
public static class AiContextFolder
{
    private const int BinarySampleBytes = 8000;

    /// <param name="languageOf">Language id of a file name, for the header of the file.</param>
    public static AiFolderContext Collect(string root, Func<string, string> languageOf, CancellationToken ct = default)
    {
        root = Path.GetFullPath(root);
        var stats = new FileWalkStats();
        // hidden files are walked too, so a secrets file (".env") is found and named instead of vanishing with the rest of them
        var options = new FileWalkOptions { IncludeHidden = true };
        var sources = new List<AiSource>();
        var omissions = new List<AiOmission>();
        long hidden = 0;
        foreach (var entry in FileWalker.Walk(root, options, stats, ct))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetRelativePath(root, entry.Path).Replace('\\', '/');
            if (SecretValues.IsSecretFile(entry.Path)) { omissions.Add(new AiOmission(AiOmitReason.SecretFile, name)); continue; }
            if (IsHidden(name, entry.Path)) { hidden++; continue; }
            byte[] bytes;
            try { bytes = File.ReadAllBytes(entry.Path); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { omissions.Add(new AiOmission(AiOmitReason.Unreadable, name)); continue; }

            var guess = EncodingGuesser.Guess(bytes, wholeFile: true);
            var codec = guess.Codec;
            // a NUL means binary unless a byte order mark says UTF-16 or UTF-32 (without one, UTF-16 text in a folder is not told from binary data)
            if ((codec.Kind is not (CodecKind.Utf16 or CodecKind.Utf32) || codec.BomLengthIn(bytes) == 0) && bytes.AsSpan(0, Math.Min(bytes.Length, BinarySampleBytes)).Contains((byte)0))
            {
                omissions.Add(new AiOmission(AiOmitReason.Binary, name));
                continue;
            }
            var text = codec.WithBom(false).Decode(bytes.AsMemory(codec.BomLengthIn(bytes))).Text;
            sources.Add(new AiSource(name, entry.Path, languageOf(entry.Path), codec.Name, null, false, [AiRange.Whole(text)]));
        }
        if (hidden > 0) omissions.Add(new AiOmission(AiOmitReason.Hidden, Count: hidden));
        if (stats.Ignored > 0) omissions.Add(new AiOmission(AiOmitReason.IgnoreRules, Count: stats.Ignored));
        if (stats.TooLarge > 0) omissions.Add(new AiOmission(AiOmitReason.TooLarge, Count: stats.TooLarge));
        if (stats.Unreadable > 0) omissions.Add(new AiOmission(AiOmitReason.Unreadable, Count: stats.Unreadable));
        return new AiFolderContext(sources, omissions);
    }

    private static bool IsHidden(string relative, string full)
    {
        foreach (var part in relative.Split('/'))
            if (part.StartsWith('.')) return true;
        try { return (File.GetAttributes(full) & FileAttributes.Hidden) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; } // reading it then reports it as unreadable
    }
}
