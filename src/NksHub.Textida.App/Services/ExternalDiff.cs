using System.ComponentModel;
using System.Diagnostics;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Services;

/// <summary>
/// Compares a tab with the file on disk in an external tool, when the user set one (otherwise the built-in comparison is used). The content of the tab
/// is handed over as a temporary file with exactly the bytes of the document; the file is deleted when the tool exits.
/// </summary>
internal static class ExternalDiff
{
    /// <param name="Problem">Why the tool could not be started; null when it runs.</param>
    /// <param name="Completion">Completes when the tool has exited and the temporary file is gone.</param>
    internal sealed record Started(string? Problem, Task Completion, string? TempFile)
    {
        public bool Ok => Problem is null;
    }

    public static Started Compare(Document doc, string diskPath, string tool, string argumentsTemplate)
    {
        var dir = Path.Combine(Path.GetTempPath(), "textida-diff-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "buffer-" + Path.GetFileName(diskPath));
        try
        {
            using (var fs = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                doc.Backend.CopyTo(fs);

            var arguments = argumentsTemplate.Replace("{left}", diskPath, StringComparison.Ordinal).Replace("{right}", temp, StringComparison.Ordinal);
            var process = Process.Start(new ProcessStartInfo(tool, arguments) { UseShellExecute = false, CreateNoWindow = false })
                ?? throw new InvalidOperationException(tool);
            return new Started(null, Task.Run(async () =>
            {
                try { await process.WaitForExitAsync(); }
                finally
                {
                    process.Dispose();
                    Cleanup(dir);
                }
            }), temp);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            Cleanup(dir);
            return new Started(e.Message, Task.CompletedTask, null);
        }
    }

    private static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the temp folder is cleaned by the OS later */ }
    }
}
