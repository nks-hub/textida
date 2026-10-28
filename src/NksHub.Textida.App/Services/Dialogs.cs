using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using NksHub.Textida.App.Help;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;
using NksHub.Textida.AvaloniaEditAdapter;
using NksHub.Textida.Core.Encodings;

namespace NksHub.Textida.App.Services;

internal sealed record DialogButton(string Label, bool IsDefault = false, bool IsCancel = false);

/// <summary>
/// A question with a few answers; <paramref name="Details"/> is an optional scrolling list under the message. <paramref name="HelpTopic"/> is the topic F1 opens;
/// without one it is the topic of the command that asked, or of where the user is.
/// </summary>
internal sealed record ChoiceRequest(string Title, string Message, IReadOnlyList<DialogButton> Buttons, IReadOnlyList<string>? Details = null, string? HelpTopic = null);

internal sealed record SaveAsRequest(string? SuggestedPath, TextCodec Current, IReadOnlyList<(TextCodec Codec, int Lost)> Targets);

internal sealed record SaveAsChoice(string Path, TextCodec Codec);

/// <summary>Every dialog of the app. Tests replace it with a scripted one; <see cref="AvaloniaDialogs"/> opens the real windows.</summary>
internal interface IDialogs
{
    /// <summary>Index of the pressed button; -1 when the window was closed without an answer.</summary>
    Task<int> ChooseAsync(Window owner, ChoiceRequest request);
    /// <summary>The same question, closed without an answer (-1) when <paramref name="cancellation"/> fires; a dialog that cannot be closed from outside stays until the user answers.</summary>
    Task<int> ChooseAsync(Window owner, ChoiceRequest request, CancellationToken cancellation) => ChooseAsync(owner, request);
    Task<SaveAsChoice?> SaveAsAsync(Window owner, SaveAsRequest request);
    Task<IReadOnlyList<string>> PickFilesAsync(Window owner);
    /// <summary>The folder the user chose, or null when the dialog was cancelled.</summary>
    Task<string?> PickFolderAsync(Window owner);
    /// <summary>The path of the file to write that the user chose (the dialog asks before replacing one), or null when cancelled.</summary>
    Task<string?> PickSaveFileAsync(Window owner, string title, string suggestedName);
    /// <param name="preview">Text shown under the input and refreshed on every edit, so the user sees what the answer would do before confirming.</param>
    Task<string?> PromptAsync(Window owner, string title, string label, string initial, Func<string, string>? preview = null);
    /// <summary>Like <see cref="PromptAsync"/> for a password: the typed characters are masked and nothing is preset. Null when cancelled. A dialog that cannot mask answers null.</summary>
    Task<string?> PromptSecretAsync(Window owner, string title, string label) => Task.FromResult<string?>(null);
    /// <summary>The encoding the user picked from <paramref name="targets"/> (lossy ones marked), or null when cancelled.</summary>
    Task<TextCodec?> PickEncodingAsync(Window owner, string title, string label, IReadOnlyList<(TextCodec Codec, int Lost)> targets, TextCodec preselected);
    Task ShowAboutAsync(Window owner);
    /// <summary>The name, colour and comment of a hex bookmark. Null when cancelled.</summary>
    Task<HexBookmarkForm?> HexBookmarkAsync(Window owner, HexBookmarkForm initial);
    /// <summary>The choices of "Insert into Lines"; <paramref name="preview"/> gives the text shown for the current choices. Null when cancelled.</summary>
    Task<LineInsertOptions?> InsertIntoLinesAsync(Window owner, LineInsertOptions initial, Func<LineInsertOptions, string> preview);
    /// <summary>The rules of "Sort Lines" with a live preview of the result. Null when cancelled.</summary>
    Task<SortOptions?> SortLinesAsync(Window owner, SortOptions initial, Func<SortOptions, string> preview);
    /// <summary>How the selected text is read as a table (delimiter, quote, header) with a live preview of the first rows. Null when cancelled.</summary>
    Task<NksHub.Textida.Core.Csv.CsvDialect?> TableRegionAsync(Window owner, NksHub.Textida.Core.Csv.CsvDialect initial, Func<NksHub.Textida.Core.Csv.CsvDialect, string> preview);
    /// <summary>The form of a clip or a file template: <paramref name="evaluate"/> gives the refused fields and the preview for the values typed so far. Null when cancelled.</summary>
    Task<IReadOnlyDictionary<string, string>?> ClipFormAsync(Window owner, string title, IReadOnlyList<NksHub.Textida.Workbench.Clips.ClipField> fields,
        IReadOnlyDictionary<string, string> initial, Func<IReadOnlyDictionary<string, string>, ClipFormState> evaluate);
}

internal sealed partial class AvaloniaDialogs : IDialogs
{
    /// <summary>Raised for every dialog window, before it is shown. The UI checks read the window from here.</summary>
    internal static event Action<Window>? DialogCreated;

    /// <summary>
    /// A dialog window. F1 opens <paramref name="helpTopic"/>; without one, the topic of the command that opened the dialog (known now, while the command's flow is current),
    /// else the topic of where the user is in the window that owns the dialog.
    /// </summary>
    private static Window Frame(string title, Control content, double width = 460, string? helpTopic = null)
    {
        var dialog = new Window
        {
            Title = title, Width = width, SizeToContent = SizeToContent.Height, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
            Classes = { "shell", "dialog" }, Content = content,
        };
        var topic = helpTopic ?? HelpContext.OfRunningCommand();
        HelpContext.OnF1(dialog, () => topic ?? (dialog.Owner as MainWindow)?.HelpContextTopic() ?? HelpCatalog.StartTopic);
        DialogCreated?.Invoke(dialog);
        return dialog;
    }

    /// <summary>Called with a dialog window just before it is shown; tests use it to drive the real controls.</summary>
    internal Action<Window>? Shown { get; set; }

    /// <summary>
    /// Shows <paramref name="dialog"/> modally. The control of the owner that had the keyboard gets it back when the dialog is gone, so a keyboard
    /// user goes on where they were. A menu item is not given back: the window sends the keyboard to the document when it is activated again.
    /// </summary>
    private async Task<T> ShowAsync<T>(Window dialog, Window owner)
    {
        var before = owner.FocusManager?.GetFocusedElement();
        Shown?.Invoke(dialog);
        try { return await dialog.ShowDialog<T>(owner); }
        finally { RestoreFocus(owner, before); }
    }

    private Task ShowAsync(Window dialog, Window owner) => ShowAsync<object?>(dialog, owner);

    private static void RestoreFocus(Window owner, IInputElement? before)
    {
        if (before is not Control { Focusable: true, IsEffectivelyVisible: true } control || control is MenuItem or Menu || !ReferenceEquals(TopLevel.GetTopLevel(control), owner)) return;
        control.Focus(NavigationMethod.Tab);
    }

    private static Button MakeButton(DialogButton b, Window dialog, int index)
    {
        var button = new Button { Content = b.Label, IsDefault = b.IsDefault, IsCancel = b.IsCancel, MinWidth = 84, Margin = new Thickness(8, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => dialog.Close(index);
        return button;
    }

    public Task<int> ChooseAsync(Window owner, ChoiceRequest request) => ChooseAsync(owner, request, CancellationToken.None);

    public async Task<int> ChooseAsync(Window owner, ChoiceRequest request, CancellationToken cancellation)
    {
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = request.Message, TextWrapping = TextWrapping.Wrap });
        if (request.Details is { Count: > 0 } details)
        {
            var list = new ListBox { ItemsSource = details, MaxHeight = 220, Background = Brushes.Transparent };
            AutomationProperties.SetName(list, request.Title);
            panel.Children.Add(list);
        }
        var row = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; // many or long buttons go to a second line
        panel.Children.Add(row);
        var dialog = Frame(request.Title, panel, request.Details is null ? 460 : 560, request.HelpTopic);
        for (int i = 0; i < request.Buttons.Count; i++) row.Children.Add(MakeButton(request.Buttons[i], dialog, i));
        using var closer = cancellation.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close(null)));
        var result = await ShowAsync<int?>(dialog, owner);
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
        var dialog = Frame(title, panel, 420, "format.encoding");
        ok.Click += (_, _) => dialog.Close(combo.SelectedIndex >= 0 ? targets[combo.SelectedIndex].Codec : null);
        cancel.Click += (_, _) => dialog.Close(null);
        return await ShowAsync<TextCodec?>(dialog, owner);
    }

    public async Task<string?> PromptAsync(Window owner, string title, string label, string initial, Func<string, string>? preview = null)
    {
        var input = new TextBox { Text = initial, MinWidth = 260 };
        AutomationProperties.SetName(input, label);
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };
        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
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
        return await ShowAsync<string?>(dialog, owner);
    }

    public async Task<string?> PromptSecretAsync(Window owner, string title, string label)
    {
        var input = new TextBox { PasswordChar = '\u2022', MinWidth = 260 };
        AutomationProperties.SetName(input, label);
        var ok = new Button { Content = Loc.Get(K.DialogOk), IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = Loc.Get(K.DialogCancel), IsCancel = true, MinWidth = 84 };
        var panel = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 10,
            Children =
            {
                new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap },
                input,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { ok, cancel } },
            },
        };
        var dialog = Frame(title, panel, 360);
        ok.Click += (_, _) => dialog.Close(input.Text ?? "");
        cancel.Click += (_, _) => dialog.Close(null);
        dialog.Opened += (_, _) => input.Focus();
        return await ShowAsync<string?>(dialog, owner);
    }

    public async Task<SaveAsChoice?> SaveAsAsync(Window owner, SaveAsRequest request)
    {
        var path = new TextBox { Text = request.SuggestedPath ?? "", MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(path, Loc.Get(K.DialogSaveAsPath));
        var browse = new Button { Content = Loc.Get(K.DialogBrowse) };
        var items = request.Targets.Select(t => t.Lost > 0 ? $"{t.Codec.Name} · {Loc.Plural(K.EncodingLossy, t.Lost)}" : t.Codec.Name).ToList();
        var encoding = new ComboBox { ItemsSource = items, SelectedIndex = Math.Max(0, request.Targets.ToList().FindIndex(t => t.Codec == request.Current)), HorizontalAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetName(encoding, Loc.Get(K.DialogSaveAsEncoding));
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
        var dialog = Frame(Loc.Get(K.DialogSaveAsTitle), panel, 520, "file.saveAs");
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
        return await ShowAsync<SaveAsChoice?>(dialog, owner);
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
        var dialog = Frame(Loc.Get(K.DialogAboutTitle, ("name", Loc.Get(K.AppName))), panel, 340, "help.about");
        close.Click += (_, _) => dialog.Close();
        await ShowAsync(dialog, owner);
    }
}
