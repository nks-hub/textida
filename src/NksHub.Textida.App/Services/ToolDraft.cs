using System.Globalization;
using NksHub.Textida.App.Text;
using NksHub.Textida.PluginHost.Manifest;

namespace NksHub.Textida.App.Services;

/// <summary>
/// A tool definition as the editor shows it: every field is text. <see cref="ToDefinition"/> turns it back into the definition of <c>tools.json</c>, so a tool edited
/// there is the same definition the launcher runs, with the same arguments. Arguments and variables are one per line; an empty argument cannot be written here.
/// </summary>
internal sealed class ToolDraft
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Executable { get; set; } = "";
    public string Args { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string Timeout { get; set; } = "";
    public string LogPattern { get; set; } = "";
    /// <summary>One variable per line: <c>Name:text</c>, <c>Name:path</c>, optionally followed by <c>=value</c>.</summary>
    public string Variables { get; set; } = "";

    public static ToolDraft From(ExternalToolContribution t) => new()
    {
        Id = t.Id, Title = t.Title, Executable = t.Executable, Args = string.Join('\n', t.Args), Cwd = t.Cwd ?? "",
        Timeout = t.TimeoutSeconds?.ToString(CultureInfo.InvariantCulture) ?? "", LogPattern = t.LogPattern ?? "",
        Variables = string.Join('\n', (t.Variables ?? []).Select(v => v.Value is null ? $"{v.Name}:{v.Type}" : $"{v.Name}:{v.Type}={v.Value}")),
    };

    /// <summary>The definition, or null with the reason (already in the language of the user) in <paramref name="error"/>.</summary>
    public ExternalToolContribution? ToDefinition(out string? error)
    {
        error = null;
        if (Id.Trim().Length == 0 || Title.Trim().Length == 0 || Executable.Trim().Length == 0)
        {
            error = Loc.Get(K.ToolEditNeeds);
            return null;
        }
        int? seconds = null;
        if (Timeout.Trim().Length > 0)
        {
            if (!int.TryParse(Timeout.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value <= 0)
            {
                error = Loc.Get(K.ToolEditTimeoutBad);
                return null;
            }
            seconds = value;
        }
        var variables = new List<ToolVariableContribution>();
        int number = 0;
        foreach (var raw in Lines(Variables))
        {
            number++;
            int equals = raw.IndexOf('=');
            string head = equals < 0 ? raw : raw[..equals];
            int colon = head.IndexOf(':');
            string type = colon < 0 ? "" : head[(colon + 1)..].Trim();
            if (colon <= 0 || type is not ("text" or "path"))
            {
                error = Loc.Get(K.ToolEditVariableBad, ("line", number));
                return null;
            }
            variables.Add(new ToolVariableContribution { Name = head[..colon].Trim(), Type = type, Value = equals < 0 ? null : raw[(equals + 1)..] });
        }
        return new ExternalToolContribution
        {
            Id = Id.Trim(), Title = Title.Trim(), Executable = Executable.Trim(), Args = [.. Lines(Args)],
            Cwd = Cwd.Trim().Length == 0 ? null : Cwd.Trim(), TimeoutSeconds = seconds,
            LogPattern = LogPattern.Trim().Length == 0 ? null : LogPattern.Trim(),
            Variables = variables.Count == 0 ? null : variables,
        };
    }

    private static IEnumerable<string> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n').Where(l => l.Trim().Length > 0);
}
