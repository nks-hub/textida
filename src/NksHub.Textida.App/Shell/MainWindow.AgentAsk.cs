using System.Text;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// A question about the selected range for an agent the user started (PZ.2.5). Nothing is needed for normal editing: the command does nothing until the user has chosen and started an agent, and then it
/// sends nothing before the user has seen what goes to the agent. The agent answers in the panel; a change it makes to the range comes back as a proposal in the change review.
/// </summary>
internal sealed partial class MainWindow
{
    internal const int AskMaxChars = 24_000;

    internal CommandOutcome AskAgentAboutSelection()
    {
        AgentTask = AskAgentAboutSelectionAsync();
        return CommandOutcome.Handled;
    }

    /// <summary>The prompt for the agent: where the range is, the task, the text of the range, and what is expected back. English, because it is a message to an agent, not a text of the editor.</summary>
    internal static string AskMessage(string path, int firstLine, int lastLine, string instruction, string selected) =>
        $"The user asks about lines {firstLine}-{lastLine} of the file {path}.\n\nTask: {instruction}\n\nThe lines are:\n```\n{selected}\n```\n\n" +
        "If the task asks for a change, make it by writing the whole file with fs/write_text_file, leaving everything outside these lines exactly as it is: the user sees your change in a review and decides. " +
        "If it asks for no change, answer in your message.";

    internal async Task AskAgentAboutSelectionAsync()
    {
        var acp = _app.Plugins.Acp;
        if (Active is not { Custom: null, Hex: null } tab || tab.Surface is not { } surface || tab.Paged is not null) return;
        if (!acp.IsRunning || acp.State != AgentState.Ready)
        {
            SetStatusMessage(Loc.Get(acp.State == AgentState.Working ? K.AgentBusy : K.AgentAskNeedAgent));
            return;
        }
        if (tab.Doc.IsResults || tab.Doc.Doc.FilePath is not { } path)
        {
            SetStatusMessage(Loc.Get(K.AgentShareUnavailable));
            return;
        }
        if (surface.IsMulti || surface.IsBlockSelection || surface.SelectionLength == 0)
        {
            SetStatusMessage(Loc.Get(K.AgentAskNoSelection));
            return;
        }
        string text = surface.Text;
        int start = surface.SelectionStart, end = start + surface.SelectionLength;
        string newLine = tab.Doc.NewLine(surface.Settings.ConfiguredEol);
        string selected = surface.SecretsHidden ? surface.MaskedSelection(newLine) : text[start..end];
        if (selected.Length > AskMaxChars)
        {
            SetStatusMessage(Loc.Get(K.AgentAskTooLarge, ("limit", AskMaxChars)));
            return;
        }
        int firstLine = LineOf(text, start), lastLine = LineOf(text, Math.Max(start, end - 1));
        if (await _app.Dialogs.PromptAsync(this, Loc.Get(K.AgentAskTitle), Loc.Get(K.AgentAskLabel, ("lines", $"{firstLine}-{lastLine}"), ("agent", acp.AgentName)), "") is not { } asked || asked.Trim().Length == 0) return;

        var details = new List<string> { Loc.Get(K.AgentAskDetailRange, ("file", tab.Title), ("first", firstLine), ("last", lastLine), ("chars", selected.Length)), Loc.Get(K.AgentAskDetailReview) };
        if (surface.SecretsHidden) details.Add(Loc.Get(K.AgentAskDetailSecrets));
        var confirm = new ChoiceRequest(Loc.Get(K.AgentAskTitle), Loc.Get(K.AgentAskConfirm, ("agent", acp.AgentName)),
            [new DialogButton(Loc.Get(K.AgentAskSend), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], details, "tools.agent");
        if (await _app.Dialogs.ChooseAsync(this, confirm) != 0) return;

        ToggleAgentPanel(true);
        if (!await acp.AskRangeAsync(this, tab, text, start, end, AskMessage(path, firstLine, lastLine, asked.Trim(), selected)))
            SetStatusMessage(Loc.Get(K.AgentAskNeedAgent));
    }

    private static int LineOf(string text, int offset)
    {
        int line = 1;
        for (int i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n'))) line++;
        return line;
    }
}
