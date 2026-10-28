using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// What a screen reader gets from the hex grid: one editable element whose value is the sentence for the byte under the caret
/// (offset, byte, value, character, the selection and a change), whose help text says where the caret is (row, position in the row, column, mode),
/// and whose value-changed event fires whenever the caret, the selection or the byte changes. The bytes of a hidden value are never in either text.
/// </summary>
internal sealed class HexAutomationPeer(HexView owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string _last = "";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(HexView);

    protected override string? GetHelpTextCore() => Position(owner);

    public bool IsReadOnly => owner.ReadOnly;

    public string? Value => Describe(owner);

    /// <summary>Typing into the value is not how the hex grid is edited; the call is ignored.</summary>
    public void SetValue(string? value) { }

    /// <summary>Tells the reader the sentence changed.</summary>
    public void CaretMoved()
    {
        string now = Describe(owner);
        if (now == _last) return;
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, _last, now);
        _last = now;
    }

    /// <summary>"offset 0x00000012, byte 03, value 3, character none"; the end of the file, a selection with its range and a changed byte are said too.</summary>
    internal static string Describe(HexView view)
    {
        var info = view.Describe(view.Caret);
        string offset = view.OffsetText(info.Offset);
        if (info.AtEnd) return Loc.Get(K.HexA11yEnd, ("offset", offset));
        string text = info.Hidden ? Loc.Get(K.HexA11yHidden, ("offset", offset))
            : Loc.Get(K.HexA11yByte, ("offset", offset), ("hex", info.Value.ToString("X2")), ("value", info.Value), ("char", CharacterName(info)));
        if (view.Editor.HasSelection)
        {
            var selection = view.Editor.Selection;
            text += ", " + Loc.Plural(K.HexA11ySelection, selection.Length) + " " + Loc.Get(K.HexA11yRange, ("start", view.OffsetText(selection.Start)), ("end", view.OffsetText(selection.End - 1)));
        }
        if (info.Changed) text += ", " + Loc.Get(K.HexA11yChanged);
        return text;
    }

    /// <summary>"row 3 of 256, position 4 of 16, hex column, Edit mode: OVERWRITE": where the caret is in the grid and what typing does.</summary>
    internal static string Position(HexView view)
    {
        int width = view.BytesPerRow;
        long caret = view.Caret;
        return Loc.Get(K.HexA11yPosition, ("row", caret / width + 1), ("rows", view.RowCount), ("column", caret % width + 1), ("columns", width),
            ("part", view.ColumnText()), ("mode", view.ModeText()));
    }

    private static string CharacterName(HexByteInfo info) => info.Kind switch
    {
        HexCellKind.Control => Loc.Get(K.HexA11yControl),
        HexCellKind.Continuation => Loc.Get(K.HexA11yContinuation),
        HexCellKind.Invalid => Loc.Get(K.HexA11yInvalid),
        _ => info.Character == " " ? Loc.Get(K.HexA11ySpace) : info.Character,
    };
}

/// <summary>
/// What a screen reader gets from the map of the file: a slider whose value is the place of the grid in the file in percent, whose help text says what the cells show and
/// how many matches, bookmarks and changed places the map marks, and which the reader can move (it scrolls the grid).
/// </summary>
internal sealed class HexMinimapAutomationPeer(HexMinimap owner) : ControlAutomationPeer(owner), IRangeValueProvider
{
    private double _last = -1;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override string GetClassNameCore() => nameof(HexMinimap);

    protected override string? GetHelpTextCore() => owner.Describe();

    public bool IsReadOnly => false;

    public double Minimum => 0;

    public double Maximum => 100;

    public double Value => owner.Percent;

    public double SmallChange => 1;

    public double LargeChange => 10;

    public void SetValue(double value) => owner.ScrollToPercent(value);

    /// <summary>Tells the reader the grid moved to another place.</summary>
    public void Moved()
    {
        double now = owner.Percent;
        if (now == _last) return;
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, _last, now);
        _last = now;
    }
}
