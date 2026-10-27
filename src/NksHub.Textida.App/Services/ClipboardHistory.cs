using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The texts copied or cut in the editor, newest first, for the clipboard history list. Kept in memory only (a copied text may be a password).
/// A text copied again moves to the top instead of being listed twice.
/// </summary>
internal sealed class ClipboardHistory(Func<int> capacity)
{
    /// <summary>A longer text is not kept. shortcut: provisional until the owner decides the largest clipboard text, then it becomes a setting.</summary>
    internal const int MaxItemLength = 1 << 20;

    private readonly List<string> _items = [];

    /// <summary>Newest first.</summary>
    public IReadOnlyList<string> Items => _items;

    public void Add(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxItemLength) return;
        _items.Remove(text);
        _items.Insert(0, text);
        int keep = Math.Max(1, capacity());
        if (_items.Count > keep) _items.RemoveRange(keep, _items.Count - keep);
    }
}

/// <summary>
/// The parameters the dialog commands were last run with, so "Repeat last command" can run them again without asking.
/// </summary>
internal sealed class LastParameters
{
    public LineInsertOptions? Insert { get; set; }
    public SortOptions? Sort { get; set; }
    public string? Ranges { get; set; }
    /// <summary>Key of the last conversion and of the last edit recipe that ran, for "repeat last command".</summary>
    public string? Conversion { get; set; }
    public string? Recipe { get; set; }
}
