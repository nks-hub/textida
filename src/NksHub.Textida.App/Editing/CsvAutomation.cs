using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// What a screen reader gets from the table: one element whose value is the sentence for the current cell (row, column, column name, value),
/// whose help text says the whole row and the column, and whose value-changed event fires whenever the current cell or its value changes.
/// </summary>
internal sealed class CsvAutomationPeer(CsvView owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string _last = "";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;

    protected override string GetClassNameCore() => nameof(CsvView);

    protected override string? GetHelpTextCore() => owner.DescribeRow() + ". " + owner.DescribeColumn();

    public bool IsReadOnly => false;

    public string? Value => owner.DescribeCell();

    /// <summary>The value is changed in the cell editor, not through the reader; the call is ignored.</summary>
    public void SetValue(string? value) { }

    /// <summary>Tells the reader the sentence changed.</summary>
    public void CaretMoved()
    {
        string now = owner.DescribeCell();
        if (now == _last) return;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _last, now);
        _last = now;
    }
}
