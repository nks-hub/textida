using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Commands;

namespace NksHub.Textida.App.Help;

/// <summary>
/// Which help topic belongs to which part of the UI. A panel, bar or view names its topic once with <see cref="Set"/>, and F1 with the keyboard inside it opens that topic.
/// A window or dialog names the topic of the whole window with <see cref="OnF1"/>.
/// </summary>
internal static class HelpContext
{
    private static readonly ConditionalWeakTable<Control, string> Topics = new();

    /// <summary>Names the topic of <paramref name="control"/> and everything inside it; returns the control so a builder can use it in place.</summary>
    public static T Set<T>(T control, string topic) where T : Control
    {
        Topics.AddOrUpdate(control, topic);
        return control;
    }

    public static string? Of(Control control) => Topics.TryGetValue(control, out var topic) ? topic : null;

    /// <summary>The topic of the nearest part of the UI around the focused element of <paramref name="top"/>, or null when the focus is in none that names one.</summary>
    public static string? Focused(TopLevel top)
    {
        for (var visual = top.FocusManager?.GetFocusedElement() as Visual; visual is not null; visual = visual.GetVisualParent())
            if (visual is Control control && Topics.TryGetValue(control, out var topic)) return topic;
        return null;
    }

    /// <summary>The topic of the command whose handler started the current flow of work (a dialog the command opened asks for it), or null.</summary>
    public static string? OfRunningCommand() =>
        CommandRegistry.Running is { } id ? HelpCatalog.ForCommand(Loc.Language, id)?.Id : null;

    /// <summary>F1 in <paramref name="window"/> opens the help; <paramref name="topic"/> is asked when the key is pressed.</summary>
    public static void OnF1(Window window, Func<string> topic) => window.KeyDown += (_, e) =>
    {
        if (e.Key != Key.F1 || e.KeyModifiers != KeyModifiers.None || Application.Current is not App app) return;
        app.OpenHelp(topic());
        e.Handled = true;
    };

    public static void OnF1(Window window, string topic) => OnF1(window, () => topic);
}
