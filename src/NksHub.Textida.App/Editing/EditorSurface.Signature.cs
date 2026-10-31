using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace NksHub.Textida.App.Editing;

/// <summary>The signature of the call the caret is in: <see cref="Label"/> with the character range of each parameter in it and the parameter the caret is on (-1: none).</summary>
internal sealed record SignatureInfo(string Label, IReadOnlyList<(int Start, int End)> Parameters, int Active, string? Documentation);

/// <summary>
/// The parameter info of a call: a small popup at the caret with the signature, the parameter the caret is on in bold, and the documentation under it. The caret moving from one argument to the next
/// moves the bold part; Esc, the caret leaving the call, an edit that takes the opening bracket away and the editor losing focus close it. The core draws it; a plugin only supplies the signature.
/// </summary>
internal sealed partial class EditorSurface
{
    private Popup? _signaturePopup;
    private TextBlock? _signatureText;
    private SignatureInfo? _signature;
    private int _signatureOpen = -1;

    /// <summary>The signature shown now, or null.</summary>
    public SignatureInfo? Signature => _signature;

    /// <summary>The offset of the bracket the caret is inside, looking back over the text before it; -1 when the caret is not inside a pair of brackets.</summary>
    internal static int OpenBracketBefore(string text, int caret)
    {
        int depth = 0;
        for (int i = Math.Min(caret, text.Length) - 1; i >= 0 && caret - i <= 4096; i--)
        {
            if (text[i] == ')') depth++;
            else if (text[i] == '(' && depth > 0) depth--;
            else if (text[i] == '(') return i;
        }
        return -1;
    }

    /// <summary>The number of commas between the bracket and the caret that are not inside a bracket of their own; -1 when the caret has left the call.</summary>
    internal static int ArgumentAt(string text, int open, int caret)
    {
        if (open < 0 || open >= text.Length || text[open] != '(' || caret <= open || caret > text.Length) return -1;
        int depth = 0, commas = 0;
        for (int i = open + 1; i < caret; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')') { if (depth == 0) return -1; depth--; }
            else if (text[i] == ',' && depth == 0) commas++;
        }
        return commas;
    }

    /// <summary>Shows the signature at the caret, which has to be inside a call. False when it is not or the signature is empty.</summary>
    public bool ShowSignature(SignatureInfo info)
    {
        if (info.Label.Length == 0) return false;
        string text = Document.Text;
        int open = OpenBracketBefore(text, CaretOffset);
        if (open < 0) return false;
        CloseSignature();
        _signature = info;
        _signatureOpen = open;
        _signatureText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 560, FontFamily = FontFamily, FontSize = FontSize };
        var body = new Border
        {
            Padding = new Thickness(8, 5),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = _signatureText,
        };
        body.Bind(Border.BackgroundProperty, body.GetResourceObservable("Bg1"));
        body.Bind(Border.BorderBrushProperty, body.GetResourceObservable("Border"));
        _signatureText.Bind(TextBlock.ForegroundProperty, _signatureText.GetResourceObservable("Fg"));
        AutomationProperties.SetLiveSetting(body, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(body, SignatureSpoken(info, ArgumentAt(text, open, CaretOffset)));
        _signaturePopup = new Popup
        {
            Child = body,
            PlacementTarget = TextArea.TextView,
            Placement = PlacementMode.Bottom,
            PlacementRect = CaretRectangle(),
            IsLightDismissEnabled = false,
            Focusable = false,
        };
        ((ISetLogicalParent)_signaturePopup).SetParent(TextArea.TextView);
        _signaturePopup.IsOpen = true;
        RenderSignature(ArgumentAt(text, open, CaretOffset));
        TextArea.Caret.PositionChanged += OnSignatureCaretMoved;
        Document.TextChanged += OnSignatureTextChanged;
        TextArea.LostFocus += OnSignatureLostFocus;
        TextArea.AddHandler(KeyDownEvent, OnSignatureKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        return true;
    }

    /// <summary>Closes the popup. Nothing happens when none is shown.</summary>
    public void CloseSignature()
    {
        if (_signaturePopup is null) return;
        _signaturePopup.IsOpen = false;
        ((ISetLogicalParent)_signaturePopup).SetParent(null);
        _signaturePopup = null;
        _signatureText = null;
        _signature = null;
        _signatureOpen = -1;
        TextArea.Caret.PositionChanged -= OnSignatureCaretMoved;
        Document.TextChanged -= OnSignatureTextChanged;
        TextArea.LostFocus -= OnSignatureLostFocus;
        TextArea.RemoveHandler(KeyDownEvent, OnSignatureKeyDown);
    }

    private Rect CaretRectangle()
    {
        var view = TextArea.TextView;
        var top = view.GetVisualPosition(TextArea.Caret.Position, VisualYPosition.LineTop) - view.ScrollOffset;
        var bottom = view.GetVisualPosition(TextArea.Caret.Position, VisualYPosition.LineBottom) - view.ScrollOffset;
        return new Rect(top.X, top.Y, 1, Math.Max(bottom.Y - top.Y, 1));
    }

    private void OnSignatureKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _signaturePopup is null) return;
        CloseSignature();
        e.Handled = true;
    }

    private void OnSignatureLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => CloseSignature();

    private void OnSignatureTextChanged(object? sender, EventArgs e) => FollowSignature();

    private void OnSignatureCaretMoved(object? sender, EventArgs e) => FollowSignature();

    /// <summary>Closes the popup when the caret has left the call, otherwise moves the bold part to the argument the caret is in.</summary>
    private void FollowSignature()
    {
        if (_signature is null) return;
        int argument = ArgumentAt(Document.Text, _signatureOpen, CaretOffset);
        if (argument < 0)
        {
            CloseSignature();
            return;
        }
        RenderSignature(argument);
        if (_signaturePopup is { Child: { } child }) AutomationProperties.SetName(child, SignatureSpoken(_signature, argument));
        if (_signaturePopup is not null) _signaturePopup.PlacementRect = CaretRectangle();
    }

    private static string SignatureSpoken(SignatureInfo info, int argument)
    {
        int active = argument >= 0 && argument < info.Parameters.Count ? argument : info.Active;
        var spoken = info.Label;
        if (active >= 0 && active < info.Parameters.Count && info.Parameters[active] is var (start, end) && end > start && end <= info.Label.Length)
            spoken += " — " + info.Label[start..end];
        return info.Documentation is null ? spoken : spoken + ". " + info.Documentation;
    }

    private void RenderSignature(int argument)
    {
        if (_signatureText is null || _signature is not { } info) return;
        int active = argument >= 0 && argument < info.Parameters.Count ? argument : info.Active;
        var inlines = _signatureText.Inlines!;
        inlines.Clear();
        string label = info.Label;
        if (active >= 0 && active < info.Parameters.Count && info.Parameters[active] is var (start, end) && end > start && end <= label.Length)
        {
            inlines.Add(new Run(label[..start]));
            var bold = new Run(label[start..end]) { FontWeight = FontWeight.Bold, TextDecorations = TextDecorations.Underline };
            bold.Bind(TextElement.ForegroundProperty, _signatureText.GetResourceObservable("Accent"));
            inlines.Add(bold);
            inlines.Add(new Run(label[end..]));
        }
        else inlines.Add(new Run(label));
        if (info.Documentation is { } documentation)
        {
            inlines.Add(new LineBreak());
            var muted = new Run(documentation) { FontStyle = FontStyle.Italic };
            muted.Bind(TextElement.ForegroundProperty, _signatureText.GetResourceObservable("FgMuted"));
            inlines.Add(muted);
        }
    }
}
