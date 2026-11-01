using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NksHub.Textida.Core.Tools;

namespace NksHub.Textida.App.Services;

/// <summary>What a script printed and how it ended.</summary>
internal sealed record WshResult(string Output, string Errors, int ExitCode, bool TimedOut);

/// <summary>
/// The bridge to the Windows Script Host (PZ.4.4): a script file of the user (<c>.js</c> as JScript, <c>.vbs</c> as VBScript, <c>.wsf</c>, <c>.wsh</c>) is run by the real <c>cscript.exe</c> of Windows and what it prints goes to the output window.
/// This is not an interpreter of the editor and makes no claim about JavaScript or VBScript compatibility of anything else: the language is whatever the Windows Script Host on the machine runs. A script can do all the user can do,
/// so each script (by its content) is confirmed once. The bridge exists on Windows only and nothing else in the editor depends on it.
/// </summary>
internal static class WshBridge
{
    public static readonly string[] Extensions = [".js", ".vbs", ".wsf", ".wsh"];
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static bool IsSupported => OperatingSystem.IsWindows();

    public static bool IsScript(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The <c>cscript.exe</c> in the system folder of Windows (never one found through the PATH); null elsewhere or when it is not there.</summary>
    public static string? CscriptPath()
    {
        if (!IsSupported) return null;
        string path = Path.Combine(Environment.SystemDirectory, "cscript.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>The invocation for confirmation and approval: the fingerprint names the script file and what is in it, so a changed script is asked about again.</summary>
    public static ToolInvocation Invocation(string cscript, string script, TimeSpan? timeout = null)
    {
        string full = Path.GetFullPath(script);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(full)));
        return new ToolInvocation("wsh", cscript, ["//Nologo", full], Path.GetDirectoryName(full)!, timeout ?? DefaultTimeout, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("wsh\0" + full.ToLowerInvariant() + "\0" + hash))));
    }

    /// <summary>Runs the script and waits for it; a run over the time limit is ended with its process tree. Output is read in the console code page of the machine, which is what cscript writes.</summary>
    public static async Task<WshResult> RunAsync(ToolInvocation tool, CancellationToken ct = default)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var info = ProcessLaunch.CreateStartInfo(tool.Executable, tool.Args);
        info.WorkingDirectory = tool.WorkingDirectory;
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardInput = info.RedirectStandardOutput = info.RedirectStandardError = true;
        info.StandardOutputEncoding = info.StandardErrorEncoding = oem;
        using var process = new Process { StartInfo = info };
        try { process.Start(); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException) { throw new ToolException(e.Message); }
        ProcessLaunch.Track(process);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = new CancellationTokenSource(tool.Timeout ?? DefaultTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, limit.Token);
        bool timedOut = false;
        try { await process.WaitForExitAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            timedOut = limit.IsCancellationRequested;
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* ended by itself */ }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (!timedOut) throw;
        }
        try { await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (TimeoutException) { /* a program the script started still holds the pipes */ }
        return new WshResult(output.IsCompletedSuccessfully ? output.Result : "", errors.IsCompletedSuccessfully ? errors.Result : "", process.ExitCode, timedOut);
    }
}
