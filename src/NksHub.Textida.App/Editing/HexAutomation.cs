using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Hex;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// What a screen reader gets from the hex grid: one editable element whose value is the sentence for the byte under the caret
/// (offset, byte, value, character) and whose value-changed event fires whenever the caret, the selection or the byte changes.
/// </summary>
internal sealed class HexAutomationPeer(HexView owner) : ControlAutomationPeer(owner), IValueProvider
{
    private string _last = "";

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;

    protected override string GetClassNameCore() => nameof(HexView);

    public bool IsReadOnly => false;

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

    /// <summary>"offset 0x00000012, byte 03, value 3, character none"; the end of the file, a selection and a changed byte are said too.</summary>
    internal static string Describe(HexView view)
    {
        var info = view.Describe(view.Caret);
        string offset = view.OffsetText(info.Offset);
        if (info.AtEnd) return Loc.Get(K.HexA11yEnd, ("offset", offset));
        string text = info.Hidden ? Loc.Get(K.HexA11yHidden, ("offset", offset))
            : Loc.Get(K.HexA11yByte, ("offset", offset), ("hex", info.Value.ToString("X2")), ("value", info.Value), ("char", CharacterName(info)));
        if (view.Editor.HasSelection) text += ", " + Loc.Plural(K.HexA11ySelection, view.Editor.Selection.Length);
        if (info.Changed) text += ", " + Loc.Get(K.HexA11yChanged);
        return text;
    }

    private static string CharacterName(HexByteInfo info) => info.Kind switch
    {
        HexCellKind.Control => Loc.Get(K.HexA11yControl),
        HexCellKind.Continuation => Loc.Get(K.HexA11yContinuation),
        HexCellKind.Invalid => Loc.Get(K.HexA11yInvalid),
        _ => info.Character == " " ? Loc.Get(K.HexA11ySpace) : info.Character,
    };
}
