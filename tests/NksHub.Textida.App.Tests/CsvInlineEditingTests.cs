using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using NksHub.Textida.App.Editing;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Documents;

namespace NksHub.Textida.App.Tests;

[Collection("LocState")]
public sealed class CsvInlineEditingTests : IDisposable
{
    private readonly TempDir temporary = new();
    private readonly App app;
    private readonly MainWindow window;

    public CsvInlineEditingTests()
    {
        app = TestApp.Prepare(temporary, new ScriptedDialogs());
        Loc.SetLanguage("en");
        window = app.CreateWindow(null);
        window.Width = 1200; window.Height = 800;
        window.Show(); window.UpdateLayout();
    }

    public void Dispose()
    {
        TestApp.Cleanup(app);
        Loc.SetLanguage(Loc.DefaultLanguage);
        temporary.Dispose();
    }

    [AvaloniaTheory]
    [InlineData("csv", ",")]
    [InlineData("tsv", "\t")]
    public void Ctrl_A_in_the_clicked_F2_editor_replaces_only_the_current_cell(string extension, string delimiter)
    {
        string before = $"label{delimiter}value\nDemo{delimiter}1\nOther{delimiter}2\n";
        var tab = window.OpenFile(temporary.File("sample." + extension, before), null);
        Assert.True(window.SetViewMode(tab.Doc, ViewMode.Table));
        var table = Assert.IsType<CsvView>(tab.Table);
        while (!table.RowsComplete) table.Tick();
        table.MoveTo(0, 1);
        Assert.True(table.Focus());
        Layout();
        var selection = table.Selection;
        Assert.True(selection.IsSingleCell);
        Assert.Equal("1", table.CellText(0, 1));

        TestApp.Press(window, Key.F2);
        Layout();
        Assert.True(table.IsEditing);
        var editor = Assert.Single(table.GetVisualDescendants().OfType<TextBox>(), box => box.IsVisible);
        Assert.Equal("1", editor.Text);
        var point = editor.TranslatePoint(new Point(editor.Bounds.Width - 8, editor.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Layout();
        Assert.Same(editor, window.FocusManager!.GetFocusedElement());
        Assert.Equal(1, editor.CaretIndex);
        Assert.Equal("", editor.SelectedText);

        TestApp.Press(window, Key.A, TestApp.Mod);
        Layout();
        Assert.Equal("1", editor.SelectedText);
        Assert.Equal(selection, table.Selection);
        Assert.True(table.IsEditing);
        TestApp.Type(window, "42");
        Layout();
        Assert.Equal("42", editor.Text);
        Assert.Equal(selection, table.Selection);

        TestApp.Press(window, Key.Enter);
        Layout();
        Assert.False(table.IsEditing);
        Assert.Equal("42", table.CellText(0, 1));
        Assert.Equal("2", table.CellText(1, 1));
        Assert.Equal((1, 1), (table.Row, table.Column));
        Assert.True(table.Selection.IsSingleCell);
        string after = $"label{delimiter}value\nDemo{delimiter}42\nOther{delimiter}2\n";
        Assert.Equal(after, Encoding.UTF8.GetString(tab.Doc.Doc.ReadBytes(new ByteRange(0, tab.Doc.Doc.ByteLength))));
        Assert.Equal(1, tab.Doc.Doc.UndoDepth);
    }

    private void Layout() => StructureLayoutTests.Layout(window);
}
