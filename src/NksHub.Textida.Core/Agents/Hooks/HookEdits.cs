using System.Text;
using NksHub.Textida.Core.Data;

namespace NksHub.Textida.Core.Agents.Hooks;

/// <summary>A value of a handler field as the form holds it: text, a number as written, or true and false.</summary>
public readonly record struct HookValue(DataKind Kind, string Text)
{
    public static HookValue String(string text) => new(DataKind.String, text);
    public static HookValue Number(string text) => new(DataKind.Number, text);
    public static HookValue Boolean(bool value) => new(DataKind.Boolean, value ? "true" : "false");

    public string Json => Kind == DataKind.String ? JsonSplice.Quote(Text) : Text;

    internal bool SameAs(DataNode node) => node.Kind == Kind && (Kind == DataKind.Boolean ? node.Bool == (Text == "true") : node.Text == Text);
}

/// <summary>What the form edits: the event, the matcher of the group (empty: none) and the fields of the handler. A field with no value is removed; a field the draft does not name is left as it is.</summary>
public sealed class HookDraft
{
    public string Event { get; set; } = "";
    public string Matcher { get; set; } = "";
    public Dictionary<string, HookValue?> Fields { get; } = new(StringComparer.Ordinal);
}

public enum HookEditError { None, Syntax, NoEvent, NotObject, HooksNotObject, EventNotArray, HandlersNotArray, NoSuchHook }

/// <summary>The text after the edit, or the reason there is none (<see cref="Text"/> is then the text as it was).</summary>
public sealed record HookEditResult(string Text, HookEditError Error)
{
    public bool Ok => Error == HookEditError.None;
}

/// <summary>
/// Adds, changes, moves and removes hooks in the text of a hooks file. Each edit changes the place it concerns and the comma next to it; every other byte of the file stays, so the result is a small change
/// that can be reviewed. Nothing is run and nothing is written: the caller gets the new text and decides what to do with it.
/// </summary>
public static class HookEdits
{
    private sealed record Member(string Name, Func<string, string> Value);

    private static HookEditResult Fail(string text, HookEditError error) => new(text, error);

    public static HookEditResult Upsert(string text, HookProduct product, HookRef? existing, HookDraft draft, HookLimits limits)
    {
        if (string.IsNullOrWhiteSpace(draft.Event)) return Fail(text, HookEditError.NoEvent);
        if (!HookDocument.TryParse(text, product, limits, out var doc, out _)) return Fail(text, HookEditError.Syntax);
        var style = JsonStyle.Of(text);
        if (existing is null) return Add(text, doc!, style, draft, NewMembers(draft, null));
        if (doc!.Find(existing) is not { } found) return Fail(text, HookEditError.NoSuchHook);
        if (found.Event.Name == draft.Event && found.Group.Matcher == draft.Matcher) return Patch(text, product, existing, draft, limits);

        // another event or matcher: the handler leaves its group and joins the one of the draft, keeping the members the form does not edit
        var carried = NewMembers(draft, found.Handler.Node, text, style);
        var removed = Remove(text, product, existing, limits);
        if (!removed.Ok) return removed;
        if (!HookDocument.TryParse(removed.Text, product, limits, out var next, out _)) return Fail(text, HookEditError.Syntax);
        var result = Add(removed.Text, next!, JsonStyle.Of(removed.Text), draft, carried);
        return result.Ok ? result : Fail(text, result.Error);
    }

    public static HookEditResult Remove(string text, HookProduct product, HookRef target, HookLimits limits)
    {
        if (!HookDocument.TryParse(text, product, limits, out var doc, out _)) return Fail(text, HookEditError.Syntax);
        if (doc!.Find(target) is not { } found || found.Group.HooksArray is not { } handlers) return Fail(text, HookEditError.NoSuchHook);
        var eventArray = found.Event.Member.Value;
        TextEdit edit;
        if (handlers.Items.Count > 1) edit = JsonSplice.RemoveItem(handlers, found.Handler.Index);
        else if (eventArray.Items.Count > 1) edit = JsonSplice.RemoveItem(eventArray, found.Group.Index);
        else edit = JsonSplice.RemoveMember(doc.Hooks!, doc.Hooks!.Members.IndexOf(found.Event.Member));
        return new HookEditResult(edit.ApplyTo(text), HookEditError.None);
    }

    /// <summary>The members of a new handler: the carried ones of <paramref name="from"/> (as written, in their order), then the fields of the draft.</summary>
    private static List<Member> NewMembers(HookDraft draft, DataNode? from, string? fromText = null, JsonStyle? style = null)
    {
        var members = new List<Member>();
        if (from is not null)
            foreach (var m in from.Members)
            {
                if (draft.Fields.ContainsKey(m.Name)) continue;
                var node = m.Value;
                members.Add(new Member(m.Name, indent => JsonSplice.Render(fromText!, node, style!, indent)));
            }
        foreach (var (name, value) in draft.Fields)
            if (value is { } v) members.Add(new Member(name, _ => v.Json));
        // the type always comes first, as in every example of the documentation
        int type = members.FindIndex(m => m.Name == "type");
        if (type > 0) (members[0], members[type]) = (members[type], members[0]);
        return members;
    }

    private static string ObjectText(JsonStyle style, string indent, IReadOnlyList<Member> members)
    {
        string inner = indent + style.Indent;
        var sb = new StringBuilder("{").Append(style.Eol);
        for (int i = 0; i < members.Count; i++)
            sb.Append(inner).Append(JsonSplice.Quote(members[i].Name)).Append(": ").Append(members[i].Value(inner)).Append(i + 1 < members.Count ? "," : "").Append(style.Eol);
        return sb.Append(indent).Append('}').ToString();
    }

    private static string ArrayText(JsonStyle style, string indent, Func<string, string> item)
    {
        string inner = indent + style.Indent;
        return "[" + style.Eol + inner + item(inner) + style.Eol + indent + "]";
    }

    private static HookEditResult Add(string text, HookDocument doc, JsonStyle style, HookDraft draft, IReadOnlyList<Member> members)
    {
        if (doc.Root.Kind != DataKind.Object) return Fail(text, HookEditError.NotObject);
        Func<string, string> handler = indent => ObjectText(style, indent, members);
        var groupMembers = new List<Member>();
        if (draft.Matcher.Length > 0) groupMembers.Add(new Member("matcher", _ => JsonSplice.Quote(draft.Matcher)));
        groupMembers.Add(new Member("hooks", i => ArrayText(style, i, handler)));
        Func<string, string> group = indent => ObjectText(style, indent, groupMembers);
        Func<string, string> eventArray = indent => ArrayText(style, indent, group);

        TextEdit edit;
        if (doc.Root.Get("hooks") is null) edit = JsonSplice.InsertMember(text, doc.Root, "hooks", indent => ObjectText(style, indent, new[] { new Member(draft.Event, eventArray) }), style);
        else if (doc.Hooks is not { } hooks) return Fail(text, HookEditError.HooksNotObject);
        else if (hooks.Get(draft.Event) is not { } events) edit = JsonSplice.InsertMember(text, hooks, draft.Event, eventArray, style);
        else if (events.Kind != DataKind.Array) return Fail(text, HookEditError.EventNotArray);
        else
        {
            var sameMatcher = doc.Events.First(e => e.Name == draft.Event).Groups.FirstOrDefault(g => g.Matcher == draft.Matcher);
            if (sameMatcher is null) edit = JsonSplice.InsertItem(text, events, group, style);
            else if (sameMatcher.Node.Get("hooks") is null) edit = JsonSplice.InsertMember(text, sameMatcher.Node, "hooks", i => ArrayText(style, i, handler), style);
            else if (sameMatcher.HooksArray is not { } array) return Fail(text, HookEditError.HandlersNotArray);
            else edit = JsonSplice.InsertItem(text, array, handler, style);
        }
        return new HookEditResult(edit.ApplyTo(text), HookEditError.None);
    }

    /// <summary>Changes the fields of one handler in place, one edit at a time (the text is read again after each, so the places stay right).</summary>
    private static HookEditResult Patch(string text, HookProduct product, HookRef at, HookDraft draft, HookLimits limits)
    {
        string current = text;
        var style = JsonStyle.Of(text);
        foreach (var (name, value) in draft.Fields)
        {
            if (!HookDocument.TryParse(current, product, limits, out var doc, out _)) return Fail(text, HookEditError.Syntax);
            if (doc!.Find(at) is not { } found) return Fail(text, HookEditError.NoSuchHook);
            var handler = found.Handler.Node;
            int index = handler.Members.FindIndex(m => m.Name == name);
            TextEdit? edit = null;
            if (index >= 0)
            {
                if (value is null) edit = JsonSplice.RemoveMember(handler, index);
                else if (!value.Value.SameAs(handler.Members[index].Value)) edit = new TextEdit(handler.Members[index].Value.Start, handler.Members[index].Value.End, value.Value.Json);
            }
            else if (value is { } v) edit = JsonSplice.InsertMember(current, handler, name, _ => v.Json, style);
            if (edit is { } e) current = e.ApplyTo(current);
        }
        return new HookEditResult(current, HookEditError.None);
    }
}
