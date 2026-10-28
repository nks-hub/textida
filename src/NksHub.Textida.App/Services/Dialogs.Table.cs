using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NksHub.Textida.App.Text;
using NksHub.Textida.Core.Csv;

namespace NksHub.Textida.App.Services;

/// <summary>The dialog that says how a selected text is read as a table.</summary>
internal sealed partial class AvaloniaDialogs
{
    public async Task<CsvDialect?> TableRegionAsync(Window owner, CsvDialect initial, Func<CsvDialect, string> preview)
    {
        var delimiters = new (char Delimiter, RadioButton Radio)[]
        {
            (',', Choice("delimiter", Loc.Get(K.TableDelimiterComma), initial.Delimiter == ',')),
            (';', Choice("delimiter", Loc.Get(K.TableDelimiterSemicolon), initial.Delimiter == ';')),
            ('\t', Choice("delimiter", Loc.Get(K.TableDelimiterTab), initial.Delimiter == '\t')),
            ('|', Choice("delimiter", Loc.Get(K.TableDelimiterPipe), initial.Delimiter == '|')),
        };
        if (delimiters.All(d => d.Radio.IsChecked != true)) delimiters[0].Radio.IsChecked = true;
        var quote = new CheckBox { Content = Loc.Get(K.TableQuote), IsChecked = initial.HasQuote };
        var header = new CheckBox { Content = Loc.Get(K.TableHeader), IsChecked = initial.HasHeader };
        var (shown, previewFrame) = PreviewBox(Loc.Get(K.LinesPreview));
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };

        CsvDialect Current() => new(delimiters.First(d => d.Radio.IsChecked == true).Delimiter, quote.IsChecked == true ? '"' : '\0', header.IsChecked == true);
        void Refresh() => shown.Text = preview(Current());
        foreach (var (_, radio) in delimiters) radio.IsCheckedChanged += (_, _) => Refresh();
        quote.IsCheckedChanged += (_, _) => Refresh();
        header.IsCheckedChanged += (_, _) => Refresh();

        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                new TextBlock { Text = Loc.Get(K.TableRegionHint), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.8 },
                new WrapPanel { Children = { delimiters[0].Radio, delimiters[1].Radio, delimiters[2].Radio, delimiters[3].Radio } },
                SideBySide(quote, header),
                new TextBlock { Text = Loc.Get(K.LinesPreview), Opacity = 0.7 },
                previewFrame,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        var dialog = Frame(Loc.Get(K.TableRegionTitle), panel, 560, "table.region");
        ok.Click += (_, _) => dialog.Close(Current());
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { Refresh(); delimiters.First(d => d.Radio.IsChecked == true).Radio.Focus(); };
        Shown?.Invoke(dialog);
        return await dialog.ShowDialog<CsvDialect?>(owner);
    }
}
