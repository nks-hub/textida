using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using AvaloniaEdit.Document;

namespace NksHub.Textida.AvaloniaEditAdapter;

/// <summary>
/// AvaloniaEdit passes <see cref="TextDocument.Changing"/> and <see cref="TextDocument.Changed"/> to its views through weak-event managers that call
/// each listener with <see cref="Delegate.DynamicInvoke"/>: a few microseconds and several allocations per listener and change, which is most of what a batch
/// of a thousand changes (typing at 1000 carets) spends in the text. For the length of a batch the delegate of the manager is replaced by one that calls the
/// same listeners, in the same order, directly. When anything on the way is not as expected the batch runs through the managers as before.
/// </summary>
internal static class WeakEventBypass
{
    private const string ManagerBase = "AvaloniaEdit.Utils.WeakEventManagerBase`4";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo? ChangingField = typeof(TextDocument).GetField("Changing", Any);
    private static readonly FieldInfo? ChangedField = typeof(TextDocument).GetField("Changed", Any);

    /// <summary>Starts a batch of changes of <paramref name="document"/>; dispose the result when it ends.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "The members are kept by the dependencies below; without them the batch runs through the managers.")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, ManagerBase, "AvaloniaEdit")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, ManagerBase + "+WeakHandlerList", "AvaloniaEdit")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, ManagerBase + "+WeakHandler", "AvaloniaEdit")]
    public static Scope Begin(TextDocument document)
    {
        var swaps = new List<Swap>(2);
        try
        {
            foreach (var field in new[] { ChangingField, ChangedField })
                if (field is not null && Replace(document, field) is { } swap) swaps.Add(swap);
        }
        catch (Exception e) when (e is MemberAccessException or TargetInvocationException or ArgumentException or InvalidCastException or NotSupportedException)
        {
            foreach (var swap in swaps) swap.Undo(document);
            swaps.Clear();
        }
        return new Scope(document, swaps);
    }

    /// <summary>A running batch.</summary>
    internal sealed class Scope(TextDocument document, List<Swap> swaps) : IDisposable
    {
        /// <summary>True while the listeners are called directly; false when the batch runs through the managers.</summary>
        public bool Active => swaps.Count > 0;

        public void Dispose()
        {
            foreach (var swap in swaps) swap.Undo(document);
            swaps.Clear();
        }
    }

    internal sealed record Swap(FieldInfo Field, Delegate Manager, Delegate Direct)
    {
        /// <summary>Puts the manager back where the direct delegate is now; listeners added to the event meanwhile stay.</summary>
        public void Undo(TextDocument document)
        {
            Delegate? result = null;
            if (Field.GetValue(document) is Delegate now)
                foreach (var d in now.GetInvocationList()) result = Delegate.Combine(result, ReferenceEquals(d, Direct) ? Manager : d);
            Field.SetValue(document, result);
        }
    }

    private static Swap? Replace(TextDocument document, FieldInfo field)
    {
        if (field.GetValue(document) is not Delegate current) return null;
        var list = current.GetInvocationList();
        int at = Array.FindIndex(list, IsManagerDelivery);
        if (at < 0 || Listeners(list[at], document) is not { } listeners) return null;
        var direct = new EventHandler<DocumentChangeEventArgs>((sender, e) =>
        {
            foreach (var listener in listeners) listener(sender, e);
        });
        Delegate? combined = null;
        for (int i = 0; i < list.Length; i++) combined = Delegate.Combine(combined, i == at ? direct : list[i]);
        field.SetValue(document, combined);
        return new Swap(field, list[at], direct);
    }

    /// <summary>The static delivery method of a weak-event manager of AvaloniaEdit.</summary>
    private static bool IsManagerDelivery(Delegate d) =>
        d.Target is null && d.Method is { Name: "DeliverEvent", DeclaringType: { IsGenericType: true } type } && type.GetGenericTypeDefinition().FullName == ManagerBase;

    /// <summary>The listeners the manager would call for a change of <paramref name="document"/>, in its order; null when the manager is not built as expected.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "See Begin.")]
    private static List<EventHandler<DocumentChangeEventArgs>>? Listeners(Delegate delivery, TextDocument document)
    {
        var type = delivery.Method.DeclaringType!;
        var manager = type.GetProperty("Current", Any)?.GetValue(null);
        var table = type.GetField("_sourceToWeakHandlers", Any)?.GetValue(manager);
        if (table is null) return null;
        var args = new object?[] { document, null };
        if (table.GetType().GetMethod("TryGetValue")?.Invoke(table, args) is not bool found) return null;
        var listeners = new List<EventHandler<DocumentChangeEventArgs>>();
        if (!found) return listeners;
        if (args[1]?.GetType().GetField("_handlers", Any)?.GetValue(args[1]) is not IEnumerable handlers) return null;
        foreach (var handler in handlers)
        {
            if (handler.GetType().GetField("_originalHandler", Any)?.GetValue(handler) is not WeakReference original) return null;
            if (original.Target is EventHandler<DocumentChangeEventArgs> listener) listeners.Add(listener);
            else if (original.Target is not null) return null;
        }
        return listeners;
    }
}
