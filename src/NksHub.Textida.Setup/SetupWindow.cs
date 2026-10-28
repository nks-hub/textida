using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NksHub.Textida.Install;

namespace NksHub.Textida.Setup;

internal enum SetupPage { Welcome, Options, Progress, Done, Error, Uninstall, Removed }

/// <summary>
/// The window of the setup. Install: welcome with the licence, options, progress, done. Uninstall: the question, progress, done.
/// The work is done by <see cref="SetupModel"/>; this class only shows it and walks the pages. Controls carry an automation id for the tests.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SetupWindow : Window
{
    private readonly SetupModel _model;
    private readonly bool _uninstalling;
    private readonly Border _body = new() { Padding = new Thickness(26, 18) };
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _heading = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold, FontSize = 15 };
    private readonly ComboBox _language = new() { MinWidth = 130, VerticalAlignment = VerticalAlignment.Center };
    private bool _accepted;
    private string _errorText = "";
    private InstallResult? _result;
    private string _dataLocation = "";
    private bool _launchAfter;
    private ProgressBar? _bar;
    private TextBlock? _stage;

    public SetupWindow(SetupModel model, bool uninstall)
    {
        _model = model;
        _uninstalling = uninstall;
        _launchAfter = model.Launch;
        Classes.Add("shell");
        Width = 680;
        Height = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var mark = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), Child = new TextBlock { Text = "T", FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        mark.Classes.Add("mark");
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(18, 12), ColumnSpacing = 10 };
        Grid.SetColumn(_heading, 1);
        Grid.SetColumn(_language, 2);
        header.Children.Add(mark);
        header.Children.Add(_heading);
        header.Children.Add(_language);

        _body.Classes.Add("page");
        var footer = new Border { Child = _buttons, Padding = new Thickness(18, 12) };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(header);
        root.Children.Add(footer);
        root.Children.Add(_body);
        Content = root;

        AutomationProperties.SetAutomationId(_language, "language");
        foreach (var tag in SetupText.Languages) _language.Items.Add(SetupText.Names[tag]);
        _language.SelectionChanged += (_, _) =>
        {
            if (_language.SelectedIndex < 0 || SetupText.Languages[_language.SelectedIndex] == _model.Text.Language) return;
            _model.SetLanguage(SetupText.Languages[_language.SelectedIndex]);
            Render();
        };
        Closing += (_, e) => e.Cancel = Page == SetupPage.Progress;

        Page = uninstall ? SetupPage.Uninstall : SetupPage.Welcome;
        Render();
    }

    public SetupPage Page { get; private set; }

    public SetupModel Model => _model;

    /// <summary>The install or the removal that is running or ran last; the page changes when it ends.</summary>
    internal Task Work { get; private set; } = Task.CompletedTask;

    /// <summary>The text of the failure that the error page shows.</summary>
    internal string ErrorText => _errorText;

    /// <summary>Test seam: answers the Browse button without the system dialog.</summary>
    internal Func<Task<string?>>? FolderPicker { get; set; }

    internal bool Accepted
    {
        get => _accepted;
        set
        {
            _accepted = value;
            if (Page == SetupPage.Welcome) Render();
        }
    }

    internal T? Find<T>(string id) where T : Control =>
        this.GetVisualDescendants().OfType<T>().FirstOrDefault(c => AutomationProperties.GetAutomationId(c) == id);

    internal void SelectLanguage(string tag) => _language.SelectedIndex = SetupText.Languages.ToList().IndexOf(tag);

    // ---- navigation ----

    /// <summary>The primary button of the page: Next, Install, Update, Uninstall, Finish or Close.</summary>
    internal void Primary()
    {
        switch (Page)
        {
            case SetupPage.Welcome when _accepted: Page = SetupPage.Options; Render(); break;
            case SetupPage.Options when _model.DirProblem() is null: StartInstall(); break;
            case SetupPage.Uninstall: StartUninstall(); break;
            case SetupPage.Done:
                if (_launchAfter && _result is not null) _model.Env.Launch(_result.ExePath, _result.Dir);
                Close();
                break;
            case SetupPage.Error: Retry(); break;
            case SetupPage.Removed: Close(); break;
        }
    }

    internal void Back()
    {
        if (Page == SetupPage.Options) Page = SetupPage.Welcome;
        else if (Page == SetupPage.Error && !_uninstalling) Page = SetupPage.Options;
        else return;
        Render();
    }

    private void Retry()
    {
        if (_uninstalling) StartUninstall();
        else StartInstall();
    }

    private void StartInstall()
    {
        Page = SetupPage.Progress;
        Render();
        Work = RunAsync(async () =>
        {
            _result = await Task.Run(() => _model.Install(ReportProgress));
            Page = SetupPage.Done;
        });
    }

    private void StartUninstall()
    {
        Page = SetupPage.Progress;
        Render();
        Work = RunAsync(async () =>
        {
            _dataLocation = await Task.Run(() => _model.Uninstall(ReportProgress));
            Page = SetupPage.Removed;
        });
    }

    private async Task RunAsync(Func<Task> work)
    {
        try { await work(); }
        catch (SetupException e) { _errorText = _model.Text.Describe(e); Page = SetupPage.Error; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException) { _errorText = e.Message; Page = SetupPage.Error; }
        Render();
    }

    private void ReportProgress(SetupStage stage, double fraction) => Dispatcher.UIThread.Post(() =>
    {
        if (_bar is null || _stage is null) return;
        _bar.Value = fraction * 100;
        _stage.Text = _uninstalling ? _model.Text.Get(S.UninstallProgress) : _model.Text.Stage(stage);
    });

    // ---- pages ----

    private void Render()
    {
        var text = _model.Text;
        Title = text.Get(S.AppTitle);
        _heading.Text = text.Get(S.AppTitle);
        AutomationProperties.SetName(_language, text.Get(S.Language));
        _language.SelectedIndex = SetupText.Languages.ToList().IndexOf(text.Language);

        _bar = null;
        _stage = null;
        _buttons.Children.Clear();
        _body.Child = Page switch
        {
            SetupPage.Welcome => Welcome(),
            SetupPage.Options => Options(),
            SetupPage.Progress => Progress(),
            SetupPage.Done => Done(),
            SetupPage.Error => Failure(),
            SetupPage.Uninstall => Question(),
            _ => Removed(),
        };
    }

    private void AddButtons(string? primaryText, bool primaryEnabled, bool back = false, bool cancel = true)
    {
        var text = _model.Text;
        if (back) _buttons.Children.Add(MakeButton("back", text.Get(S.Back), true, Back));
        if (primaryText is not null)
        {
            var primary = MakeButton("primary", primaryText, primaryEnabled, Primary);
            primary.Classes.Add("accent");
            primary.IsDefault = true;
            _buttons.Children.Add(primary);
        }
        if (cancel) _buttons.Children.Add(MakeButton("cancel", text.Get(S.Cancel), Page != SetupPage.Progress, Close));
    }

    private static Button MakeButton(string id, string content, bool enabled, Action click)
    {
        var button = new Button { Content = content, IsEnabled = enabled, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = id == "cancel" };
        AutomationProperties.SetAutomationId(button, id);
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Title1(string text) => new() { Text = text, Classes = { "title" }, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Paragraph(string text, string id) => WithId(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, id);

    private static T WithId<T>(T control, string id) where T : Control
    {
        AutomationProperties.SetAutomationId(control, id);
        return control;
    }

    private Control Welcome()
    {
        var text = _model.Text;
        var intro = _model.Installed is { } installed
            ? text.Get(S.WelcomeInstalled, ("installed", installed.Version), ("dir", installed.Dir), ("version", _model.PackageVersion))
            : text.Get(S.WelcomeIntro, ("version", _model.PackageVersion));
        if (!_model.HasPackage)
        {
            _errorText = text.Get(S.ErrorNoPackage);
            AddButtons(null, false, cancel: false);
            _buttons.Children.Add(MakeButton("cancel", text.Get(S.Close), true, Close));
            return new StackPanel { Spacing = 10, Children = { Title1(text.Get(S.ErrorTitle)), Paragraph(_errorText, "message") } };
        }

        var licence = WithId(new TextBox
        {
            Text = LicenceText.Value.Value, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 210,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace"), FontSize = 12,
        }, "licence");
        AutomationProperties.SetName(licence, text.Get(S.LicenceTitle));
        var accept = WithId(new CheckBox { Content = text.Get(S.LicenceAccept), IsChecked = _accepted }, "accept");
        accept.IsCheckedChanged += (_, _) =>
        {
            _accepted = accept.IsChecked == true;
            if (Find<Button>("primary") is { } primary) primary.IsEnabled = _accepted;
        };
        AddButtons(text.Get(S.Next), _accepted);
        return new StackPanel
        {
            Spacing = 10,
            Children = { Title1(text.Get(S.WelcomeTitle)), Paragraph(intro, "intro"), new TextBlock { Text = text.Get(S.LicenceTitle), FontWeight = FontWeight.SemiBold }, licence, accept },
        };
    }

    private Control Options()
    {
        var text = _model.Text;
        var problem = WithId(new TextBlock { Classes = { "problem" }, TextWrapping = TextWrapping.Wrap, MinHeight = 18 }, "problem");

        var dir = WithId(new TextBox { Text = _model.Dir }, "dir");
        AutomationProperties.SetName(dir, text.Get(S.OptionsDir));
        var browse = WithId(new Button { Content = text.Get(S.Browse) }, "browse");
        browse.Click += async (_, _) =>
        {
            var picked = await PickFolderAsync();
            if (picked is null) return;
            if (Directory.Exists(picked) && !File.Exists(Path.Combine(picked, Installer.ProgramFile)) && Directory.EnumerateFileSystemEntries(picked).Any()) picked = Path.Combine(picked, "Textida");
            dir.Text = picked;
        };
        void CheckDir()
        {
            _model.Dir = dir.Text ?? "";
            var message = _model.DirProblem();
            problem.Text = message ?? "";
            if (Find<Button>("primary") is { } primary) primary.IsEnabled = message is null;
        }
        dir.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) CheckDir(); };
        var dirRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(browse, 1);
        dirRow.Children.Add(dir);
        dirRow.Children.Add(browse);

        var extensions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var extension in ShellIntegration.Extensions)
        {
            var box = WithId(new CheckBox { Content = extension, IsChecked = _model.Extensions.Contains(extension), MinWidth = 70, Margin = new Thickness(0, 0, 6, 0) }, "ext" + extension);
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true) _model.Extensions.Add(extension);
                else _model.Extensions.Remove(extension);
            };
            extensions.Children.Add(box);
        }
        void SetAll(bool on)
        {
            foreach (var box in extensions.Children.OfType<CheckBox>()) box.IsChecked = on;
        }
        var all = WithId(new Button { Content = text.Get(S.OptionsAssocAll), Padding = new Thickness(8, 1), MinHeight = 24 }, "assocAll");
        all.Click += (_, _) => SetAll(true);
        var none = WithId(new Button { Content = text.Get(S.OptionsAssocNone), Padding = new Thickness(8, 1), MinHeight = 24 }, "assocNone");
        none.Click += (_, _) => SetAll(false);
        var assocHeader = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0),
            Children = { new TextBlock { Text = text.Get(S.OptionsAssoc), VerticalAlignment = VerticalAlignment.Center }, all, none },
        };

        CheckBox Option(string id, string label, bool state, Action<bool> set)
        {
            var box = WithId(new CheckBox { Content = label, IsChecked = state }, id);
            box.IsCheckedChanged += (_, _) => set(box.IsChecked == true);
            return box;
        }

        var installing = _model.Installed is null;
        AddButtons(text.Get(installing ? S.Install : S.Update), _model.DirProblem() is null, back: true);
        problem.Text = _model.DirProblem() ?? "";
        var page = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 14, 0),
            Children =
            {
                Title1(text.Get(S.OptionsTitle)),
                new TextBlock { Text = text.Get(S.OptionsDir), FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) },
                dirRow,
                problem,
                Option("startMenu", text.Get(S.OptionsStartMenu), _model.StartMenu, on => _model.StartMenu = on),
                Option("desktop", text.Get(S.OptionsDesktop), _model.Desktop, on => _model.Desktop = on),
                Option("menu", text.Get(S.OptionsMenu, ("label", text.Get(S.MenuLabel))), _model.ContextMenu, on => _model.ContextMenu = on),
                assocHeader,
                extensions,
                Option("launch", text.Get(S.OptionsLaunch), _model.Launch, on => { _model.Launch = on; _launchAfter = on; }),
            },
        };
        return new ScrollViewer { Content = page }; // the list of file types is long; a small screen or a large font scrolls instead of clipping
    }

    private Task<string?> PickFolderAsync()
    {
        if (FolderPicker is { } seam) return seam();
        return PickWithDialogAsync();

        async Task<string?> PickWithDialogAsync()
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = _model.Text.Get(S.OptionsDir), AllowMultiple = false });
            return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
        }
    }

    private Control Progress()
    {
        var text = _model.Text;
        _bar = WithId(new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 10 }, "bar");
        _stage = WithId(new TextBlock { Text = _uninstalling ? text.Get(S.UninstallProgress) : text.Stage(SetupStage.Preparing), Classes = { "hint" } }, "stage");
        AutomationProperties.SetName(_bar, _stage.Text);
        AddButtons(null, false);
        return new StackPanel
        {
            Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
            Children = { Title1(_uninstalling ? text.Get(S.UninstallTitle) : text.Get(_model.Installed is null ? S.ProgressInstall : S.ProgressUpdate)), _bar, _stage },
        };
    }

    private Control Done()
    {
        var text = _model.Text;
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(Title1(text.Get(_result!.Updated ? S.DoneUpdate : S.DoneInstall)));
        panel.Children.Add(Paragraph(_model.DoneMessage(_result), "message"));
        foreach (var warning in _result.Warnings) panel.Children.Add(WithId(new TextBlock { Text = text.Get(S.DoneWarning, ("reason", warning)), Classes = { "problem" }, TextWrapping = TextWrapping.Wrap }, "warning"));
        var launch = WithId(new CheckBox { Content = text.Get(S.DoneLaunch), IsChecked = _launchAfter }, "launchDone");
        launch.IsCheckedChanged += (_, _) => _launchAfter = launch.IsChecked == true;
        panel.Children.Add(launch);
        AddButtons(text.Get(S.Finish), true, cancel: false);
        return panel;
    }

    private Control Failure()
    {
        var text = _model.Text;
        AddButtons(text.Get(S.Retry), true, back: !_uninstalling, cancel: false);
        _buttons.Children.Add(MakeButton("cancel", text.Get(S.Close), true, Close));
        return new StackPanel { Spacing = 10, Children = { Title1(text.Get(S.ErrorTitle)), WithId(new TextBlock { Text = _errorText, TextWrapping = TextWrapping.Wrap, Classes = { "problem" } }, "message") } };
    }

    private Control Question()
    {
        var text = _model.Text;
        var data = _model.NewInstaller().UserDataLocation(_model.Dir);
        var delete = WithId(new CheckBox { Content = text.Get(S.UninstallDeleteSettings), IsChecked = _model.DeleteSettings }, "deleteSettings");
        delete.IsCheckedChanged += (_, _) => _model.DeleteSettings = delete.IsChecked == true;
        AddButtons(text.Get(S.Uninstall), true);
        return new StackPanel
        {
            Spacing = 10,
            Children = { Title1(text.Get(S.UninstallTitle)), Paragraph(text.Get(S.UninstallQuestion, ("dir", _model.Dir)), "message"), Paragraph(text.Get(S.UninstallKeep, ("data", data)), "keep"), delete },
        };
    }

    private Control Removed()
    {
        var text = _model.Text;
        AddButtons(text.Get(S.Close), true, cancel: false);
        return new StackPanel
        {
            Spacing = 10,
            Children = { Title1(text.Get(S.UninstallTitle)), Paragraph(text.Get(_model.DeleteSettings ? S.UninstallDoneAll : S.UninstallDone, ("data", _dataLocation)), "message") },
        };
    }
}

/// <summary>The licence shown on the first page: the LICENSE file of the repository, embedded.</summary>
internal static class LicenceText
{
    public static readonly Lazy<string> Value = new(() =>
    {
        using var stream = typeof(LicenceText).Assembly.GetManifestResourceStream("LICENSE");
        if (stream is null) return "MIT License";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    });
}
