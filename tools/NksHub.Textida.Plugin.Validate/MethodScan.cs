using System.Reflection;
using System.Text;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.PluginHost.Ui;

namespace NksHub.Textida.Plugin.Validate;

/// <summary>
/// Finds the methods of the plugin API that a plugin refers to by looking for their names in its files. A program holds the name of each method it calls as text (in UTF-8, or in UTF-16 in a
/// .NET program), and a script holds it in its source, so a name that is in no file is a method the plugin never calls. It is a scan, not an analysis: a name that is in a file may be dead code, and a
/// name put together at run time is not found. The run of the plugin (<see cref="ValidatorOptions.Run"/>) shows what it really calls.
/// </summary>
internal static class MethodScan
{
    private const int Chunk = 1 << 20;

    /// <summary>Every method name of the plugin API the editor serves.</summary>
    public static IReadOnlyList<string> Known { get; } =
    [
        .. Constants(typeof(DocumentMethods)), .. Constants(typeof(UiMethods)), .. Constants(typeof(PluginServiceMethods)),
    ];

    private static IEnumerable<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    /// <summary>The methods whose names occur in any of the files, each with the files it occurs in.</summary>
    public static async Task<IReadOnlyDictionary<string, List<string>>> FindAsync(IEnumerable<string> files, CancellationToken ct)
    {
        var patterns = Known.Select(m => (Method: m, Utf8: Encoding.UTF8.GetBytes(m), Utf16: Encoding.Unicode.GetBytes(m))).ToList();
        var overlap = patterns.Max(p => p.Utf16.Length);
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            await using var stream = File.OpenRead(file);
            var buffer = new byte[Chunk + overlap];
            var carried = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(carried, Chunk), ct).ConfigureAwait(false)) > 0)
            {
                var window = buffer.AsSpan(0, carried + read);
                foreach (var (method, utf8, utf16) in patterns)
                {
                    if (found.TryGetValue(method, out var seen) && seen.Contains(file)) continue;
                    if (window.IndexOf(utf8) < 0 && window.IndexOf(utf16) < 0) continue;
                    if (seen is null) found[method] = seen = [];
                    seen.Add(file);
                }
                // a name cut by the end of the chunk is found in the next one
                carried = Math.Min(overlap, window.Length);
                window[^carried..].CopyTo(buffer);
            }
        }
        return found;
    }
}
