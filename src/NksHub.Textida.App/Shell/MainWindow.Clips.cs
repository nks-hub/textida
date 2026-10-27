using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// Clips and file templates (data of the plugins in the profile): the list with a preview, the form of a clip with typed inputs, the insertion at every caret
/// as one undo step, a clip made from the selection, and a new file from a template. The text and its variables are handled by <see cref="Variables"/>.
/// </summary>
internal sealed partial class MainWindow
{
    // ---- lists ----

    /// <summary>The clips of all data plugins, with the text each would insert as a preview; the chosen one opens its form (when it has one) and is inserted.</summary>
    internal CommandOutcome ShowClips()
    {
        if (ActiveEditor is not { IsReadOnly: false }) return CommandOutcome.NotHandled;
        ReportLibraryProblems();
        var rows = _app.Clips.Clips.Select(e => new QuickRow(
            e.Clip.Title, e.Clip.Language ?? e.PluginName, FirstLines(e.Clip.Text, 1), null, true, null, $"{e.Clip.Title}, {e.PluginName}",
            () => _ = InsertClipAsync(e), () => PreviewOf(e.Clip.Text, e.Clip.Fields))).ToList();
        ShowChoice(Loc.Get(K.ClipsWatermark), Loc.Get(K.A11yClips), rows, emptyText: Loc.Get(K.ClipsEmpty));
        return CommandOutcome.Handled;
    }

    internal CommandOutcome ShowTemplates()
    {
        ReportLibraryProblems();
        var rows = _app.Clips.Templates.Select(e => new QuickRow(
            e.Template.Title, e.PluginName, FirstLines(e.Template.Text, 1), null, true, null, $"{e.Template.Title}, {e.PluginName}",
            () => _ = NewFromTemplateAsync(e), () => PreviewOf(e.Template.Text, e.Template.Fields))).ToList();
        ShowChoice(Loc.Get(K.TemplatesWatermark), Loc.Get(K.A11yTemplates), rows, emptyText: Loc.Get(K.TemplatesEmpty));
        return CommandOutcome.Handled;
    }

    /// <summary>The command of a clip, template, converter or recipe that a data plugin named.</summary>
    internal CommandOutcome RunDataCommand(string pluginId, string itemId, string kind)
    {
        if (kind == "converter") return _app.Clips.FindConverter(pluginId, itemId) is { } converter ? ConvertSelections(ConverterChoice(converter)) : CommandOutcome.NotHandled;
        if (kind == "recipe")
        {
            if (ActiveEditor is not { IsReadOnly: false } || _app.Clips.FindRecipe(pluginId, itemId) is not { } recipe) return CommandOutcome.NotHandled;
            _ = RunRecipeAsync(recipe, confirm: true);
            return CommandOutcome.Handled;
        }
        if (kind == "template")
        {
            if (_app.Clips.FindTemplate(pluginId, itemId) is not { } t) return CommandOutcome.NotHandled;
            _ = NewFromTemplateAsync(t);
            return CommandOutcome.Handled;
        }
        if (ActiveEditor is not { IsReadOnly: false } || _app.Clips.FindClip(pluginId, itemId) is not { } c) return CommandOutcome.NotHandled;
        _ = InsertClipAsync(c);
        return CommandOutcome.Handled;
    }

    private void ReportLibraryProblems()
    {
        if (_app.Clips.Problems.Count > 0) SetStatusMessage(Loc.Get(K.ClipLibraryProblem, ("reason", _app.Clips.Problems[0])));
    }

    private static string FirstLines(string text, int lines) => string.Join(' ', text.Split('\n', lines + 1).Take(lines).Select(l => l.Trim()));

    /// <summary>What the list shows for a clip: the text with the starting values of its form and the selection of the first caret.</summary>
    private string PreviewOf(string text, IReadOnlyList<ClipField> fields)
    {
        var context = _app.VariableContextFor(Active?.Path, SelectionTexts().FirstOrDefault() ?? "", out _);
        var scope = new VariableScope(context);
        var result = Variables.Expand(text, new VariableScope(context, ClipFields.Defaults(fields, scope)));
        return result.Ok ? result.Text : (result.SyntaxError is { } why ? Loc.Get(K.ClipBadText, ("reason", why)) : Loc.Get(K.ClipNoValue, ("names", string.Join(", ", result.Missing))));
    }

    /// <summary>The text of each selection of the active editor, in document order.</summary>
    private List<string> SelectionTexts() =>
        ActiveEditor is { } surface ? [.. surface.GetSelections().OrderBy(s => s.Start).Select(s => surface.Document.GetText(s.Start, s.Length))] : [];

    // ---- form and expansion ----

    private static string? ProblemOf(ExpandResult result) =>
        result.SyntaxError is { } why ? Loc.Get(K.ClipBadText, ("reason", why))
        : result.Missing.Count > 0 ? Loc.Get(K.ClipNoValue, ("names", string.Join(", ", result.Missing))) : null;

    /// <summary>
    /// The values for the variables of a clip: the starting values when it has no form, otherwise what the user typed. Null when the user cancelled or a value
    /// is refused; the form is not trusted to have checked, so every value is checked again here.
    /// </summary>
    private async Task<Dictionary<string, string>?> FillFormAsync(string formTitle, string text, IReadOnlyList<ClipField> fields, VariableContext context)
    {
        var initial = ClipFields.Defaults(fields, new VariableScope(context));
        if (fields.Count == 0) return initial;
        ClipFormState Evaluate(IReadOnlyDictionary<string, string> values)
        {
            var problems = ClipFields.Validate(fields, values);
            var result = Variables.Expand(text, new VariableScope(context, values));
            return new ClipFormState(problems, result.Text, ProblemOf(result));
        }
        var answer = await _app.Dialogs.ClipFormAsync(this, formTitle, fields, initial, Evaluate);
        if (answer is null) return null;
        if (ClipFields.Validate(fields, answer) is { Count: > 0 } refused)
        {
            var (name, problem) = refused.First();
            SetStatusMessage($"{fields.First(f => f.Name == name).Label ?? name}: {ClipTexts.Problem(problem)}");
            return null;
        }
        return new Dictionary<string, string>(answer, StringComparer.Ordinal);
    }

    // ---- insert ----

    /// <summary>Asks for the form (when the clip has one), then writes the clip at every caret as one undo step. Nothing changes when any caret cannot be expanded.</summary>
    internal async Task InsertClipAsync(ClipEntry entry)
    {
        var clip = entry.Clip;
        if (ActiveEditor is not { IsReadOnly: false } surface || Active is not { } tab) return;
        if (clip.Text.Length > PluginLimitsPlaceholders.SnippetLength) { SetStatusMessage(Loc.Get(K.ClipTooBig, ("max", PluginLimitsPlaceholders.SnippetLength), ("carets", PluginLimitsPlaceholders.SnippetCarets))); return; }

        var selections = SelectionTexts();
        var context = _app.VariableContextFor(tab.Path, selections.FirstOrDefault() ?? "", out var broken);
        if (broken is not null) { SetStatusMessage(Loc.Get(K.ClipVariablesBroken, ("reason", broken))); return; }
        var values = await FillFormAsync(Loc.Get(K.ClipFormTitle, ("title", clip.Title)), clip.Text, clip.Fields, context);
        if (values is null || !ReferenceEquals(ActiveEditor, surface) || surface.IsReadOnly) return;

        selections = SelectionTexts();
        string newLine = tab.Doc.NewLine(surface.Settings.ConfiguredEol);
        var pieces = new List<ExpandResult>(selections.Count);
        foreach (var selected in selections)
        {
            var result = Variables.Expand(clip.Text, new VariableScope(context.WithSelection(selected), values), newLine);
            if (ProblemOf(result) is { } problem) { SetStatusMessage(problem); return; }
            if (result.Text.Length > PluginLimitsPlaceholders.SnippetLength || result.Carets.Count > PluginLimitsPlaceholders.SnippetCarets)
            {
                SetStatusMessage(Loc.Get(K.ClipTooBig, ("max", PluginLimitsPlaceholders.SnippetLength), ("carets", PluginLimitsPlaceholders.SnippetCarets)));
                return;
            }
            pieces.Add(result);
        }
        RunEdit((s, sels) => LineOperations.ReplaceSelectionsWithCarets(s.Document, sels, i => (pieces[i].Text, pieces[i].Carets)));
    }

    // ---- clip from the selection ----

    /// <summary>Asks for a name and adds the selected text, taken literally, to the user's library of clips.</summary>
    internal CommandOutcome SaveSelectionAsClip()
    {
        if (ActiveEditor is null) return CommandOutcome.NotHandled;
        _ = SaveSelectionAsClipAsync();
        return CommandOutcome.Handled;
    }

    private async Task SaveSelectionAsClipAsync()
    {
        var text = SelectionTexts().FirstOrDefault(t => t.Length > 0);
        if (text is null) { SetStatusMessage(Loc.Get(K.ClipNoSelection)); return; }
        var suggestion = FirstLines(text, 1);
        if (suggestion.Length > 40) suggestion = suggestion[..40].TrimEnd();
        var title = (await _app.Dialogs.PromptAsync(this, Loc.Get(K.ClipSaveTitle), Loc.Get(K.ClipSaveLabel), suggestion))?.Trim();
        if (string.IsNullOrEmpty(title)) return;
        SetStatusMessage(_app.Clips.AddUserClip(title, text, out _, out var detail) switch
        {
            SaveClipResult.Saved => Loc.Get(K.ClipSaved, ("title", title)),
            SaveClipResult.TooLong => Loc.Get(K.ClipSaveTooLong, ("max", PluginLimitsPlaceholders.SnippetLength)),
            SaveClipResult.TooLarge => Loc.Get(K.ClipSaveTooLarge, ("max", NksHub.Textida.Workbench.Contributions.ManifestLoader.MaxBytes)),
            _ => Loc.Get(K.ClipSaveFailed, ("reason", detail)),
        });
    }

    // ---- user variables ----

    /// <summary>Opens the file of the user variables in the editor (created with no variables on first use); a saved change counts from the next use.</summary>
    internal CommandOutcome EditUserVariables()
    {
        var path = Path.Combine(_app.ProfileDirectory, UserVariables.FileName);
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "{\n}\n");
            OpenFile(path, null);
        }
        catch (IOException e) { SetStatusMessage(e.Message); }
        catch (UnauthorizedAccessException e) { SetStatusMessage(e.Message); }
        return CommandOutcome.Handled;
    }

    // ---- new file from a template ----

    /// <summary>A new tab with the template's text; the carets stand at its marks (at the start when it has none).</summary>
    internal async Task NewFromTemplateAsync(TemplateEntry entry)
    {
        var template = entry.Template;
        var context = _app.VariableContextFor(null, "", out var broken);
        if (broken is not null) { SetStatusMessage(Loc.Get(K.ClipVariablesBroken, ("reason", broken))); return; }
        var values = await FillFormAsync(Loc.Get(K.TemplateFormTitle, ("title", template.Title)), template.Text, template.Fields, context);
        if (values is null) return;
        var scope = new VariableScope(context, values);
        if (ProblemOf(Variables.Expand(template.Text, scope)) is { } problem) { SetStatusMessage(problem); return; }

        var tab = NewDocument();
        if (tab.Surface is not { } surface) return;
        var result = Variables.Expand(template.Text, scope, tab.Doc.NewLine(surface.Settings.ConfiguredEol));
        surface.Document.Text = result.Text;
        surface.SetSelections(result.Carets.Count == 0 ? [Sel.At(0)] : [.. result.Carets.Select(Sel.At)]);
        FocusView(tab);
    }
}
