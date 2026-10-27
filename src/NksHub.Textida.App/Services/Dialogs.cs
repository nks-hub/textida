using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Services;

internal sealed record DialogButton(string Label, bool IsDefault = false, bool IsCancel = false);

/// <summary>A question with a few answers; <paramref name="Details"/> is an optional scrolling list under the message.</summary>
internal sealed record ChoiceRequest(string Title, string Message, IReadOnlyList<DialogButton> Buttons, IReadOnlyList<string>? Details = null);

internal sealed record SaveAsRequest(string? SuggestedPath, TextCodec Current, IReadOnlyList<(TextCodec Codec, int Lost)> Targets);

internal sealed record SaveAsChoice(string Path, TextCodec Codec);

/// <summary>Every dialog of the app. Tests replace it with a scripted one; <see cref="AvaloniaDialogs"/> opens the real windows.</summary>
internal interface IDialogs
{
    /// <summary>Index of the pressed button; -1 when the window was closed without an answer.</summary>
    Task<int> ChooseAsync(Window owner, ChoiceRequest request);
    Task<SaveAsChoice?> SaveAsAsync(Window owner, SaveAsRequest request);
    Task<IReadOnlyList<string>> PickFilesAsync(Window owner);
    /// <summary>The folder the user chose, or null when the dialog was cancelled.</summary>
    Task<string?> PickFolderAsync(Window owner);
    /// <summary>The path of the file to write that the user chose (the dialog asks before replacing one), or null when cancelled.</summary>
    Task<string?> PickSaveFileAsync(Window owner, string title, string suggestedName);
    /// <param name="preview">Text shown under the input and refreshed on every edit, so the user sees what the answer would do before confirming.</param>
    Task<string?> PromptAsync(Window owner, string title, string label, string initial, Func<string, string>? preview = null);
    /// <summary>The encoding the user picked from <paramref name="targets"/> (lossy ones marked), or null when cancelled.</summary>
    Task<TextCodec?> PickEncodingAsync(Window owner, string title, string label, IReadOnlyList<(TextCodec Codec, int Lost)> targets, TextCodec preselected);
    Task ShowAboutAsync(Window owner);
    /// <summary>The choices of "Insert into Lines"; <paramref name="preview"/> gives the text shown for the current choices. Null when cancelled.</summary>
    Task<LineInsertOptions?> InsertIntoLinesAsync(Window owner, LineInsertOptions initial, Func<LineInsertOptions, string> preview);
    /// <summary>The rules of "Sort Lines" with a live preview of the result. Null when cancelled.</summary>
    Task<SortOptions?> SortLinesAsync(Window owner, SortOptions initial, Func<SortOptions, string> preview);
}

internal sealed partial class AvaloniaDialogs : IDialogs
{
    private static Window Frame(string title, Control content, double width = 460) => new()
    {
        Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        Classes = { "shell", "dialog" }, Content = content,
    };

    private static Button MakeButton(DialogButton b, Window dialog, int index)
    {
        var button = new Button { Content = b.Label, IsDefault = b.IsDefault, IsCancel = b.IsCancel, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => dialog.Close(index);
        return button;
    }

    public async Task<int> ChooseAsync(Window owner, ChoiceRequest request)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = request.Message, TextWrapping = TextWrapping.Wrap });
        if (request.Details is { Count: > 0 } details)
            panel.Children.Add(new ListBox { ItemsSource = details, MaxHeight = 220, Background = Brushes.Transparent });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(row);
        var dialog = Frame(request.Title, panel, request.Details is null ? 460 : 560);
        for (int i = 0; i < request.Buttons.Count; i++) row.Children.Add(MakeButton(request.Buttons[i], dialog, i));
        var result = await dialog.ShowDialog<int?>(owner);
        return result ?? -1;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = Loc.Get(K.DialogOpenTitle), AllowMultiple = true });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    public async Task<string?> PickFolderAsync(Window owner)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.Get(K.DialogOpenFolderTitle), AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(Window owner, string title, string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggestedName, ShowOverwritePrompt = true });
        return file?.TryGetLocalPath();
    }

    public async Task<TextCodec?> PickEncodingAsync(Window owner, string title, string label, IReadOnlyList<(TextCodec Codec, int Lost)> targets, TextCodec preselected)
    {
        var items = targets.Select(t => t.Lost > 0 ? $"{t.Codec.Name} · {Loc.Plural(K.EncodingLossy, t.Lost)}" : t.Codec.Name).ToList();
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = Math.Max(0, targets.ToList().FindIndex(t => t.Codec == preselected)), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(combo, label);
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };
        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                combo,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        var dialog = Frame(title, panel, 420);
        ok.Click += (_, _) => dialog.Close(combo.SelectedIndex >= 0 ? targets[combo.SelectedIndex].Codec : null);
        cancel.Click += (_, _) => dialog.Close(null);
        return await dialog.ShowDialog<TextCodec?>(owner);
    }

    public async Task<string?> PromptAsync(Window owner, string title, string label, string initial, Func<string, string>? preview = null)
    {
        var input = new TextBox { Text = initial, MinWidth = 260 };
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };
        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                new TextBlock { Text = label },
                input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        if (preview is not null)
        {
            var shown = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, MinHeight = 18, Text = preview(initial) };
            AutomationProperties.SetName(shown, Loc.Get(K.HexGotoPreviewA11y));
            AutomationProperties.SetLiveSetting(shown, AutomationLiveSetting.Polite);
            input.TextChanged += (_, _) =>
            {
                shown.Text = preview(input.Text ?? "");
                AutomationProperties.SetHelpText(input, shown.Text);
            };
            panel.Children.Insert(2, shown);
        }
        var dialog = Frame(title, panel, 360);
        ok.Click += (_, _) => dialog.Close(input.Text ?? "");
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
        return await dialog.ShowDialog<string?>(owner);
    }

    public async Task<SaveAsChoice?> SaveAsAsync(Window owner, SaveAsRequest request)
    {
        var path = new TextBox { Text = request.SuggestedPath ?? "", MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
        var browse = new Button { Content = Loc.Get(K.DialogBrowse) };
        var items = request.Targets.Select(t => t.Lost > 0 ? $"{t.Codec.Name} · {Loc.Plural(K.EncodingLossy, t.Lost)}" : t.Codec.Name).ToList();
        var encoding = new ComboBox { ItemsSource = items, SelectedIndex = Math.Max(0, request.Targets.ToList().FindIndex(t => t.Codec == request.Current)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var ok = new Button { Content = Loc.Get(K.DialogSave), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };
        var pathRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { path, browse } };
        Grid.SetColumn(browse, 1);
        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 8,
            Children =
            {
                new TextBlock { Text = Loc.Get(K.DialogSaveAsPath) }, pathRow,
                new TextBlock { Text = Loc.Get(K.DialogSaveAsEncoding), Margin = new Thickness(0, 6, 0, 0) }, encoding,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0), Children = { ok, cancel } },
            },
        };
        var dialog = Frame(Loc.Get(K.DialogSaveAsTitle), panel, 520);
        browse.Click += async (_, _) =>
        {
            var file = await dialog.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get(K.DialogSaveAsTitle),
                SuggestedFileName = string.IsNullOrWhiteSpace(path.Text) ? null : System.IO.Path.GetFileName(path.Text),
            });
            if (file?.TryGetLocalPath() is { } picked) path.Text = picked;
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(path.Text) || encoding.SelectedIndex < 0) return;
            dialog.Close(new SaveAsChoice(System.IO.Path.GetFullPath(path.Text.Trim()), request.Targets[encoding.SelectedIndex].Codec));
        };
        cancel.Click += (_, _) => dialog.Close(null);
        return await dialog.ShowDialog<SaveAsChoice?>(owner);
    }

    public async Task ShowAboutAsync(Window owner)
    {
        var version = typeof(AvaloniaDialogs).Assembly.GetName().Version?.ToString(3) ?? "";
        var close = new Button { Content = Loc.Get(K.DialogAboutClose), HorizontalAlignment = HorizontalAlignment.Right, IsDefault = true, IsCancel = true };
        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children = { new TextBlock { Text = Loc.Get(K.AppName), FontSize = 20, FontWeight = FontWeight.SemiBold }, new TextBlock { Text = Loc.Get(K.DialogAboutVersion, ("version", version)) }, close },
        };
        var dialog = Frame(Loc.Get(K.DialogAboutTitle, ("name", Loc.Get(K.AppName))), panel, 340);
        close.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(owner);
    }
}
