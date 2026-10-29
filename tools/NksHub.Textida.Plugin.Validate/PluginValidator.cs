using System.Reflection.PortableExecutable;
using System.Text.Json;
using NksHub.Textida.Plugin.Testing;
using NksHub.Textida.PluginHost;
using NksHub.Textida.PluginHost.Documents;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.PluginHost.Permissions;
using NksHub.Textida.Rpc;

namespace NksHub.Textida.Plugin.Validate;

/// <summary>
/// Checks a plugin folder before anybody installs it. Statically: the manifest with every rule the editor applies when it loads one, the range of the plugin API, the permissions the manifest declares against
/// the methods the plugin refers to, the size of the folder, and whether the program is a NativeAOT one (and, with the sources, whether they keep to the rules of one). With <see cref="ValidatorOptions.Run"/>:
/// the handshake of the running plugin, its answer to a method it does not have, and a recorded session. It does not audit the binary for anything that is not a rule of the API.
/// </summary>
public static class PluginValidator
{
    private static readonly string[] ScriptExtensions = [".py", ".js", ".mjs", ".cjs", ".ts", ".lua", ".rb", ".pl", ".php", ".ps1", ".sh"];

    /// <summary>Permissions a manifest asks for that no single method of the API needs; they are not reported as unused.</summary>
    private static readonly string[] NoMethod = [DocumentPermissions.WorkspaceFiles, DocumentPermissions.LanguageServer];

    /// <summary>Runs the checks the options ask for on the plugin folder <paramref name="folder"/>.</summary>
    public static async Task<ValidationReport> ValidateAsync(string folder, ValidatorOptions options, CancellationToken ct = default)
    {
        var root = Path.GetFullPath(folder);
        var findings = new List<Finding>();
        if (!Directory.Exists(root))
        {
            findings.Add(new(FindingSeverity.Error, "manifest", $"The folder '{root}' does not exist."));
            return new ValidationReport(root, findings);
        }

        var load = PluginManifestLoader.Load(root, options.HostApiVersion, options.MaxGrammarBytes, options.MaxIconBytes);
        foreach (var error in load.Errors) findings.Add(new(FindingSeverity.Error, CheckOf(error), error));
        if (load.Plugin is { } loaded)
        {
            var api = loaded.Manifest.Api;
            findings.Add(new(FindingSeverity.Info, "api", $"The plugin works with plugin API {api.Min} to {api.Max}; this version of the SDK provides {options.HostApiVersion}."));
            findings.Add(new(FindingSeverity.Info, "manifest", $"'{loaded.Manifest.Id}' {loaded.Manifest.Version} is a valid manifest."));
        }

        CheckSize(root, options, findings);
        if (load.Plugin is { } plugin)
        {
            await CheckPermissionsAsync(root, plugin, findings, ct).ConfigureAwait(false);
            CheckProgram(plugin, findings);
        }
        if (options.SourcePath is { } sources) findings.AddRange(await SourceAnalysis.RunAsync(Path.GetFullPath(sources), ct).ConfigureAwait(false));
        if (options.Run) await RunAsync(load.Plugin, options, findings, ct).ConfigureAwait(false);
        else if (options.ScenarioPath is not null) findings.Add(new(FindingSeverity.Error, "scenario", "A scenario is replayed against a running plugin; add the option to run it."));
        return new ValidationReport(root, findings);
    }

    private static string CheckOf(string error) =>
        error.StartsWith("api ", StringComparison.Ordinal) || error.StartsWith("The plugin supports plugin API", StringComparison.Ordinal) ? "api"
        : error.Contains("ermission", StringComparison.Ordinal) ? "permissions"
        : "manifest";

    private static void CheckSize(string root, ValidatorOptions options, List<Finding> findings)
    {
        long total = 0;
        var count = 0;
        var enumeration = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (var file in new DirectoryInfo(root).EnumerateFiles("*", enumeration))
        {
            count++;
            total += file.Length;
        }
        if (count > options.MaxEntries) findings.Add(new(FindingSeverity.Error, "size", $"The folder holds {count} files; a plugin may hold {options.MaxEntries}."));
        if (total > options.MaxFolderBytes) findings.Add(new(FindingSeverity.Error, "size", $"The folder holds {total} bytes; a plugin may hold {options.MaxFolderBytes}."));
        if (count <= options.MaxEntries && total <= options.MaxFolderBytes) findings.Add(new(FindingSeverity.Info, "size", $"{count} files, {total} bytes: within the limits ({options.MaxEntries} files, {options.MaxFolderBytes} bytes)."));
    }

    private static async Task CheckPermissionsAsync(string root, LoadedPlugin plugin, List<Finding> findings, CancellationToken ct)
    {
        var declared = plugin.Manifest.Permissions.ToHashSet(StringComparer.Ordinal);
        var files = CandidateFiles(root, plugin);
        if (files.Count == 0)
        {
            findings.Add(new(FindingSeverity.Info, "permissions", $"The plugin has no file the validator can read for the methods it calls (a program outside the folder?); {declared.Count} permissions are declared and none was compared."));
            return;
        }

        var found = await MethodScan.FindAsync(files, ct).ConfigureAwait(false);
        var needed = found.Keys.Select(method => (Method: method, Permission: PluginMethodPermissions.For(method))).Where(x => x.Permission is not null).GroupBy(x => x.Permission!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        foreach (var group in needed)
        {
            var methods = string.Join(", ", group.Select(x => x.Method).Order(StringComparer.Ordinal));
            if (group.Key == PluginPermissionNames.CommandExecute)
                findings.Add(new(FindingSeverity.Info, "permissions", $"The plugin refers to {methods}; the user grants '{group.Key}' for the target plugin when it is needed, a manifest cannot ask for it."));
            else if (!declared.Contains(group.Key))
                findings.Add(new(FindingSeverity.Error, "permissions", $"The plugin refers to {methods}, which needs the permission '{group.Key}', but the manifest does not declare it."));
            else
                findings.Add(new(FindingSeverity.Info, "permissions", $"'{group.Key}' is declared and the plugin refers to {methods}."));
        }
        var used = needed.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var permission in declared.Where(p => !used.Contains(p) && !NoMethod.Contains(p)).Order(StringComparer.Ordinal))
            findings.Add(new(FindingSeverity.Warning, "permissions", $"The manifest declares '{permission}', but no method that needs it was found in {files.Count} files of the plugin."));
    }

    /// <summary>The program, the files its arguments name, and the scripts of the folder: where the names of the methods a plugin calls are.</summary>
    private static List<string> CandidateFiles(string root, LoadedPlugin plugin)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddIfInside(string path)
        {
            string full;
            try { full = Path.GetFullPath(path, root); }
            catch (ArgumentException) { return; }
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) files.Add(full);
        }
        if (plugin.Spec is { } spec)
        {
            AddIfInside(spec.Executable);
            AddIfInside(Path.ChangeExtension(spec.Executable, ".dll")); // the program of a framework-dependent plugin is the assembly beside its launcher
            foreach (var argument in spec.Arguments) AddIfInside(argument);
        }
        foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }))
            if (ScriptExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) files.Add(file);
        return [.. files];
    }

    /// <summary>A NativeAOT program is native code: it has no .NET metadata and no runtime configuration beside it.</summary>
    private static void CheckProgram(LoadedPlugin plugin, List<Finding> findings)
    {
        if (plugin.Spec is not { } spec)
        {
            findings.Add(new(FindingSeverity.Info, "aot", "The plugin has no process (data only); there is no program to check."));
            return;
        }
        var program = spec.Executable;
        if (spec.Arguments.FirstOrDefault(a => a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) is { } assembly && Path.GetFileNameWithoutExtension(program).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new(FindingSeverity.Warning, "aot", $"The plugin runs the managed assembly '{assembly}' through dotnet; a distributed plugin is a NativeAOT program, which needs no .NET runtime on the machine of the user."));
            return;
        }
        if (!File.Exists(program))
        {
            findings.Add(new(FindingSeverity.Info, "aot", $"'{program}' is not a file the validator can read."));
            return;
        }
        if (File.Exists(Path.ChangeExtension(program, ".runtimeconfig.json")) || File.Exists(program + ".runtimeconfig.json"))
        {
            findings.Add(new(FindingSeverity.Warning, "aot", $"'{Path.GetFileName(program)}' has a runtimeconfig.json beside it, so it is a framework-dependent .NET program; publish it with NativeAOT."));
            return;
        }
        try
        {
            using var stream = File.OpenRead(program);
            var head = new byte[2];
            if (stream.Read(head) == 2 && head[0] == 'M' && head[1] == 'Z')
            {
                stream.Position = 0;
                using var pe = new PEReader(stream);
                findings.Add(pe.HasMetadata
                    ? new(FindingSeverity.Warning, "aot", $"'{Path.GetFileName(program)}' is a managed assembly; publish the plugin with NativeAOT.")
                    : new(FindingSeverity.Info, "aot", $"'{Path.GetFileName(program)}' is native code with no .NET metadata, as a NativeAOT program is."));
            }
            else if (head is [(byte)'#', (byte)'!']) findings.Add(new(FindingSeverity.Info, "aot", $"'{Path.GetFileName(program)}' is a script; NativeAOT does not apply."));
            else findings.Add(new(FindingSeverity.Info, "aot", $"'{Path.GetFileName(program)}' is not a Windows executable; the validator cannot tell a managed program from a native one here."));
        }
        catch (Exception e) when (e is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            findings.Add(new(FindingSeverity.Warning, "aot", $"'{Path.GetFileName(program)}' could not be read as an executable: {e.Message}"));
        }
    }

    private static async Task RunAsync(LoadedPlugin? plugin, ValidatorOptions options, List<Finding> findings, CancellationToken ct)
    {
        if (plugin?.Spec is not { } spec)
        {
            findings.Add(new(FindingSeverity.Error, "handshake", plugin is null ? "The plugin was not started because its manifest is not valid." : "The plugin has no process, so there is nothing to start."));
            return;
        }
        Recording? recording = null;
        if (options.ScenarioPath is { } scenario)
        {
            try { recording = Recording.Load(scenario); }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                findings.Add(new(FindingSeverity.Error, "scenario", $"The scenario '{scenario}' cannot be read: {e.Message}"));
                return;
            }
        }

        var declared = plugin.Manifest.Permissions;
        TestEditor editor;
        try
        {
            editor = await TestEditor.StartProcessAsync(spec.Executable, spec.Arguments,
                new TestEditorOptions { PluginId = plugin.Manifest.Id, Grants = declared, ApiVersion = options.HostApiVersion, RequestTimeout = options.PluginTimeout }, spec.WorkingDirectory)
                .WaitAsync(options.PluginTimeout, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is PluginHandshakeException or PluginCrashedException or TimeoutException)
        {
            findings.Add(new(FindingSeverity.Error, "handshake", e is TimeoutException ? $"The plugin did not finish the handshake in {options.PluginTimeout}." : e.Message));
            return;
        }

        List<string> stderr;
        try
        {
            findings.Add(new(FindingSeverity.Info, "handshake", $"The plugin answered the handshake with plugin API version {options.HostApiVersion}."));
            await CheckUnknownMethodAsync(editor, options, findings, ct).ConfigureAwait(false);
            if (recording is not null) await ReplayAsync(editor, recording, declared, findings, ct).ConfigureAwait(false);
        }
        finally
        {
            stderr = [.. editor.StderrTail.Take(5)];
            try { await editor.DisposeAsync().AsTask().WaitAsync(options.PluginTimeout, ct).ConfigureAwait(false); }
            catch (TimeoutException) { findings.Add(new(FindingSeverity.Warning, "handshake", $"The plugin did not leave in {options.PluginTimeout} after it was asked to end.")); }
        }
        foreach (var line in stderr) findings.Add(new(FindingSeverity.Info, "handshake", "stderr: " + line));
    }

    private static async Task CheckUnknownMethodAsync(TestEditor editor, ValidatorOptions options, List<Finding> findings, CancellationToken ct)
    {
        try
        {
            await editor.RequestPluginAsync("validate/unknown-method", "{}", ct).WaitAsync(options.PluginTimeout, ct).ConfigureAwait(false);
            findings.Add(new(FindingSeverity.Warning, "rpc", "The plugin answered a method it does not have; it should answer with the error method-not-found (-32601)."));
        }
        catch (RpcException e) when (e.Code == RpcErrorCodes.MethodNotFound) { findings.Add(new(FindingSeverity.Info, "rpc", "An unknown method is answered with method-not-found (-32601).")); }
        catch (RpcException e) { findings.Add(new(FindingSeverity.Warning, "rpc", $"An unknown method was answered with the error {e.Code} ({e.Message}); the error method-not-found (-32601) is the one that means it.")); }
        catch (TimeoutException) { findings.Add(new(FindingSeverity.Error, "rpc", $"The plugin did not answer an unknown method in {options.PluginTimeout}.")); }
        catch (Exception e) when (e is PluginCrashedException or PluginUnavailableException) { findings.Add(new(FindingSeverity.Error, "rpc", "The plugin ended when it was sent an unknown method: " + e.Message)); }
    }

    private static async Task ReplayAsync(TestEditor editor, Recording recording, IReadOnlyList<string> declared, List<Finding> findings, CancellationToken ct)
    {
        var result = await editor.ReplayAsync(recording, ct: ct).ConfigureAwait(false);
        foreach (var failure in result.Failures) findings.Add(new(FindingSeverity.Error, "scenario", failure));
        if (result.Passed) findings.Add(new(FindingSeverity.Info, "scenario", $"The recorded session ({result.Steps.Count} steps) ran as recorded."));

        var declaredSet = declared.ToHashSet(StringComparer.Ordinal);
        foreach (var permission in editor.UsedPermissions())
        {
            var methods = string.Join(", ", editor.Calls.Where(c => PluginMethodPermissions.For(c.Method) == permission).Select(c => c.Method).Distinct().Order(StringComparer.Ordinal));
            if (permission == PluginPermissionNames.CommandExecute) findings.Add(new(FindingSeverity.Info, "permissions", $"During the session the plugin called {methods}; '{permission}' is granted by the user for the target plugin when it is needed."));
            else if (!declaredSet.Contains(permission)) findings.Add(new(FindingSeverity.Error, "permissions", $"During the session the plugin called {methods}, which needs '{permission}', but the manifest does not declare it."));
            else findings.Add(new(FindingSeverity.Info, "permissions", $"During the session the plugin used '{permission}' ({methods})."));
        }
        var used = editor.UsedPermissions().ToHashSet(StringComparer.Ordinal);
        foreach (var permission in declaredSet.Where(p => !used.Contains(p) && !NoMethod.Contains(p)).Order(StringComparer.Ordinal))
            findings.Add(new(FindingSeverity.Info, "permissions", $"The session did not use '{permission}'; the session may not reach every command."));
    }
}
