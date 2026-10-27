using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AvaloniaEdit.Document;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Secrets;

namespace NksHub.Textida.App.Editing;

/// <summary>
/// Hidden values of a secrets file (<c>.env</c>): the surface draws a placeholder in place of every value, the clipboard gets the placeholder too,
/// and the minimap shows it. The text of the document is never touched. The user shows the values with a switch for the tab, or one at a time by resting the pointer on it.
/// </summary>
internal sealed partial class EditorSurface
{
    private SecretMaskGenerator? _secretMask;
    private ITextSourceVersion? _secretVersion;
    private List<SecretSpan> _secretSpans = [];
    private bool _secretsRevealed;
    private bool _secretFileSeen;
    private Action? _contentChanged;

    /// <summary>The document is a secrets file by its name.</summary>
    public bool IsSecretFile => SecretValues.IsSecretFile(Doc.Doc.FilePath);

    /// <summary>The user chose to show the values in this tab.</summary>
    public bool SecretsRevealed => _secretsRevealed;

    /// <summary>The values are hidden now.</summary>
    public bool SecretsHidden => IsSecretFile && !_secretsRevealed;

    private void InitSecrets()
    {
        _secretMask = new SecretMaskGenerator(() => SecretsHidden ? Secrets() : null);
        TextArea.TextView.ElementGenerators.Insert(0, _secretMask);
        TextArea.TextView.PointerMoved += (_, e) => RestOnSecret(e);
        TextArea.TextView.PointerExited += (_, _) => ShowSecretAt(null);
        _secretFileSeen = IsSecretFile;
        Doc.Changed += OnSecretStateMaybeChanged;
    }

    /// <summary>The name of the file changed (Save As): the values appear or go with it.</summary>
    private void OnSecretStateMaybeChanged()
    {
        if (IsSecretFile == _secretFileSeen) return;
        _secretFileSeen = IsSecretFile;
        RefreshSecrets();
    }

    /// <summary>The secrets of the text as it is now; looked for again only after an edit.</summary>
    internal IReadOnlyList<SecretSpan> Secrets()
    {
        var version = Document.Version;
        if (_secretVersion is null || !version.BelongsToSameDocumentAs(_secretVersion) || version.CompareAge(_secretVersion) != 0)
        {
            _secretSpans = SecretValues.Find(Document.Text);
            _secretVersion = version;
        }
        return _secretSpans;
    }

    /// <summary>Shows or hides the values of this tab again.</summary>
    public void ToggleSecrets()
    {
        _secretsRevealed = !_secretsRevealed;
        RefreshSecrets();
    }

    private void RefreshSecrets()
    {
        if (_secretMask is not null) _secretMask.Shown = null;
        Options.EnableTextDragDrop = !SecretsHidden; // dragging a selection out would carry the plain text
        TextArea.TextView.Redraw();
        _contentChanged?.Invoke();
    }

    private void RestOnSecret(PointerEventArgs e)
    {
        if (!SecretsHidden || TextArea.TextView.GetPositionFloor(e.GetPosition(TextArea.TextView) + TextArea.TextView.ScrollOffset) is not { } position)
        {
            ShowSecretAt(null);
            return;
        }
        ShowSecretAt(Document.GetOffset(position.Location));
    }

    /// <summary>Shows the value at the document offset in plain (the pointer rests on it), and hides the one shown before; null shows none.</summary>
    internal void ShowSecretAt(int? offset)
    {
        if (_secretMask is null) return;
        SecretSpan? next = offset is { } at && SecretsHidden ? SecretValues.SpanAt(Secrets(), at) : null;
        if (next == _secretMask.Shown) return;
        var previous = _secretMask.Shown;
        _secretMask.Shown = next;
        foreach (var span in new[] { previous, next })
            if (span is { } s) TextArea.TextView.Redraw(s.Start, s.Length);
    }

    /// <summary>
    /// Copy or cut while the values are hidden: when the selection touches one, the clipboard gets the placeholder in its place and a cut deletes nothing,
    /// so a value never leaves the editor by the clipboard. False when nothing is hidden or the selection holds no value; the caller then copies as usual.
    /// </summary>
    internal bool CopyHidden(bool cut)
    {
        if (!SecretsHidden || !HasSelection) return false;
        var spans = Secrets();
        var selected = GetSelections().Where(s => !s.IsEmpty).ToList();
        if (!selected.Any(s => SecretValues.Overlaps(spans, s.Start, s.End))) return false;
        var text = string.Join(Doc.NewLine(Settings.ConfiguredEol), selected.Select(s => SecretValues.Mask(Document.GetText(s.Start, s.Length), spans, s.Start)));
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) _ = clipboard.SetTextAsync(text);
        return true;
    }

    private bool HiddenCopyKey(KeyEventArgs e)
    {
        if (!SecretsHidden || Application.Current?.PlatformSettings?.HotkeyConfiguration is not { } keys) return false;
        bool cut = keys.Cut.Any(g => g.Matches(e));
        return (cut || keys.Copy.Any(g => g.Matches(e))) && CopyHidden(cut);
    }

    /// <summary>The minimap shows other lines than before without an edit (the values were shown or hidden).</summary>
    event Action? IMinimapHost.ContentChanged
    {
        add => _contentChanged += value;
        remove => _contentChanged -= value;
    }
}
