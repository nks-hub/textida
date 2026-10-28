using System.Globalization;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Agents.Hooks;

public enum HookSeverity { Error, Warning }

/// <summary>
/// One finding. <see cref="Code"/> is a keyword of the schema (<c>required</c>, <c>enum</c>, ...) or one of the checks of this class (<c>hook.*</c>, <c>syntax</c>);
/// <see cref="Detail"/> is English text for logs and <see cref="Args"/> are the values it is made of, so the caller can write it again in the language of the user.
/// </summary>
public sealed record HookIssue(HookSeverity Severity, string Code, string Detail, string[] Args, int Start, int End, string Pointer);

/// <summary>
/// Checks a hooks file: the JSON Schema of the product (an event the product does not have, a missing <c>command</c>, a field of the wrong type are errors; a field the documentation does not
/// list is a warning), then what the documentation says about the settings that have no effect. The command of a hook is only read as text; nothing is run.
/// </summary>
public static class HookValidator
{
    public static IReadOnlyList<HookIssue> Validate(string text, HookProduct product, HookLimits limits)
    {
        if (!HookDocument.TryParse(text, product, limits, out var doc, out var syntax))
            return [new HookIssue(HookSeverity.Error, "syntax", syntax!.Message, [], syntax.Start, syntax.End, "")];
        var catalog = HookCatalog.For(product);
        var issues = new List<HookIssue>();
        if (!JsonDataReader.TryParse(catalog.SchemaText, jsonc: false, limits.MaxDepth, out var schema, out var schemaError))
            throw new InvalidDataException("The embedded hook schema is damaged: " + schemaError!.Message);
        foreach (var i in new SchemaValidator(schema!, new SchemaOptions(limits.MaxDepth, limits.RegexTimeout)).Validate(doc!.Root))
        {
            if (i.Code == "additionalProperties" && product == HookProduct.Codex && HandlerAt(doc, i.Pointer) is { Type: "prompt" or "agent" }) continue; // the documentation does not list their fields
            issues.Add(new HookIssue(i.Code == "additionalProperties" ? HookSeverity.Warning : HookSeverity.Error, i.Code, i.Detail, i.Args ?? [], i.Start, i.End, i.Pointer));
        }
        foreach (var ev in doc.Events)
        {
            if (ev.Info is not { } info) continue;
            foreach (var group in ev.Groups)
            {
                string pointer = $"/hooks/{Escape(ev.Name)}/{group.Index.ToString(CultureInfo.InvariantCulture)}";
                CheckGroup(issues, info, group, pointer, limits);
                foreach (var handler in group.Handlers) CheckHandler(issues, catalog, info, handler, pointer + "/hooks/" + handler.Index.ToString(CultureInfo.InvariantCulture));
            }
        }
        issues.Sort((a, b) => a.Start.CompareTo(b.Start));
        return issues;
    }

    private static string Escape(string name) => name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static HookHandlerNode? HandlerAt(HookDocument doc, string pointer)
    {
        var parts = pointer.Split('/');
        if (parts.Length < 6 || parts[1] != "hooks" || parts[4] != "hooks") return null;
        if (!int.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out int g) || !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out int h)) return null;
        var name = parts[2].Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
        return doc.Find(new HookRef(name, g, h))?.Handler;
    }

    private static void Add(List<HookIssue> issues, HookSeverity severity, string code, string detail, DataNode at, string pointer, params string[] args) =>
        issues.Add(new HookIssue(severity, code, detail, args, at.Start, at.End, pointer));

    private static void CheckGroup(List<HookIssue> issues, HookEventInfo info, HookGroupNode group, string pointer, HookLimits limits)
    {
        if (group.Node.Get("matcher") is { Kind: DataKind.String } matcher && matcher.Text.Length > 0)
        {
            if (info.MatcherField is null)
                Add(issues, HookSeverity.Warning, "hook.matcherIgnored", $"The event {info.Name} has no matcher, so this one is ignored.", matcher, pointer + "/matcher", info.Name);
            else if (HookMatcher.IsBrokenPattern(info, matcher.Text, limits.RegexTimeout))
                Add(issues, HookSeverity.Warning, "hook.matcherPattern", "The matcher is read as a regular expression, and it is not a valid one.", matcher, pointer + "/matcher");
        }
        if (group.Node.Get("hooks") is { Kind: DataKind.Array, Items.Count: 0 } empty)
            Add(issues, HookSeverity.Warning, "hook.emptyGroup", "The group has no handlers, so it does nothing.", empty, pointer + "/hooks");
    }

    private static void CheckHandler(List<HookIssue> issues, HookCatalog catalog, HookEventInfo info, HookHandlerNode handler, string pointer)
    {
        string type = handler.Type;
        if (catalog.Product == HookProduct.Codex && type is "prompt" or "agent")
        {
            Add(issues, HookSeverity.Warning, "hook.skipped", $"Codex reads handlers of type {type} and skips them.", handler.Node.Get("type")!, pointer + "/type", type);
            return;
        }
        if (catalog.FindType(type) is null) return; // the schema already reported the type
        foreach (var member in handler.Node.Members)
            if (!catalog.IsKnownField(type, member.Name) && AnyTypeHas(catalog, member.Name)) // a field no type has is reported by the schema
                issues.Add(new HookIssue(HookSeverity.Warning, "hook.fieldNotForType", $"The field {member.Name} is not used by a handler of type {type}.", [member.Name, type], member.NameStart, member.NameEnd, pointer + "/" + member.Name));
        if (catalog.Product == HookProduct.ClaudeCode)
        {
            if (handler.Node.Get("if") is { } cond && !info.ToolEvent)
                Add(issues, HookSeverity.Warning, "hook.ifNeverRuns", $"The if condition is only evaluated on tool events; on {info.Name} the hook never runs.", cond, pointer + "/if", info.Name);
            if (handler.Node.Get("shell") is { } shell && handler.Node.Get("args") is not null)
                Add(issues, HookSeverity.Warning, "hook.shellIgnored", "The shell is ignored when args is set.", shell, pointer + "/shell");
        }
        else if (info.Name is "SessionEnd" or "Interrupt" && handler.Node.Get("timeout") is { Kind: DataKind.Number } timeout && timeout.Number > 3)
            Add(issues, HookSeverity.Warning, "hook.timeoutLimit", $"{info.Name} hooks support a timeout of at most 3 seconds.", timeout, pointer + "/timeout", info.Name);
    }

    private static bool AnyTypeHas(HookCatalog catalog, string field) => catalog.HandlerTypes.Any(t => catalog.IsKnownField(t.Name, field));
}
