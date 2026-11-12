using NksHub.Textida.Core.Data;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Editing;

internal sealed partial class JsonPrettyView
{
    private string CaretText => EditRendered(_caretLine, _caretPart)?.Text ?? "";

    private static int AlignColumn(string text, int column)
    {
        column = Math.Clamp(column, 0, text.Length);
        if (column > 0 && column < text.Length && char.IsLowSurrogate(text[column]) && char.IsHighSurrogate(text[column - 1])) column--;
        return column;
    }

    private int NextColumn(int column)
    {
        string text = CaretText;
        if (column >= text.Length) return text.Length;
        return column + (char.IsHighSurrogate(text[column]) && column + 1 < text.Length && char.IsLowSurrogate(text[column + 1]) ? 2 : 1);
    }

    private int PreviousColumn(int column)
    {
        string text = CaretText;
        if (column <= 0) return 0;
        return column - (column > 1 && char.IsLowSurrogate(text[column - 1]) && char.IsHighSurrogate(text[column - 2]) ? 2 : 1);
    }

    /// <summary>The document offset the caret stands on: inside a key or value its byte, elsewhere the start of the key or value of the line.</summary>
    public long SourceOffset
    {
        get
        {
            if (_outline is null || LineAt(_caretLine) is not { } line) return Doc.BomLength;
            long at = EditRendered(_caretLine, _caretPart) is { } r ? _outline.SourceOffsetAt(r, _caretColumn) : -1;
            return at >= 0 ? at : JsonOutline.SourceOffsetOf(line);
        }
    }

    public void MoveCaret(long line, int column, bool reveal = true, long part = 0)
    {
        long last = Math.Max(0, LineCount - 1);
        long row = RowOf(Math.Clamp(line, 0, last), part);
        (_caretLine, _caretPart) = At(row);
        _caretColumn = AlignColumn(CaretText, column);
        if (reveal)
        {
            int visible = Math.Max(1, VisibleRows);
            if (row < _top) _top = row;
            else if (row >= _top + visible) _top = row - visible + 1;
            _canvas.RevealColumn();
        }
        Refresh();
    }

    private void MoveRows(long delta)
    {
        long row = Math.Clamp(RowOf(_caretLine, _caretPart) + delta, 0, Math.Max(0, RowCount - 1));
        var (line, part) = At(row);
        MoveCaret(line, _caretColumn, part: part);
    }

    /// <summary>The column where the value piece of the caret row starts: on a continuation row of a wrapped value the caret does not go into the indent.</summary>
    private int RowStart(long line, long part) => part > 0 && EditRendered(line, part) is { Spans.Count: > 0 } r ? r.Spans[0].Column : 0;

    private void StepRight()
    {
        if (_caretColumn < RowLength(_caretLine, _caretPart))
        {
            if (_caretLine != _expanded && EditRendered(_caretLine, 0) is { } r && CutAt(r, _caretColumn) is { } cut)
            {
                // at the cut of a long key or value: wrap the line and go on inside
                _expanded = _caretLine;
                CaretToOffset(_caretLine, cut.End);
            }
            MoveCaret(_caretLine, NextColumn(_caretColumn), part: _caretPart);
            return;
        }
        MoveRows(1);
        MoveCaret(_caretLine, _caretPart > 0 ? NextColumn(RowStart(_caretLine, _caretPart)) : 0, part: _caretPart);
    }

    private void StepLeft()
    {
        if (_caretColumn > RowStart(_caretLine, _caretPart))
        {
            MoveCaret(_caretLine, PreviousColumn(_caretColumn), part: _caretPart);
            return;
        }
        // the start of a continuation row is the end of the row above: step over that character as well
        long from = _caretPart;
        MoveRows(-1);
        int end = RowLength(_caretLine, _caretPart);
        MoveCaret(_caretLine, from > 0 ? PreviousColumn(end) : end, part: _caretPart);
    }

    /// <summary>Puts the caret on the line that shows the document offset, expanding the containers that hide it, and marks the range.</summary>
    public void RevealOffset(long start, long end)
    {
        if (_outline is null) return;
        long line = _outline.LineOfOffset(start);
        while (UnfoldAround(line)) { }
        _mark = end > start ? new ByteRange(start, end) : null;
        _anchor = null;
        if (LineAt(line) is { } l && EditRendered(line, 0) is { } r && r.Spans.Any(s => s.Kind != JsonTokenKind.Punctuation && s.Source <= start))
        {
            if (JsonOutline.IsLong(l) && start >= JsonOutline.SourceOffsetOf(l)) CaretToOffset(line, Math.Min(start, l.Kind == JsonLineKind.Value ? l.ValueEnd : l.KeyEnd));
            else
            {
                int column = _outline.ColumnOfOffset(r, start);
                var last = r.Spans.Last(s => s.Kind != JsonTokenKind.Punctuation && s.Source <= start);
                MoveCaret(line, column >= 0 ? column : last.Column + last.Length);
            }
        }
        else MoveCaret(line, 0);
        _top = Math.Max(0, RowOf(_caretLine, _caretPart) - VisibleRows / 3);
        Refresh();
    }

    private bool UnfoldAround(long line)
    {
        long open = _folds.LineOfRow(_folds.RowOfLine(line));
        return open != line && _folds.Unfold(open);
    }

}
