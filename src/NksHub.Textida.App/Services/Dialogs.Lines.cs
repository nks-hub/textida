using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;

namespace NksHub.Textida.App.Services;

/// <summary>The two option dialogs of the line commands. Each shows a live preview of what the current choice would produce.</summary>
internal sealed partial class AvaloniaDialogs
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");

    /// <summary>Called with a dialog window just before it is shown; tests use it to drive the real controls.</summary>
    internal Action<Window>? Shown { get; set; }

    private static Grid Row(string label, Control input)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(input, 1);
        AutomationProperties.SetName(input, label);
        return new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 10, Children = { text, input } };
    }

    /// <summary>Check boxes side by side; a longer text moves one to the next line instead of clipping it.</summary>
    private static WrapPanel SideBySide(params CheckBox[] boxes)
    {
        var panel = new WrapPanel();
        foreach (var box in boxes)
        {
            box.Margin = new Thickness(0, 0, 16, 0);
            panel.Children.Add(box);
        }
        return panel;
    }

    private static RadioButton Choice(string group, string label, bool on) =>
        new() { GroupName = group, Content = label, IsChecked = on, Margin = new Thickness(0, 0, 12, 0) };

    private static (TextBlock Shown, Border Frame) PreviewBox(string label)
    {
        var shown = new TextBlock { FontFamily = Mono, FontSize = 12, MinHeight = 90, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(10, 6) };
        AutomationProperties.SetName(shown, label);
        AutomationProperties.SetLiveSetting(shown, AutomationLiveSetting.Polite);
        return (shown, new Border { Child = shown, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, CornerRadius = new CornerRadius(4), ClipToBounds = true });
    }

    private static TextBox Field(string text, double width = double.NaN) => new() { Text = text, Width = width, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 60 };

    private static int Number(TextBox box, int fallback, int min) =>
        int.TryParse(box.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v) ? Math.Max(min, v) : fallback;

    public async Task<LineInsertOptions?> InsertIntoLinesAsync(Window owner, LineInsertOptions initial, Func<LineInsertOptions, string> preview)
    {
        var modeText = Choice("mode", Loc.Get(K.LinesInsertModeText), initial.Mode == LineInsertMode.Text);
        var modeNumbering = Choice("mode", Loc.Get(K.LinesInsertModeNumbering), initial.Mode == LineInsertMode.Numbering);
        var modeBullets = Choice("mode", Loc.Get(K.LinesInsertModeBullets), initial.Mode == LineInsertMode.Bullets);
        var text = Field(initial.Text);
        var start = Field(initial.Start.ToString(CultureInfo.InvariantCulture), 90);
        var width = Field(initial.Width.ToString(CultureInfo.InvariantCulture), 60);
        var separator = Field(initial.Separator, 90);
        var placeStart = Choice("place", Loc.Get(K.LinesInsertPlaceStart), initial.Place == LineInsertPlace.LineStart);
        var placeEnd = Choice("place", Loc.Get(K.LinesInsertPlaceEnd), initial.Place == LineInsertPlace.LineEnd);
        var placeColumn = Choice("place", Loc.Get(K.LinesInsertPlaceColumn), initial.Place == LineInsertPlace.Column);
        var placeCarets = Choice("place", Loc.Get(K.LinesInsertPlaceCarets), initial.Place == LineInsertPlace.Selections);
        var column = Field(initial.Column.ToString(CultureInfo.InvariantCulture), 70);
        var skipIndent = new CheckBox { Content = Loc.Get(K.LinesInsertSkipIndent), IsChecked = initial.SkipIndent };
        var skipEmpty = new CheckBox { Content = Loc.Get(K.LinesInsertSkipEmpty), IsChecked = initial.SkipEmptyLines };
        var (shown, previewFrame) = PreviewBox(Loc.Get(K.LinesPreview));
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };

        var textRow = Row(Loc.Get(K.LinesInsertText), text);
        var startRow = Row(Loc.Get(K.LinesInsertStart), start);
        var widthRow = Row(Loc.Get(K.LinesInsertWidth), width);
        var separatorRow = Row(Loc.Get(K.LinesInsertSeparator), separator);
        var columnRow = Row(Loc.Get(K.LinesInsertColumn), column);
        AutomationProperties.SetName(modeText, Loc.Get(K.LinesInsertModeText));

        LineInsertOptions Current() => new(
            modeNumbering.IsChecked == true ? LineInsertMode.Numbering : modeBullets.IsChecked == true ? LineInsertMode.Bullets : LineInsertMode.Text,
            text.Text ?? "",
            placeEnd.IsChecked == true ? LineInsertPlace.LineEnd : placeColumn.IsChecked == true ? LineInsertPlace.Column : placeCarets.IsChecked == true ? LineInsertPlace.Selections : LineInsertPlace.LineStart,
            Number(column, 1, 1),
            skipIndent.IsChecked == true,
            skipEmpty.IsChecked == true,
            long.TryParse(start.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long first) ? first : 1,
            Number(width, 1, 1),
            separator.Text ?? "");

        void Refresh()
        {
            var o = Current();
            textRow.IsVisible = o.Mode != LineInsertMode.Numbering;
            ((TextBlock)textRow.Children[0]).Text = Loc.Get(o.Mode == LineInsertMode.Bullets ? K.LinesInsertBullet : K.LinesInsertText);
            startRow.IsVisible = widthRow.IsVisible = separatorRow.IsVisible = o.Mode == LineInsertMode.Numbering;
            columnRow.IsVisible = o.Place == LineInsertPlace.Column;
            skipIndent.IsEnabled = o.Place == LineInsertPlace.LineStart;
            skipEmpty.IsEnabled = o.Place != LineInsertPlace.Selections;
            shown.Text = preview(o);
        }

        foreach (var radio in new[] { modeText, modeNumbering, modeBullets, placeStart, placeEnd, placeColumn, placeCarets })
            radio.IsCheckedChanged += (_, _) => Refresh();
        foreach (var box in new[] { text, start, width, separator, column }) box.TextChanged += (_, _) => Refresh();
        foreach (var box in new[] { skipIndent, skipEmpty }) box.IsCheckedChanged += (_, _) => Refresh();

        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                Row(Loc.Get(K.LinesInsertMode), new WrapPanel { Children = { modeText, modeNumbering, modeBullets } }),
                textRow, startRow, widthRow, separatorRow,
                Row(Loc.Get(K.LinesInsertPlace), new WrapPanel { Children = { placeStart, placeEnd, placeColumn, placeCarets } }),
                columnRow,
                SideBySide(skipIndent, skipEmpty),
                new TextBlock { Text = Loc.Get(K.LinesPreview), Opacity = 0.7 },
                previewFrame,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        var dialog = Frame(Loc.Get(K.LinesInsertTitle), panel, 560, "edit.insertLines");
        ok.Click += (_, _) => dialog.Close(Current());
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { Refresh(); (text.IsVisible ? text : start).Focus(); };
        Shown?.Invoke(dialog);
        return await dialog.ShowDialog<LineInsertOptions?>(owner);
    }

    public async Task<SortOptions?> SortLinesAsync(Window owner, SortOptions initial, Func<SortOptions, string> preview)
    {
        var up = Choice("direction", Loc.Get(K.LinesSortAscending), !initial.Descending);
        var down = Choice("direction", Loc.Get(K.LinesSortDescending), initial.Descending);
        var byCode = Choice("method", Loc.Get(K.LinesSortByCode), initial.Method == SortMethod.Ordinal);
        var byLocale = Choice("method", Loc.Get(K.LinesSortByLocale), initial.Method == SortMethod.Locale);
        var byNumber = Choice("method", Loc.Get(K.LinesSortByNumber), initial.Method == SortMethod.Numeric);
        var locale = Field(initial.Locale, 160);
        var localeNote = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#C0392B")), FontSize = 12 };
        var matchCase = new CheckBox { Content = Loc.Get(K.LinesSortCase), IsChecked = initial.CaseSensitive };
        var duplicates = new CheckBox { Content = Loc.Get(K.LinesSortDuplicates), IsChecked = initial.RemoveDuplicates };
        var from = Field(initial.ColumnFrom.ToString(CultureInfo.InvariantCulture), 70);
        var to = Field(initial.ColumnTo.ToString(CultureInfo.InvariantCulture), 70);
        var (shown, previewFrame) = PreviewBox(Loc.Get(K.LinesPreview));
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };
        AutomationProperties.SetName(locale, Loc.Get(K.LinesSortLocale));
        var localeRow = Row(Loc.Get(K.LinesSortLocale), new StackPanel { Spacing = 2, Children = { locale, localeNote } });

        SortOptions Current() => new(
            down.IsChecked == true, matchCase.IsChecked == true, duplicates.IsChecked == true,
            byLocale.IsChecked == true ? SortMethod.Locale : byNumber.IsChecked == true ? SortMethod.Numeric : SortMethod.Ordinal,
            locale.Text ?? "", Number(from, 0, 0), Number(to, 0, 0));

        void Refresh()
        {
            var o = Current();
            localeRow.IsVisible = o.Method == SortMethod.Locale;
            bool unknown = o.Method == SortMethod.Locale && o.Culture is null;
            localeNote.Text = unknown ? Loc.Get(K.LinesSortLocaleInvalid) : "";
            ok.IsEnabled = !unknown;
            shown.Text = unknown ? "" : preview(o);
        }

        foreach (var radio in new[] { up, down, byCode, byLocale, byNumber }) radio.IsCheckedChanged += (_, _) => Refresh();
        foreach (var box in new[] { locale, from, to }) box.TextChanged += (_, _) => Refresh();
        foreach (var box in new[] { matchCase, duplicates }) box.IsCheckedChanged += (_, _) => Refresh();

        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                new TextBlock { Text = Loc.Get(K.LinesSortHint), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                new WrapPanel { Children = { up, down } },
                Row(Loc.Get(K.LinesSortBy), new WrapPanel { Children = { byCode, byLocale, byNumber } }),
                localeRow,
                SideBySide(matchCase, duplicates),
                Row(Loc.Get(K.LinesSortFrom), from),
                Row(Loc.Get(K.LinesSortTo), to),
                new TextBlock { Text = Loc.Get(K.LinesSortColumnsHint), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 },
                new TextBlock { Text = Loc.Get(K.LinesPreview), Opacity = 0.7 },
                previewFrame,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        var dialog = Frame(Loc.Get(K.LinesSortTitle), panel, 560, "edit.sort");
        ok.Click += (_, _) => dialog.Close(Current());
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { Refresh(); up.Focus(); };
        Shown?.Invoke(dialog);
        return await dialog.ShowDialog<SortOptions?>(owner);
    }
}
