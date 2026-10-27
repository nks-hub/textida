using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// What a screen reader gets from a compare tab: one read-only element whose value is the sentence for the current difference (its number, the lines it
/// covers on each side and the first line of each side, see <see cref="DiffView.Describe"/>) and whose value-changed event fires whenever that changes.
/// </summary>
internal sealed class DiffAutomationPeer(DiffView owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string _last = "";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(DiffView);

    public bool IsReadOnly => true;

    public string? Value => owner.Describe();

    /// <summary>The sides change by copying blocks, not through the reader; the call is ignored.</summary>
    public void SetValue(string? value) { }

    /// <summary>Tells the reader the sentence changed.</summary>
    public void Changed()
    {
        string now = owner.Describe();
        if (now == _last) return;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _last, now);
        _last = now;
    }
}
