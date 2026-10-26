using Avalonia.Controls;
using Avalonia.Media;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Shell;

/// <summary>One open document: its editor control, where it came from and a task that completes when the tab is closed (for <c>--wait</c>).</summary>
internal sealed class DocTab
{
    /// <summary>Largest file the P1.1 skeleton opens. The paged document engine arrives in P1.2.</summary>
    public const long MaxFileBytes = 4 << 20;

    public string? Path { get; }
    public int Number { get; }
    public TextBox Editor { get; }
    public TaskCompletionSource Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string? EncodingName { get; init; }

    /// <summary>Files are shown read-only until saving with byte-exact encoding and line endings exists (P1.2).</summary>
    public bool ReadOnly => Path is not null;

    public string Title => Path is null ? Loc.Get(K.TabUntitled, ("number", Number)) : System.IO.Path.GetFileName(Path);

    public DocTab(string? path, int number, string text)
    {
        Path = path;
        Number = number;
        Editor = new TextBox
        {
            Text = text,
            AcceptsReturn = true,
            AcceptsTab = true,
            IsReadOnly = path is not null,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(14, 10),
            CornerRadius = new Avalonia.CornerRadius(0),
            FontFamily = new FontFamily("Cascadia Mono, Cascadia Code, SF Mono, Menlo, Consolas, monospace"),
            FontSize = 14,
            TextWrapping = TextWrapping.NoWrap,
        };
        Editor.Classes.Add("editor");
    }

    /// <summary>1-based line and column of the caret.</summary>
    public (int Line, int Column) CaretPosition()
    {
        var text = Editor.Text ?? "";
        int caret = Math.Min(Editor.CaretIndex, text.Length);
        int line = 1, lineStart = 0;
        for (int i = 0; i < caret; i++)
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        return (line, caret - lineStart + 1);
    }

    public void GoToLine(int line)
    {
        var text = Editor.Text ?? "";
        int offset = 0;
        for (int l = 1; l < line; l++)
        {
            int nl = text.IndexOf('\n', offset);
            if (nl < 0) { offset = text.Length; break; }
            offset = nl + 1;
        }
        Editor.CaretIndex = offset;
    }
}
