using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Data;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>Commands that make text from the text of the editor: types from a JSON document, ready to be copied into code.</summary>
internal sealed partial class MainWindow
{
    /// <summary>The text of the selection of the active tab, or of the whole document when nothing is selected; null for a tab without a text surface.</summary>
    internal string? SelectedOrWholeText()
    {
        if (ActiveEditor is not { } surface) return null;
        var selected = surface.GetSelections().Where(s => !s.IsEmpty).ToList();
        return selected.Count == 0 ? surface.Document.Text : string.Join(Environment.NewLine, selected.Select(s => surface.Document.GetText(s.Start, s.Length)));
    }

    /// <summary>A new tab with <paramref name="text"/> in the mode of the language <paramref name="languageId"/>; the tab is not a file yet.</summary>
    internal DocTab OpenGeneratedText(string text, string languageId)
    {
        var tab = NewDocument();
        tab.Doc.ChooseLanguage(LanguageModes.ById(languageId));
        if (tab.Surface is { } surface) surface.Document.Text = text;
        FocusView(tab);
        return tab;
    }

    /// <summary>Turns the selected JSON (or the whole document) into types of <paramref name="language"/> in a new tab; the name of the root type is the one of the file.</summary>
    internal CommandOutcome JsonToTypes(TypeLanguage language)
    {
        if (SelectedOrWholeText() is not { } text) return CommandOutcome.NotHandled;
        string root = Active?.Path is { } path ? System.IO.Path.GetFileNameWithoutExtension(path) : "Root";
        try
        {
            string types = JsonTypeGenerator.Generate(text, language, root);
            OpenGeneratedText(types, language switch { TypeLanguage.Go => "go", TypeLanguage.CSharp => "csharp", _ => "typescript" });
            SetStatusMessage(Loc.Get(K.JsonTypesDone, ("language", language.ToString())));
        }
        catch (System.Text.Json.JsonException e)
        {
            SetStatusMessage(Loc.Get(K.JsonTypesInvalid, ("reason", e.Message)));
        }
        return CommandOutcome.Handled;
    }
}
