using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using NksHub.Textida.App.Text;
using NksHub.Textida.Workbench.Clips;

namespace NksHub.Textida.App.Services;

/// <summary>What the form of a clip shows for the current values: the refused fields, the text the clip would insert, and a problem of the clip itself (or null).</summary>
internal sealed record ClipFormState(IReadOnlyDictionary<string, FieldProblem> Problems, string Preview, string? Error);

internal static class ClipTexts
{
    public static string Problem(FieldProblem problem) => Loc.Get(problem switch
    {
        FieldProblem.Empty => K.ClipProblemEmpty,
        FieldProblem.NotInteger => K.ClipProblemInteger,
        FieldProblem.NotNumber => K.ClipProblemNumber,
        FieldProblem.NotColor => K.ClipProblemColor,
        FieldProblem.NotDate => K.ClipProblemDate,
        FieldProblem.NotChoice => K.ClipProblemChoice,
        FieldProblem.NoFile => K.ClipProblemFile,
        _ => K.ClipProblemEmpty,
    });
}

internal sealed partial class AvaloniaDialogs
{
    /// <summary>Hint shown under the input of a field whose type needs a particular form.</summary>
    private static string? Hint(ClipField f) => ClipFields.TryParseType(f.Type, out var type) ? type switch
    {
        ClipFieldType.Color => Loc.Get(K.ClipHintColor),
        ClipFieldType.Date => Loc.Get(K.ClipHintDate),
        ClipFieldType.Integer => Loc.Get(K.ClipHintInteger),
        ClipFieldType.Number => Loc.Get(K.ClipHintNumber),
        _ => null,
    } : null;

    public async Task<IReadOnlyDictionary<string, string>?> ClipFormAsync(Window owner, string title, IReadOnlyList<ClipField> fields,
        IReadOnlyDictionary<string, string> initial, Func<IReadOnlyDictionary<string, string>, ClipFormState> evaluate)
    {
        var inputs = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
        var notes = new Dictionary<string, TextBlock>(StringComparer.Ordinal);
        var swatches = new Dictionary<string, Border>(StringComparer.Ordinal);
        var rows = new List<Control>();
        var refresh = new List<Action>();
        var red = new SolidColorBrush(Color.Parse("#C0392B"));

        var (shown, previewFrame) = PreviewBox(Loc.Get(K.ClipFormPreview));
        var error = new TextBlock { Foreground = red, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };

        foreach (var field in fields)
        {
            string label = string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label;
            string start = initial.TryGetValue(field.Name, out var v) ? v : "";
            Control input;
            if (field.Type == "choice")
            {
                var combo = new ComboBox { ItemsSource = field.Choices, SelectedItem = start, HorizontalAlignment = HorizontalAlignment.Stretch };
                combo.SelectionChanged += (_, _) => Update();
                inputs[field.Name] = () => combo.SelectedItem as string ?? "";
                input = combo;
            }
            else
            {
                var box = new TextBox { Text = start, MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Stretch, FontFamily = field.Type == "text" ? FontFamily.Default : Mono };
                box.TextChanged += (_, _) => Update();
                inputs[field.Name] = () => box.Text ?? "";
                input = box;
                if (field.Type == "file")
                {
                    var browse = new Button { Content = Loc.Get(K.DialogBrowse) };
                    browse.Click += async (_, _) =>
                    {
                        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = label, AllowMultiple = false });
                        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) box.Text = path;
                    };
                    Grid.SetColumn(browse, 1);
                    input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { box, browse } };
                }
                else if (field.Type == "color")
                {
                    var swatch = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Margin = new Thickness(8, 0, 0, 0) };
                    swatches[field.Name] = swatch;
                    Grid.SetColumn(swatch, 1);
                    input = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { box, swatch } };
                }
            }
            AutomationProperties.SetName(input is Grid g && g.Children[0] is { } inner ? inner : input, label);
            var note = new TextBlock { Foreground = red, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
            notes[field.Name] = note;
            var stack = new StackPanel { Spacing = 2, Children = { input } };
            if (Hint(field) is { } hint) stack.Children.Add(new TextBlock { Text = hint, Opacity = 0.65, FontSize = 12 });
            stack.Children.Add(note);
            var text = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
            Grid.SetColumn(stack, 1);
            rows.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), ColumnSpacing = 10, Children = { text, stack } });
        }

        Dictionary<string, string> Current() => inputs.ToDictionary(p => p.Key, p => p.Value(), StringComparer.Ordinal);

        void Update()
        {
            if (notes.Count == 0) return;
            var state = evaluate(Current());
            foreach (var field in fields)
            {
                var note = notes[field.Name];
                bool bad = state.Problems.TryGetValue(field.Name, out var problem);
                note.Text = bad ? ClipTexts.Problem(problem) : "";
                note.IsVisible = bad;
                if (swatches.TryGetValue(field.Name, out var swatch))
                    swatch.Background = !bad && Color.TryParse(inputs[field.Name](), out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
            }
            shown.Text = state.Preview;
            error.Text = state.Error ?? "";
            error.IsVisible = state.Error is not null;
            ok.IsEnabled = state.Problems.Count == 0 && state.Error is null;
        }

        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        foreach (var row in rows) panel.Children.Add(row);
        panel.Children.Add(error);
        panel.Children.Add(new TextBlock { Text = Loc.Get(K.ClipFormPreview), Opacity = 0.7 });
        panel.Children.Add(previewFrame);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } });

        var dialog = Frame(title, panel, 560);
        ok.Click += (_, _) => dialog.Close(Current());
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { Update(); if (inputs.Count > 0) (dialog.GetVisualDescendants().OfType<TextBox>().FirstOrDefault())?.Focus(); };
        Shown?.Invoke(dialog);
        return await dialog.ShowDialog<IReadOnlyDictionary<string, string>?>(owner);
    }
}
