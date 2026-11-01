using System.Globalization;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Agents;
using NksHub.Textida.Core.Documents;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// What an agent loads in this folder (PZ.6.6): the list of instruction files, rules and skills that apply to the active file, with the size and an estimate of the tokens of each and the sum, opened as a list in which Enter opens the file.
/// Also the plans of agents as a source of Go to Anything (<c>+</c> before the name). Both only read files; nothing is sent anywhere.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>The folder whose <c>.claude</c> holds the user's own instructions and plans; the profile folder unless a test says otherwise.</summary>
    internal string AgentHomeFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    internal static string FormatSize(long bytes) =>
        bytes < 1024 ? bytes.ToString(CultureInfo.CurrentCulture) + " B" : (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB";

    private string? AgentRoot() => FileRootFolder ?? (Active?.Path is { } path ? Path.GetDirectoryName(path) : null);

    /// <summary>The files an agent loads for the active file, for the list and for tests.</summary>
    internal IReadOnlyList<AgentFile> AgentFilesNow() =>
        AgentRoot() is { } root ? AgentFiles.Find(root, Active?.Path, AgentHomeFolder, _tokenRule) : [];

    internal CommandOutcome ShowAgentFiles()
    {
        string title = Loc.Get(K.AgentFilesTitle);
        if (AgentRoot() is null)
        {
            SetStatusMessage(Loc.Get(K.AgentFilesNoFolder));
            return CommandOutcome.Handled;
        }
        var files = AgentFilesNow();
        var rows = new List<QuickRow>();
        long tokens = files.Where(f => f.Applies).Sum(f => f.Tokens), bytes = files.Where(f => f.Applies).Sum(f => f.Bytes);
        if (files.Count > 0)
        {
            string summary = Loc.Get(K.AgentFilesSummary, ("count", files.Count(f => f.Applies)), ("size", FormatSize(bytes)), ("tokens", FormatTokens(tokens)), ("rule", TokenRuleName(_tokenRule)));
            rows.Add(new QuickRow(summary, null, null, null, false, null, summary, () => { }));
        }
        foreach (var file in files)
        {
            string category = Loc.Get(file.Kind switch { AgentFileKind.Instructions => K.AgentFilesKindInstructions, AgentFileKind.Rule => K.AgentFilesKindRule, _ => K.AgentFilesKindSkill });
            string detail = Loc.Get(K.AgentFilesDetail, ("size", FormatSize(file.Bytes)), ("tokens", FormatTokens(file.Tokens)));
            string? reason = file.Applies ? null : Loc.Get(K.AgentFilesNotApplied, ("limit", file.Limit ?? ""));
            string path = file.Path;
            rows.Add(new QuickRow(file.Display, category, detail, null, true, reason, $"{category}: {file.Display}, {detail}" + (reason is null ? "" : ", " + reason),
                () => JumpToFile(path, null), () => ReadPreview(path)));
        }
        ShowChoice(title, Loc.Get(K.AgentFilesA11y), rows, null, Loc.Get(K.AgentFilesNone));
        return CommandOutcome.Handled;
    }

    /// <summary>Go to Anything with <c>+</c>: the plans agents wrote, newest first, filtered by the words typed.</summary>
    private List<QuickRow> PlanRows(string text, out string highlight, out string? emptyText)
    {
        highlight = text;
        emptyText = Loc.Get(K.PalettePlansNone);
        string category = Loc.Get(K.PaletteCategoryPlan);
        string? root = AgentRoot();
        return [.. AgentFiles.FindPlans(root, AgentHomeFolder)
            .Select(p => (Path: p, Display: root is not null && PathMove.IsInside(p, root) ? Path.GetRelativePath(root, p).Replace('\\', '/') : p))
            .Where(p => Matches(text, p.Display)).Take(60)
            .Select(p => new QuickRow(Path.GetFileName(p.Path), category, Path.GetDirectoryName(p.Display)?.Replace('\\', '/') is { Length: > 0 } d ? d : null, null, true, null, category + ": " + p.Display,
                () => JumpToFile(p.Path, null), () => ReadPreview(p.Path)))];
    }

    internal CommandOutcome ShowAgentPlans()
    {
        ShowQuickOpen("+");
        return CommandOutcome.Handled;
    }
}
