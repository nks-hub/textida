using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Services;

/// <summary>A file that cannot be restored: not a ZIP, not a configuration backup, written by a newer editor, or too large.</summary>
internal sealed class ConfigBackupException(string message) : Exception(message);

/// <param name="Files">Files written into the backup.</param>
/// <param name="LeftOut">Values, and whole files, left out because they hold or look like a password or a token.</param>
internal readonly record struct ConfigExport(int Files, int LeftOut);

/// <summary>
/// What the restore staged before the last start did: files put back, the archive that keeps the configuration from before it (null when the restore failed
/// before anything was changed), or why it failed.
/// </summary>
internal sealed record ConfigRestoreResult(int Files, string? SavedBefore, string? Problem);

/// <summary>
/// The configuration of a profile as one ZIP (F111): settings, keys, user variables, tools, save actions, log views, macros, recipes, data plugins,
/// workspaces, remote connections, the proxy and the toolbar. Journal, history, backups, the cache of remote files, sessions, logs and installed plugins stay out.
/// Passwords and tokens live in the credential store, which is never copied; a value that still holds one is left out (see <see cref="Export"/>).
/// A restore is only staged; <see cref="ApplyPending"/> puts the files back at the next start, before any service has read them, so no service writes
/// its old state over the restored files.
/// </summary>
internal static partial class ConfigBackup
{
    public const int FormatVersion = 1;
    private const string ManifestName = "textida-backup.json";

    /// <summary>The backup chosen in Restore Configuration, waiting in the profile for the next start.</summary>
    public const string PendingName = "config-restore.zip";

    /// <summary>Folder of the profile that keeps the configuration a restore replaced.</summary>
    public const string BeforeRestoreFolder = "config-backups";

    /// <summary>Default: the largest total size the files of one restore may unpack to; a larger backup is refused. Decided: a round default for ordinary use, changed only when a measurement asks for it.</summary>
    public const long MaxUnpackedBytes = 256L << 20;

    private static readonly string[] Files =
    [
        AppWorkbench.UserSettingsFile, AppWorkbench.UserKeymapFile, NksHub.Textida.Workbench.Clips.UserVariables.FileName, ToolCatalog.FileName,
        NksHub.Textida.Core.Tools.SaveActionConfig.FileName, "logviews.json", "workspace.json", "remote/connections.json", "remote/network.json", NksHub.Textida.Workbench.Toolbar.ToolbarLayout.FileName,
    ];

    private static readonly string[] Folders = ["macros", "recipes", "plugins", "projects"];

    private static readonly HashSet<string> SecretWords = ["password", "passwd", "pwd", "passphrase", "secret", "secrets", "token", "tokens", "credential", "credentials", "apikey", "authorization", "cookie"];

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string SuggestedName(DateTime now) => $"textida-config-{now:yyyy-MM-dd}.zip";

    /// <summary>Whether a path relative to the profile, with <c>/</c>, is part of the configuration. Anything that could leave the profile is not.</summary>
    internal static bool Belongs(string entry) =>
        entry.Length > 0 && entry[0] != '/' && !entry.Contains('\\') && !entry.Contains(':') && !entry.Split('/').Any(p => p is "" or "." or "..")
        && !entry.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
        && (Files.Contains(entry, StringComparer.OrdinalIgnoreCase) || Folders.Any(f => entry.StartsWith(f + "/", StringComparison.OrdinalIgnoreCase)));

    private static IEnumerable<string> Present(string profile)
    {
        foreach (var file in Files)
            if (File.Exists(Path.Combine(profile, file))) yield return file;
        foreach (var folder in Folders)
        {
            string dir = Path.Combine(profile, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                if (Path.GetRelativePath(profile, path).Replace('\\', '/') is var entry && Belongs(entry)) yield return entry;
        }
    }

    /// <summary>
    /// Writes the configuration of <paramref name="profile"/> into a ZIP at <paramref name="zipPath"/>, which is replaced only once the archive is complete.
    /// With <paramref name="scrub"/> a JSON property named like a password, token or key is left out, and so is a value that holds one of
    /// <paramref name="knownSecrets"/> (the passwords the credential store keeps for the connections); a file that cannot be checked value by value
    /// (damaged JSON, another kind of file holding a known secret) is left out whole.
    /// </summary>
    public static ConfigExport Export(string profile, string zipPath, IReadOnlyCollection<string> knownSecrets, bool scrub = true)
    {
        // a password of one to three characters would match ordinary text; the credential store keeps it out of the files anyway
        var secrets = knownSecrets.Where(s => s.Length >= 4).Distinct(StringComparer.Ordinal).ToList();
        string full = Path.GetFullPath(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = full + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        int files = 0, leftOut = 0;
        try
        {
            using (var zip = new ZipArchive(new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None), ZipArchiveMode.Create))
            {
                foreach (var entry in Present(profile).ToList())
                {
                    byte[] bytes = File.ReadAllBytes(Path.Combine(profile, entry));
                    if (scrub)
                    {
                        if (Clean(entry, bytes, secrets, ref leftOut) is not { } kept) continue;
                        bytes = kept;
                    }
                    using (var stream = zip.CreateEntry(entry).Open()) stream.Write(bytes);
                    files++;
                }
                var manifest = new JsonObject { ["format"] = FormatVersion, ["created"] = DateTime.UtcNow.ToString("O"), ["files"] = files };
                using var m = zip.CreateEntry(ManifestName).Open();
                m.Write(Encoding.UTF8.GetBytes(manifest.ToJsonString()));
            }
            FileSwap.Replace(temp, full);
        }
        finally { FileSwap.TryDelete(temp); }
        return new ConfigExport(files, leftOut);
    }

    /// <summary>The bytes to back up, or null when the file is left out whole.</summary>
    private static byte[]? Clean(string entry, byte[] bytes, List<string> secrets, ref int leftOut)
    {
        string text = Encoding.UTF8.GetString(bytes);
        if (entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            JsonNode? root = null;
            try { root = JsonNode.Parse(text.TrimStart('﻿'), documentOptions: ReadOptions); }
            catch (JsonException) { /* a damaged file cannot be checked value by value */ }
            if (root is null) { leftOut++; return null; }
            int removed = Scrub(root, secrets);
            leftOut += removed;
            return removed == 0 ? bytes : Serialize(root);
        }
        if (!secrets.Any(s => text.Contains(s, StringComparison.Ordinal))) return bytes;
        leftOut++;
        return null;
    }

    /// <summary>Removes, from an object, every property named like a secret or holding a known secret, and blanks such a text in an array. Returns how many.</summary>
    internal static int Scrub(JsonNode node, IReadOnlyCollection<string> secrets)
    {
        int removed = 0;
        if (node is JsonObject obj)
        {
            foreach (var (name, value) in obj.ToList())
            {
                if (IsSecretName(name) || Holds(value, secrets)) { obj.Remove(name); removed++; }
                else if (value is not null) removed += Scrub(value, secrets);
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (Holds(array[i], secrets)) { array[i] = ""; removed++; }
                else if (array[i] is { } item) removed += Scrub(item, secrets);
            }
        }
        return removed;
    }

    private static bool Holds(JsonNode? node, IReadOnlyCollection<string> secrets) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.GetValue<string>() is var text && secrets.Any(s => text.Contains(s, StringComparison.Ordinal));

    /// <summary>
    /// Whether a property name says it holds a secret: its last word is one of <see cref="SecretWords"/>, or it ends in an api, access, private or secret key.
    /// Words are split at dots, dashes, underscores and camel case, so <c>githubToken</c>, <c>API_TOKEN</c> and <c>x-api-key</c> are secret names, and
    /// <c>history.storeSecretFiles</c> or <c>privateKeyPath</c> are not.
    /// </summary>
    internal static bool IsSecretName(string name)
    {
        var words = Words().Matches(name).Select(m => m.Value.ToLowerInvariant()).ToList();
        if (words.Count == 0) return false;
        return SecretWords.Contains(words[^1]) || words.Count > 1 && words[^1] == "key" && words[^2] is "api" or "access" or "private" or "secret";
    }

    [GeneratedRegex("[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])")]
    private static partial Regex Words();

    private static byte[] Serialize(JsonNode root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            root.WriteTo(writer);
        return stream.ToArray();
    }

    /// <summary>Checks that <paramref name="zipPath"/> is a backup this editor can restore and returns how many files it would put back.</summary>
    public static int Inspect(string zipPath)
    {
        using var zip = OpenBackup(zipPath);
        return zip.Entries.Count(e => Belongs(e.FullName));
    }

    private static ZipArchive OpenBackup(string zipPath)
    {
        string name = Path.GetFileName(zipPath);
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(zipPath); }
        catch (InvalidDataException e) { throw new ConfigBackupException($"'{name}' is not a ZIP file: {e.Message}"); }
        try
        {
            var manifest = zip.GetEntry(ManifestName) ?? throw new ConfigBackupException($"'{name}' is not a configuration backup of Textida.");
            JsonNode? m;
            try { using var s = manifest.Open(); m = JsonNode.Parse(s); }
            catch (JsonException e) { throw new ConfigBackupException($"The description of the backup '{name}' is damaged: {e.Message}"); }
            if ((m as JsonObject)?["format"] is not JsonValue v || v.GetValueKind() != JsonValueKind.Number || !v.TryGetValue<int>(out int format) || format < 1)
                throw new ConfigBackupException($"'{name}' has no valid format version.");
            if (format > FormatVersion) throw new ConfigBackupException($"'{name}' was written by a newer editor (format {format}).");
            long size = zip.Entries.Where(e => Belongs(e.FullName)).Sum(e => e.Length);
            if (size > MaxUnpackedBytes) throw new ConfigBackupException($"'{name}' unpacks to {size} bytes; the limit is {MaxUnpackedBytes}.");
            return zip;
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }

    /// <summary>Copies a checked backup into the profile; the next start puts its files back.</summary>
    public static void Stage(string profile, string zipPath)
    {
        Directory.CreateDirectory(profile);
        string target = Path.Combine(profile, PendingName);
        string temp = target + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.Copy(zipPath, temp);
            FileSwap.Replace(temp, target);
        }
        finally { FileSwap.TryDelete(temp); }
    }

    /// <summary>
    /// Runs at start before anything reads the profile. Saves the configuration the staged backup replaces, unscrubbed, to <see cref="BeforeRestoreFolder"/>,
    /// then puts back every file of the backup; files the backup does not have stay as they are. The staged file is removed either way. Null when nothing was staged.
    /// </summary>
    public static ConfigRestoreResult? ApplyPending(string profile, DateTime now)
    {
        string pending = Path.Combine(profile, PendingName);
        if (!File.Exists(pending)) return null;
        string? before = null;
        int files = 0;
        try
        {
            using var zip = OpenBackup(pending);
            string saved = Path.Combine(profile, BeforeRestoreFolder, $"before-restore-{now:yyyyMMdd-HHmmss}.zip");
            Export(profile, saved, [], scrub: false);
            before = saved;
            string root = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var entry in zip.Entries.Where(e => Belongs(e.FullName)))
            {
                string target = Path.GetFullPath(Path.Combine(profile, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // Belongs refuses such names already; the last guard of the profile
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string temp = target + ".textida-" + Guid.NewGuid().ToString("N")[..8] + ".tmp";
                try
                {
                    entry.ExtractToFile(temp);
                    FileSwap.Replace(temp, target);
                }
                finally { FileSwap.TryDelete(temp); }
                files++;
            }
            return new ConfigRestoreResult(files, before, null);
        }
        catch (Exception e) when (e is ConfigBackupException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new ConfigRestoreResult(files, before, e.Message);
        }
        finally { FileSwap.TryDelete(pending); }
    }
}
