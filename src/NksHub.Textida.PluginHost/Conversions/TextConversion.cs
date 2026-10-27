using NksHub.Textida.Core.Search;
using NksHub.Textida.PluginHost.Manifest;
using NksHub.Textida.Workbench.Clips;

namespace NksHub.Textida.PluginHost.Conversions;

/// <param name="Text">The converted text, or null when the conversion failed.</param>
/// <param name="Problem">Why it failed (a pattern the search dialect does not read, a pattern that took too long), or null.</param>
public sealed record ConversionResult(string? Text, string? Problem)
{
    public bool Ok => Problem is null;
}

/// <summary>Runs the rules of a converter. Literal rules and regular expressions go through the engine of the search, so a rule finds exactly what Find finds.</summary>
public static class TextConversion
{
    public static ConversionResult Apply(ConverterContribution converter, string text) => Apply(converter.Rules, text);

    public static ConversionResult Apply(IEnumerable<ConverterRule> rules, string text)
    {
        foreach (var rule in rules)
        {
            var next = RegexTools.ReplaceAll(new SearchPattern(rule.Find, rule.Regex, !rule.IgnoreCase), text, rule.Replace, out var problem);
            if (next is null) return new ConversionResult(null, problem);
            text = next;
        }
        return new ConversionResult(text, null);
    }

    /// <summary>The problems of a converter that can be found without running it on a document: the rules and the samples.</summary>
    public static IEnumerable<string> Check(ConverterContribution converter)
    {
        foreach (var rule in converter.Rules)
        {
            if (rule.Find.Length == 0) yield return "has a rule with empty find.";
            else if (rule.Regex && RegexTools.Validate(new SearchPattern(rule.Find, true, !rule.IgnoreCase)) is { } why) yield return $"has an invalid regular expression: {why}";
        }
        if (converter.Rules.Count == 0) yield return "has no rules.";
        if (converter.Rules.Count == 0 || converter.Rules.Any(r => r.Find.Length == 0)) yield break;

        int n = 0;
        foreach (var sample in converter.Samples)
        {
            n++;
            var result = Apply(converter, sample.Input);
            if (result.Problem is { } problem) yield return $"has sample {n} that cannot be converted: {problem}";
            else if (result.Text != sample.Expected) yield return $"has sample {n} that gives '{Shorten(result.Text!)}' but expects '{Shorten(sample.Expected)}'.";
        }
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "...";
}

/// <summary>A part of the document the recipe changes: <paramref name="Start"/> and <paramref name="Length"/> in the text it was run on.</summary>
public sealed record RecipeChange(int Start, int Length, string Text);

/// <param name="Changes">Parts whose text changes, ascending and disjoint; empty when the recipe changes nothing.</param>
/// <param name="Problem">Why the recipe cannot run (a variable without a value, a pattern that took too long); nothing may be written then.</param>
public sealed record RecipeResult(IReadOnlyList<RecipeChange> Changes, string? Problem)
{
    public bool Ok => Problem is null;
}

/// <summary>
/// Runs an edit recipe over the text of a file without touching the file: the selecting steps narrow the chosen parts, the changing steps give each part its new
/// text. The caller shows the result and writes it as one undo step, so a recipe can always be taken back whole.
/// </summary>
public static class RecipeRunner
{
    /// <param name="converters">The converters a <c>convert</c> step may name (those of the plugin of the recipe).</param>
    /// <param name="ranges">Chosen parts to start from as start and end offsets; none means the whole text.</param>
    /// <param name="context">The facts variables are taken from; each part gets its own <c>selection</c>, as each caret of a clip does.</param>
    /// <param name="newLine">Line break written for the line breaks of a <c>set</c> text.</param>
    public static RecipeResult Run(RecipeContribution recipe, IReadOnlyList<ConverterContribution> converters, string text,
        IReadOnlyList<(int Start, int End)> ranges, VariableContext context, string newLine)
    {
        List<(int Start, int End)> parts = ranges.Count == 0 ? [(0, text.Length)] : [.. ranges.OrderBy(r => r.Start)];
        var changes = new List<RecipeChange>();
        int first = 0;
        try
        {
            for (; first < recipe.Steps.Count && recipe.Steps[first].Selects; first++)
                parts = recipe.Steps[first].Kind == RecipeStep.Matches ? Matches(recipe.Steps[first], text, parts) : Columns(recipe.Steps[first], text, parts);
            var steps = recipe.Steps.Skip(first).ToList();
            if (steps.FindIndex(s => s.Selects) is >= 0 and var late) return Fail(first + late + 1, "a selecting step comes after a changing one");
            foreach (var (start, end) in parts)
            {
                string original = text.Substring(start, end - start), current = original;
                foreach (var step in steps)
                {
                    var next = Change(step, current, converters, context, newLine);
                    if (next.Problem is { } problem) return Fail(recipe.Steps.IndexOf(step) + 1, problem);
                    current = next.Text!;
                }
                if (current != original) changes.Add(new RecipeChange(start, end - start, current));
            }
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            return new RecipeResult([], "A pattern took too long on this text.");
        }
        catch (ArgumentException e)
        {
            return new RecipeResult([], e.Message);
        }
        return new RecipeResult(changes, null);
    }

    private static RecipeResult Fail(int step, string why) => new([], $"step {step}: {why}");

    private static ConversionResult Change(RecipeStep step, string current, IReadOnlyList<ConverterContribution> converters, VariableContext context, string newLine)
    {
        switch (step.Kind)
        {
            case RecipeStep.Convert:
                return converters.FirstOrDefault(c => c.Id == step.Converter) is { } converter
                    ? TextConversion.Apply(converter, current)
                    : new ConversionResult(null, $"no converter '{step.Converter}'");
            case RecipeStep.Replace:
                return TextConversion.Apply([new ConverterRule { Find = step.Find, Replace = step.Replacement, Regex = step.Regex, IgnoreCase = step.IgnoreCase }], current);
            default:
                var expanded = Variables.Expand(step.Text, new VariableScope(context.WithSelection(current)), newLine);
                if (expanded.SyntaxError is { } syntax) return new ConversionResult(null, syntax);
                return expanded.Missing.Count > 0
                    ? new ConversionResult(null, "no value for " + string.Join(", ", expanded.Missing))
                    : new ConversionResult(expanded.Text, null);
        }
    }

    private static List<(int Start, int End)> Matches(RecipeStep step, string text, List<(int Start, int End)> parts)
    {
        var regex = RegexTools.Create(new SearchPattern(step.Find, step.Regex, !step.IgnoreCase, step.WholeWord));
        var found = new List<(int, int)>();
        foreach (var (start, end) in parts)
            foreach (System.Text.RegularExpressions.Match m in regex.Matches(text.Substring(start, end - start)))
                if (m.Length > 0) found.Add((start + m.Index, start + m.Index + m.Length));
        return found;
    }

    /// <summary>Columns <c>from</c> to <c>to</c> of every line the parts touch, cut to the part. A line shorter than <c>from</c> gives nothing. Columns count code points, not bytes or UTF-16 units.</summary>
    private static List<(int Start, int End)> Columns(RecipeStep step, string text, List<(int Start, int End)> parts)
    {
        var found = new List<(int, int)>();
        foreach (var (start, end) in parts)
        {
            int lineStart = start;
            while (lineStart > 0 && text[lineStart - 1] is not ('\n' or '\r')) lineStart--;
            while (lineStart < end)
            {
                int lineEnd = lineStart;
                while (lineEnd < text.Length && text[lineEnd] is not ('\n' or '\r')) lineEnd++;
                int a = Advance(text, lineStart, lineEnd, step.From - 1);
                int b = step.To <= 0 ? lineEnd : Advance(text, lineStart, lineEnd, step.To);
                int s = Math.Max(a, start), e = Math.Min(b, end);
                if (a < lineEnd && e > s) found.Add((s, e));
                if (lineEnd >= text.Length) break;
                lineStart = lineEnd + (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? 2 : 1);
            }
        }
        return found;
    }

    private static int Advance(string text, int from, int limit, int codePoints)
    {
        int at = from;
        for (int i = 0; i < codePoints && at < limit; i++)
            at += char.IsHighSurrogate(text[at]) && at + 1 < limit && char.IsLowSurrogate(text[at + 1]) ? 2 : 1;
        return at;
    }

    /// <summary>The problems of a recipe that can be found without a document: unknown kinds, a bad pattern, an unknown converter, a bad template, a wrong order.</summary>
    public static IEnumerable<string> Check(RecipeContribution recipe, IReadOnlySet<string> converterIds)
    {
        if (recipe.Steps.Count == 0) yield return "has no steps.";
        bool changing = false;
        int n = 0;
        foreach (var step in recipe.Steps)
        {
            n++;
            if (step.Kind is not (RecipeStep.Matches or RecipeStep.Columns or RecipeStep.Convert or RecipeStep.Replace or RecipeStep.Set))
            {
                yield return $"step {n} has the unknown kind '{step.Kind}'.";
                continue;
            }
            if (step.Selects && changing) yield return $"step {n} selects after a changing step; selecting steps come first.";
            if (!step.Selects) changing = true;
            switch (step.Kind)
            {
                case RecipeStep.Matches or RecipeStep.Replace:
                    if (step.Find.Length == 0) yield return $"step {n} has an empty find.";
                    else if (step.Regex && RegexTools.Validate(new SearchPattern(step.Find, true, !step.IgnoreCase)) is { } why) yield return $"step {n}: invalid regular expression: {why}";
                    break;
                case RecipeStep.Columns:
                    if (step.From < 1) yield return $"step {n}: from must be 1 or more.";
                    if (step.To != 0 && step.To < step.From) yield return $"step {n}: to must be 0 (end of line) or not less than from.";
                    break;
                case RecipeStep.Convert:
                    if (!converterIds.Contains(step.Converter)) yield return $"step {n} names the unknown converter '{step.Converter}'.";
                    break;
                case RecipeStep.Set:
                    if (Variables.CheckSyntax(step.Text) is { } syntax) yield return $"step {n}: {syntax}.";
                    break;
            }
        }
        if (recipe.Steps.Count > 0 && !changing) yield return "never changes anything: add a convert, replace or set step.";
    }
}
