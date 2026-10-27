using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Conversions;
using NksHub.Textida.PluginHost.Conversions;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Shell;

/// <summary>One entry of the list of conversions: the special conversions of the editor and the converters of the data plugins. <paramref name="Key"/> finds it again for "repeat last command".</summary>
internal sealed record ConversionChoice(string Key, string Title, string Origin, Func<string, ConversionContext, ConversionResult> Convert, string? Sample);

/// <summary>
/// Conversions and edit recipes over the open file. A conversion changes the text of every selection (the whole file when nothing is selected) and leaves
/// the selections on the new text; a recipe chooses parts of the file, changes them and shows what will change before it is written. Both are one undo step
/// through <see cref="RunEdit"/>. The text work is in <see cref="TextConversion"/> and <see cref="RecipeRunner"/>.
/// </summary>
internal sealed partial class MainWindow
{
    private const int RecipePreviewLines = 50;
    private const int RecipeSnippetLength = 40;

    // ---- conversions ----

    private List<ConversionChoice> ConversionChoices()
    {
        var list = new List<ConversionChoice>();
        foreach (var s in _app.SpecialConversions) list.Add(SpecialChoice(s));
        foreach (var e in _app.Clips.Converters) list.Add(ConverterChoice(e));
        return list;
    }

    private ConversionChoice SpecialChoice(SpecialConversion s) =>
        new("special/" + s.Id, _app.Workbench.Strings.Get(s.TitleKey), Loc.Get(K.ConvertOriginBuiltin),
            (t, at) => s.Convert(t, at) is { } converted ? new ConversionResult(converted, null) : new ConversionResult(null, Loc.Get(K.ConvertSpecialRefused)), s.Sample);

    private static ConversionChoice ConverterChoice(ConverterEntry e) =>
        new($"{e.PluginId}/{e.Converter.Id}", e.Converter.Title, e.PluginName, (t, _) => TextConversion.Apply(e.Converter, t), e.Converter.Samples.FirstOrDefault()?.Input);

    /// <summary>The command of one special conversion: converts every selection at once, without the list.</summary>
    internal CommandOutcome ConvertSpecial(SpecialConversion conversion) => ConvertSelections(SpecialChoice(conversion));

    /// <summary>Edit, Convert: the list of conversions with a preview on the selected text (or the first sample of the converter); the chosen one runs. While repeating, the last one runs at once.</summary>
    internal CommandOutcome ShowConversions(bool repeat)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        ReportLibraryProblems();
        var choices = ConversionChoices();
        if (repeat && choices.FirstOrDefault(c => c.Key == _app.LastParameters.Conversion) is { } last) return ConvertSelections(last);

        var selected = SelectionTexts().FirstOrDefault(t => t.Length > 0);
        // shortcut: the preview counts tab stops from column 0, so Tabs to Spaces on a selection that starts inside a line may preview other widths than it writes
        var at = new ConversionContext(TabSizeOf(surface), 0);
        var rows = choices.Select(c => new QuickRow(c.Title, c.Origin, null, null, true, null, $"{c.Title}, {c.Origin}",
            () => ConvertSelections(c), () => ConversionPreview(c, selected ?? c.Sample ?? "", at))).ToList();
        ShowChoice(Loc.Get(K.ConvertWatermark), Loc.Get(K.A11yConvert), rows, emptyText: Loc.Get(K.ConvertEmpty));
        return CommandOutcome.Handled;
    }

    private static int TabSizeOf(EditorSurface surface) => Math.Max(1, surface.Settings.TabSize);

    private static string ConversionPreview(ConversionChoice choice, string text, ConversionContext at)
    {
        var result = choice.Convert(text, at);
        return result.Problem is { } why ? Loc.Get(K.ConvertFailed, ("title", choice.Title), ("reason", why)) : FirstLines(result.Text!);
    }

    /// <summary>
    /// Converts the text of every selection, or the whole file when nothing is selected, and keeps the new text selected. A selection whose conversion fails stops
    /// the whole command before anything is written, so a failure never leaves a half converted file.
    /// </summary>
    internal CommandOutcome ConvertSelections(ConversionChoice choice)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface) return CommandOutcome.NotHandled;
        var document = surface.Document;
        var targets = surface.GetSelections().Where(s => !s.IsEmpty).OrderBy(s => s.Start).ToList();
        if (targets.Count == 0) targets = [new Sel(0, document.TextLength)];

        var results = new List<string>(targets.Count);
        bool changed = false;
        int tabSize = TabSizeOf(surface), lineStart = -1, scanned = 0, column = 0;
        foreach (var target in targets)
        {
            // the visual column of each start, counted on from the previous selection of the same line so many carets on one long line stay linear
            var line = document.GetLineByOffset(target.Start);
            if (line.Offset != lineStart) (lineStart, scanned, column) = (line.Offset, line.Offset, 0);
            for (; scanned < target.Start; scanned++) column = document.GetCharAt(scanned) == '\t' ? column + tabSize - column % tabSize : column + 1;

            var original = document.GetText(target.Start, target.Length);
            var result = choice.Convert(original, new ConversionContext(tabSize, column));
            if (result.Problem is { } why)
            {
                SetStatusMessage(Loc.Get(K.ConvertFailed, ("title", choice.Title), ("reason", why)));
                return CommandOutcome.Handled;
            }
            results.Add(result.Text!);
            changed |= result.Text != original;
        }
        if (!changed)
        {
            SetStatusMessage(Loc.Get(K.StatusNothingToChange));
            return CommandOutcome.Handled;
        }
        _app.LastParameters.Conversion = choice.Key;
        return RunEdit((s, _) => LineOperations.ReplaceSelections(s.Document, targets, i => results[i], selectNew: true));
    }

    // ---- recipes ----

    /// <summary>Edit, Run Edit Recipe: the list of recipes with a preview of what each would change in this file; the chosen one is shown in full and written when confirmed. While repeating, the last one is written at once.</summary>
    internal CommandOutcome ShowRecipes(bool repeat)
    {
        if (ActiveEditor is not { IsReadOnly: false }) return CommandOutcome.NotHandled;
        ReportLibraryProblems();
        if (repeat && _app.Clips.Recipes.FirstOrDefault(r => RecipeKey(r) == _app.LastParameters.Recipe) is { } last)
        {
            _ = RunRecipeAsync(last, confirm: false);
            return CommandOutcome.Handled;
        }
        var rows = _app.Clips.Recipes.Select(e => new QuickRow(e.Recipe.Title, e.PluginName, null, null, true, null, $"{e.Recipe.Title}, {e.PluginName}",
            () => _ = RunRecipeAsync(e, confirm: true), () => RecipePreview(e))).ToList();
        ShowChoice(Loc.Get(K.RecipesWatermark), Loc.Get(K.A11yRecipes), rows, emptyText: Loc.Get(K.RecipesEmpty));
        return CommandOutcome.Handled;
    }

    private static string RecipeKey(RecipeEntry e) => $"{e.PluginId}/{e.Recipe.Id}";

    /// <summary>A recipe run over the current text of the editor; <see cref="Result"/> says what would change and <see cref="Text"/> is the text it was planned on.</summary>
    private sealed record RecipePlan(RecipeResult Result, string Text);

    private RecipePlan? PlanRecipe(RecipeEntry entry, EditorSurface surface)
    {
        if (Active is not { } tab) return null;
        var text = surface.Document.Text;
        var ranges = surface.GetSelections().Where(s => !s.IsEmpty).OrderBy(s => s.Start).Select(s => (s.Start, s.End)).ToList();
        var context = _app.VariableContextFor(tab.Path, "", out var broken);
        if (broken is not null) return new RecipePlan(new RecipeResult([], Loc.Get(K.ClipVariablesBroken, ("reason", broken))), text);
        return new RecipePlan(RecipeRunner.Run(entry.Recipe, entry.Converters, text, ranges, context, tab.Doc.NewLine(surface.Settings.ConfiguredEol)), text);
    }

    private string RecipePreview(RecipeEntry entry)
    {
        if (ActiveEditor is not { } surface || PlanRecipe(entry, surface) is not { } plan) return "";
        if (plan.Result.Problem is { } why) return Loc.Get(K.RecipeFailed, ("title", entry.Recipe.Title), ("reason", why));
        var lines = plan.Result.Changes.Take(PreviewLines).Select(c => ChangeLine(surface, plan.Text, c, withPosition: false));
        return string.Join("\n", [Loc.Plural(K.RecipeChanges, plan.Result.Changes.Count, ("title", entry.Recipe.Title)), .. lines]);
    }

    private static string Snippet(string text)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= RecipeSnippetLength ? flat : flat[..RecipeSnippetLength] + "…";
    }

    private static string ChangeLine(EditorSurface surface, string text, RecipeChange change, bool withPosition)
    {
        var before = Snippet(text.Substring(change.Start, change.Length));
        var after = Snippet(change.Text);
        if (!withPosition) return Loc.Get(K.RecipeLinePlain, ("before", before), ("after", after));
        var at = surface.Document.GetLocation(change.Start);
        return Loc.Get(K.RecipeLine, ("line", at.Line), ("column", at.Column), ("before", before), ("after", after));
    }

    /// <summary>
    /// Plans the recipe, shows every change (unless <paramref name="confirm"/> is off) and writes all of them as one undo step; the new text stays selected.
    /// Nothing is written when the recipe cannot run, when it changes nothing, when the user cancels, or when the file changed while the preview was open.
    /// </summary>
    internal async Task RunRecipeAsync(RecipeEntry entry, bool confirm)
    {
        if (ActiveEditor is not { IsReadOnly: false } surface || PlanRecipe(entry, surface) is not { } plan) return;
        var title = entry.Recipe.Title;
        if (plan.Result.Problem is { } why) { SetStatusMessage(Loc.Get(K.RecipeFailed, ("title", title), ("reason", why))); return; }
        var changes = plan.Result.Changes;
        if (changes.Count == 0) { SetStatusMessage(Loc.Get(K.StatusNothingToChange)); return; }

        if (confirm)
        {
            var lines = changes.Take(RecipePreviewLines).Select(c => ChangeLine(surface, plan.Text, c, withPosition: true)).ToList();
            if (changes.Count > lines.Count) lines.Add(Loc.Get(K.PreviewMore, ("count", changes.Count - lines.Count)));
            var request = new ChoiceRequest(Loc.Get(K.PreviewTitle), Loc.Plural(K.RecipeChanges, changes.Count, ("title", title)),
                [new DialogButton(Loc.Get(K.PreviewApply), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)], lines);
            if (await _app.Dialogs.ChooseAsync(this, request) != 0) return;
        }
        if (!ReferenceEquals(ActiveEditor, surface) || surface.IsReadOnly || surface.Document.Text != plan.Text)
        {
            SetStatusMessage(Loc.Get(K.RecipeStale));
            return;
        }
        _app.LastParameters.Recipe = RecipeKey(entry);
        var targets = changes.Select(c => new Sel(c.Start, c.Start + c.Length)).ToList();
        RunEdit((s, _) => LineOperations.ReplaceSelections(s.Document, targets, i => changes[i].Text, selectNew: true));
    }
}
