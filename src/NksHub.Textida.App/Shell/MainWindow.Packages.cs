using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Tools;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Package manifests (P8.7.3, P8.7.5): asking a registry about the versions of a manifest after the user allowed it, and running a script of a <c>package.json</c> or
/// <c>composer.json</c> through the tool runner like a tool from a project folder. The registry requests themselves are made by the JSON plugin.
/// </summary>
internal sealed partial class MainWindow
{
    // ---- registry lookups ----

    /// <summary>Checks the pinned versions of the open manifest against its registry; <paramref name="refresh"/> drops the cached answers first. Asks for consent the first time for each registry.</summary>
    internal CommandOutcome PackageLookup(bool refresh)
    {
        if (Active is not { Custom: null, Hex: null } tab || !PluginServices.IsManifestFile(tab))
        {
            SetStatusMessage(Loc.Get(K.PackagesNotManifest));
            return CommandOutcome.Handled;
        }
        _ = PackageLookupAsync(tab, refresh);
        return CommandOutcome.Handled;
    }

    internal async Task PackageLookupAsync(DocTab tab, bool refresh)
    {
        var registry = PackageManifests.For(tab.Doc.Doc.FilePath!)!;
        var plugins = _app.Plugins;
        if (!plugins.Registries.IsAllowed(registry.Id))
        {
            var request = new ChoiceRequest(Loc.Get(K.PackagesConsentTitle), Loc.Get(K.PackagesConsentMessage, ("registry", registry.Name)),
                [new DialogButton(Loc.Get(K.PackagesConsentAllow), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)],
                [Loc.Get(K.PackagesConsentHosts, ("hosts", registry.Hosts))]);
            if (await _app.Dialogs.ChooseAsync(this, request) != 0) return;
            plugins.Registries.Allow(registry.Id);
        }
        SetStatusMessage(Describe(registry, await plugins.CheckPackagesAsync(this, tab, refresh, background: false)));
    }

    private static string Describe(PackageRegistry registry, JsonElement? answer)
    {
        if (answer is not { } a) return Loc.Get(K.PackagesBusy);
        switch (a.GetProperty("status").GetString())
        {
            case "ok":
                string done = Loc.Get(K.PackagesDone, ("count", a.GetProperty("issues").GetInt32()));
                return a.GetProperty("cached").GetBoolean() ? done + " " + Loc.Get(K.PackagesCachedSuffix) : done;
            case "noConsent": return Loc.Get(K.PackagesNoConsent, ("registry", registry.Name));
            case "tooLarge": return Loc.Get(K.PackagesTooLarge);
            case "error": return Loc.Get(K.PackagesError, ("registry", registry.Name), ("reason", a.GetProperty("message").GetString() ?? ""));
            default: return Loc.Get(K.PackagesBusy);
        }
    }

    /// <summary>Takes every registry consent back and has the open manifests drop the marks they got from a registry.</summary>
    internal CommandOutcome PackageRevoke()
    {
        _app.Plugins.Registries.RevokeAll();
        foreach (var tab in Tabs.Where(PluginServices.IsManifestFile).ToList())
            _ = _app.Plugins.CheckPackagesAsync(this, tab, refresh: false, background: true, force: true);
        SetStatusMessage(Loc.Get(K.PackagesRevoked));
        return CommandOutcome.Handled;
    }

    // ---- scripts ----

    /// <summary>Lists the scripts of the open <c>package.json</c> or <c>composer.json</c>; the chosen one is started by npm or Composer through the tool runner.</summary>
    internal CommandOutcome ShowPackageScripts()
    {
        if (Active is not { Custom: null, Hex: null } tab || tab.Path is not { } path || Path.GetFileName(path).ToLowerInvariant() is not ("package.json" or "composer.json"))
        {
            SetStatusMessage(Loc.Get(K.PackagesScriptNotManifest));
            return CommandOutcome.Handled;
        }
        string text;
        try { text = tab.Surface is { } surface ? surface.Document.GetText(0, surface.Document.TextLength) : File.ReadAllText(path); }
        catch (IOException e)
        {
            SetStatusMessage(e.Message);
            return CommandOutcome.Handled;
        }
        var scripts = PackageManifests.Scripts(path, text);
        if (scripts.Count == 0)
        {
            SetStatusMessage(Loc.Get(K.PackagesScriptNone, ("file", Path.GetFileName(path))));
            return CommandOutcome.Handled;
        }
        var rows = scripts.Select(s => new QuickRow(s.Name, null, s.Body.Length > 80 ? s.Body[..80] + "…" : s.Body, null, true, null, s.Name, () => _ = RunPackageScriptAsync(path, s))).ToList();
        ShowChoice(Loc.Get(K.PackagesScriptWatermark), Loc.Get(K.A11yPackageScripts), rows);
        return CommandOutcome.Handled;
    }

    /// <summary>
    /// Runs a script of a manifest as a tool from that file: the command, the directory and the file are confirmed the first time, and again when the text of the script changes (it is part of
    /// the tool's identity). <paramref name="program"/> and <paramref name="programArgs"/> (put before the arguments of the script runner) exist so a test can stand in for npm or Composer.
    /// </summary>
    internal Task<bool> RunPackageScriptAsync(string manifest, PackageScript script, string? program = null, IReadOnlyList<string>? programArgs = null)
    {
        string full = Path.GetFullPath(manifest);
        static string Literal(string text) => text.Replace("$", "$$", StringComparison.Ordinal);
        string executable = script.Executable;
        List<string> before = [];
        if (program is not null)
        {
            executable = program;
            before.AddRange(programArgs ?? []);
        }
        else if (OperatingSystem.IsWindows() && PackageManifests.WindowsTarget(script.Executable, Environment.GetEnvironmentVariable("PATH") ?? "") is { } target)
        {
            executable = target.Executable;
            before.AddRange(target.Prefix);
        }
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Body)))[..16];
        var definition = new ExternalToolContribution
        {
            Id = $"script:{script.Executable}:{script.Name}:{identity}",
            Title = Loc.Get(K.PackagesScriptTitle, ("runner", script.Executable), ("script", script.Name)),
            Executable = Literal(executable),
            Args = [.. before.Select(Literal), .. script.Args.Select(Literal)],
            Cwd = Literal(Path.GetDirectoryName(full)!),
        };
        return RunToolAsync(new ToolEntry(definition, ToolCatalog.ToSpec(definition, [], full), null, null, full));
    }
}
