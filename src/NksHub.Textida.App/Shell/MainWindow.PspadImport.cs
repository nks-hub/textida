using System.Globalization;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Secrets;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Remote;
using NksHub.Textida.Workbench.Commands;
using NksHub.Textida.Workbench.Contributions;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Import of the user's own data from a PSPad installation (PZ.1.1). The folder is only read. The user first sees what was found (and what cannot be carried over), then agrees;
/// saved FTP passwords are read and put into the credential store only after a second question.
/// </summary>
internal sealed partial class MainWindow
{
    internal CommandOutcome ImportPspad()
    {
        _ = ImportPspadAsync(null);
        return CommandOutcome.Handled;
    }

    private static string Names(IEnumerable<string> names, int max = 8)
    {
        var list = names.ToList();
        return string.Join(", ", list.Take(max)) + (list.Count > max ? ", …" : "");
    }

    /// <summary>The lines of the preview: one for each kind of data, the ones that cannot be carried over named.</summary>
    internal static List<string> PspadPreview(PspadPlan plan)
    {
        var lines = new List<string>();
        if (plan.Connections.Count > 0)
            lines.Add(Loc.Get(K.PspadFtp, ("number", plan.Connections.Count), ("withPassword", plan.ConnectionsWithPassword), ("names", Names(plan.Connections.Select(c => c.Name)))));
        if (plan.Projects.Count > 0)
            lines.Add(Loc.Get(K.PspadProjects, ("number", plan.Projects.Count), ("names", Names(plan.Projects.Select(p => p.Name)))));
        if (plan.ClipFiles.Count > 0)
            lines.Add(Loc.Get(K.PspadClips, ("number", plan.ClipFiles.Sum(f => f.Clips.Count - f.Skipped)), ("files", plan.ClipFiles.Count), ("skipped", plan.ClipFiles.Sum(f => f.Skipped))));
        if (plan.Macros.Count > 0)
        {
            var bad = plan.Macros.Where(m => m.Unsupported is not null).ToList();
            lines.Add(Loc.Get(K.PspadMacros, ("number", plan.Macros.Count - bad.Count), ("skipped", bad.Count), ("names", Names(bad.Select(m => $"{m.Name} ({m.Unsupported})")))));
        }
        if (plan.Highlighters.Count > 0)
            lines.Add(Loc.Get(K.PspadHighlighters, ("number", plan.Highlighters.Count), ("names", Names(plan.Highlighters.Select(h => h.Name))), ("approximate", plan.Highlighters.Count(h => h.NotConverted.Count > 0))));
        if (plan.Keys.Count > 0 || plan.SkippedKeys.Count > 0)
            lines.Add(Loc.Get(K.PspadKeys, ("number", plan.Keys.Count), ("skipped", plan.SkippedKeys.Count)));
        lines.AddRange(plan.Problems.Take(5));
        return lines;
    }

    internal async Task ImportPspadAsync(string? folder)
    {
        folder ??= await _app.Dialogs.PickFolderAsync(this);
        if (folder is null) return;
        SetStatusMessage(Loc.Get(K.PspadReading, ("folder", folder)));
        PspadPlan plan;
        try { plan = await Task.Run(() => PspadImport.Build(folder)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            SetStatusMessage(Loc.Get(K.PspadFailed, ("reason", e.Message)));
            return;
        }
        if (plan.IsEmpty)
        {
            SetStatusMessage(Loc.Get(K.PspadNothing, ("folder", folder)));
            return;
        }
        var ask = new ChoiceRequest(Loc.Get(K.PspadTitle), Loc.Get(K.PspadMessage, ("folder", folder)),
            [new DialogButton(Loc.Get(K.PspadImport), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], PspadPreview(plan), HelpTopic: "import.pspad");
        if (await _app.Dialogs.ChooseAsync(this, ask) != 0) return;

        bool passwords = false;
        if (plan.Connections.Count > 0 && plan.ConnectionsWithPassword > 0)
        {
            var passwordAsk = new ChoiceRequest(Loc.Get(K.PspadTitle), Loc.Get(K.PspadPasswordQuestion, ("number", plan.ConnectionsWithPassword)),
                [new DialogButton(Loc.Get(K.PspadWithoutPasswords), IsDefault: true), new DialogButton(Loc.Get(K.PspadWithPasswords)), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]);
            int answer = await _app.Dialogs.ChooseAsync(this, passwordAsk);
            if (answer is not (0 or 1)) return;
            passwords = answer == 1;
        }
        var report = ApplyPspadImport(plan, passwords);
        SetStatusMessage(Loc.Get(K.PspadDone, ("connections", report.Connections), ("projects", report.Projects), ("clips", report.Clips), ("macros", report.Macros), ("highlighters", report.Highlighters), ("keys", report.Keys)));
        if (report.Problems.Count > 0)
            await _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.PspadTitle), Loc.Get(K.PspadProblems), [new DialogButton(Loc.Get(K.DialogOk), IsDefault: true)], report.Problems));
    }

    internal sealed record PspadReport(int Connections, int Projects, int Clips, int Macros, int Highlighters, int Keys, List<string> Problems);

    /// <summary>Writes the plan into the profile of the editor. Everything that exists already is kept: a connection to the same server and user, a project or macro of the same name.</summary>
    internal PspadReport ApplyPspadImport(PspadPlan plan, bool withPasswords)
    {
        var problems = new List<string>();
        int connections = 0, projects = 0, clips = 0, macros = 0, highlighters = 0, keys = 0;

        var known = _app.Remote.Connections.Load();
        var passwords = new Dictionary<string, string>(StringComparer.Ordinal);
        if (withPasswords && PspadImportFile(plan.Root, "PSPadFTP.INI") is { } ftpText)
            foreach (var c in PspadFtp.Read(ftpText, withPasswords: true))
                if (c.Password is not null) passwords[c.Name] = c.Password;
        foreach (var c in plan.Connections)
        {
            var protocol = c.Tls ? RemoteProtocol.Ftps : RemoteProtocol.Ftp;
            if (known.Any(k => k.Protocol == protocol && string.Equals(k.Host, c.Host, StringComparison.OrdinalIgnoreCase) && k.Port == c.Port && k.UserName == c.User)) continue;
            var connection = new RemoteConnection
            {
                Name = c.Folder.Length > 0 ? $"{c.Folder}/{c.Name}" : c.Name, Protocol = protocol, Host = c.Host, Port = c.Port, UserName = c.User,
                FtpsMode = c.Tls && c.Port == 990 ? FtpsMode.Implicit : FtpsMode.Explicit,
            };
            try
            {
                _app.Remote.Connections.Add(connection, passwords.GetValueOrDefault(c.Name), _app.Remote.Secrets);
                connections++;
            }
            catch (Exception e) when (e is CredentialStoreUnavailableException or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{c.Name}: {e.Message}");
            }
        }

        foreach (var p in plan.Projects)
        {
            string name = _app.Workspace.Find(p.Name) is null ? p.Name : p.Name + " (PSPad)";
            var data = new ProjectData { Name = name, Folders = [p.Folder], Files = [.. p.Files.Where(File.Exists)] };
            if (p.DefaultFolder is { } d && Directory.Exists(d) && !data.Folders.Contains(d, StringComparer.OrdinalIgnoreCase)) data.Folders.Add(d);
            try
            {
                _app.Workspace.SaveProject(data);
                projects++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { problems.Add($"{p.Name}: {e.Message}"); }
        }

        foreach (var file in plan.ClipFiles)
        {
            string? language = LanguageModes.All.FirstOrDefault(l => string.Equals(l.Id, file.Stem, StringComparison.OrdinalIgnoreCase) || string.Equals(l.Name, file.Stem, StringComparison.OrdinalIgnoreCase))?.Id;
            var items = file.Clips.Where(c => !c.UsesMacro && c.Name.Length > 0)
                .Select(c => new ClipContribution { Title = c.Name, Text = PspadClips.ToTemplate(c.Text, file.Cursor, file.Selection), Language = language }).ToList();
            clips += _app.Clips.ImportClips(file.Stem, file.Stem, items, out string detail);
            if (detail.Length > 0) problems.Add($"{file.Stem}: {detail}");
        }

        var existingMacros = _app.Macros.Load([]).Select(m => m.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in plan.Macros.Where(m => m.Unsupported is null))
        {
            string name = existingMacros.Contains(m.Name) ? m.Name + " (PSPad)" : m.Name;
            var steps = m.Steps.Select(s => new MacroStep(s.Command, s.Text, null)).ToList();
            if (_app.Macros.Save(name, steps, out string detail)) macros++;
            else problems.Add($"{m.Name}: {detail}");
        }

        foreach (var h in plan.Highlighters)
        {
            if (_app.Clips.ImportGrammar(h.Id, h.Name, h.GrammarJson, out string detail, "import", "PSPad")) highlighters++;
            else problems.Add($"{h.Name}: {detail}");
        }

        if (plan.Keys.Count > 0)
        {
            keys = _app.Workbench.AddUserBindings([.. plan.Keys.Select(k => new KeybindingContribution { Key = k.Key, Command = k.Command })], out string? keyProblem);
            if (keyProblem is not null) problems.Add("KeyMap.ini: " + keyProblem);
        }

        _app.ReloadClips();
        return new PspadReport(connections, projects, clips, macros, highlighters, keys, problems);
    }

    private static string? PspadImportFile(string root, string name)
    {
        string? path = Directory.Exists(root) ? Directory.EnumerateFiles(root).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase)) : null;
        return path is null ? null : PspadText.Decode(File.ReadAllBytes(path));
    }
}
