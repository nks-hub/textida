using System.Text;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
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

    /// <summary>Asks how many paragraphs, sentences or words, and writes that much placeholder text at every caret (a selection is replaced); one Undo step.</summary>
    internal CommandOutcome InsertLorem(LoremUnit unit)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        _ = InsertLoremAsync(surface, unit);
        return CommandOutcome.Handled;
    }

    private async Task InsertLoremAsync(EditorSurface surface, LoremUnit unit)
    {
        var answer = await _app.Dialogs.PromptAsync(this, Loc.Get(K.LoremTitle), Loc.Get(K.LoremLabel, ("unit", Loc.Get(unit switch { LoremUnit.Words => K.LoremWords, LoremUnit.Sentences => K.LoremSentences, _ => K.LoremParagraphs }))), "3");
        if (answer is null) return;
        if (!int.TryParse(answer.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int count) || count < 1 || count > LoremIpsum.MaxCount)
        {
            SetStatusMessage(Loc.Get(K.LoremInvalid, ("max", LoremIpsum.MaxCount)));
            return;
        }
        if (!ReferenceEquals(ActiveEditor, surface) || surface.IsReadOnly || Active is not { } tab) return;
        string text = LoremIpsum.Generate(unit, count, tab.Doc.NewLine(surface.Settings.ConfiguredEol));
        RunEdit((s, sels) => AvaloniaEditAdapter.LineOperations.ReplaceSelections(s.Document, sels, _ => text));
    }

    /// <summary>Says what the character after the caret (the one before it at the end of the text) is: code point, name, category and its bytes in UTF-8, UTF-16 and the encoding of the file.</summary>
    internal CommandOutcome InspectCharacter()
    {
        if (ActiveEditor is not { } surface || Active is not { } tab) return CommandOutcome.NotHandled;
        string text = surface.Document.Text;
        int at = Math.Min(surface.CaretOffset, text.Length);
        var info = CharacterInfo.At(text, at) ?? (at > 0 ? CharacterInfo.At(text, char.IsLowSurrogate(text[at - 1]) && at > 1 ? at - 2 : at - 1) : null);
        if (info is null)
        {
            SetStatusMessage(Loc.Get(K.CharInspectNone));
            return CommandOutcome.Handled;
        }
        string inFile;
        try { inFile = CharacterInfo.Hex(tab.Doc.Doc.Codec.Encode(char.ConvertFromUtf32(info.CodePoint))); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or EncoderFallbackException) { inFile = Loc.Get(K.CharInspectNotInFile); }
        string message = Loc.Get(K.CharInspectText, ("codepoint", info.CodePointText), ("name", info.Name ?? Loc.Get(K.CharInspectNoName)),
            ("category", info.CategoryCode + " " + Loc.Get("unicode.category." + info.CategoryCode)), ("utf8", info.Utf8.Length == 0 ? Loc.Get(K.CharInspectNoUtf8) : CharacterInfo.Hex(info.Utf8)), ("utf16", CharacterInfo.Hex(info.Utf16)),
            ("encoding", tab.Doc.Doc.Codec.Name), ("file", inFile));
        _ = _app.Dialogs.ChooseAsync(this, new ChoiceRequest(Loc.Get(K.CharInspectTitle), message, [new DialogButton(Loc.Get(K.DialogClose), IsDefault: true, IsCancel: true)]));
        return CommandOutcome.Handled;
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
