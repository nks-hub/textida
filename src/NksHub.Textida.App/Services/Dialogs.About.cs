using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NksHub.Textida.App.Profile;
using NksHub.Textida.App.Shell;
using NksHub.Textida.App.Text;

namespace NksHub.Textida.App.Services;

/// <summary>
/// The About window: version and build, what the program runs on, the plugins, the licence with the notices of the libraries, the project links, and the block
/// "Copy information" puts on the clipboard for a bug report. Everything is read from the program and the files next to it; a link opens only when it is pressed
/// and the question every other link asks has been answered.
/// </summary>
internal sealed partial class AvaloniaDialogs
{
    /// <summary>The folder with <c>version.txt</c>, <c>LICENSE</c> and <c>THIRD-PARTY-NOTICES.md</c>. Tests point it at a folder of their own.</summary>
    internal string ProgramDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>The icon of the program, the same picture the windows and the executable carry (<c>Assets/textida-256.png</c>, an Avalonia resource).</summary>
    internal const string IconUri = "avares://Textida/Assets/textida-256.png";

    public async Task ShowAboutAsync(Window owner)
    {
        var app = (App)Application.Current!;
        var dialog = BuildAbout(app, AboutInfo.Collect(app, ProgramDirectory), AboutInfo.NoticesIn(ProgramDirectory), owner);
        await ShowAsync(dialog, owner);
    }

    private static string OrUnknown(string value) => value.Length > 0 ? value : Loc.Get(K.DialogAboutUnknown);

    /// <summary>A page of the tab control: <paramref name="content"/> in a scroll viewer, so a long list or a long translation scrolls inside the page and not the window.</summary>
    private static TabItem Page(string header, Control content)
    {
        content.Margin = new Thickness(4, 12, 4, 8);
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = content,
        };
        return new TabItem { Header = header, Content = scroll };
    }

    /// <summary>A label with its value; the value is selectable text named "label: value", so a screen reader reads the pair once. <paramref name="action"/> sits at the end of the row.</summary>
    private static Grid FactRow(string label, string value, Button? action = null)
    {
        var name = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        AutomationProperties.SetAccessibilityView(name, AccessibilityView.Raw);
        var shown = new SelectableTextBlock { Text = value, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(shown, label + ": " + value);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("170,*,Auto"), ColumnSpacing = 10, Margin = new Thickness(0, 2), Children = { name, shown } };
        Grid.SetColumn(shown, 1);
        if (action is null) return row;
        Grid.SetColumn(action, 2);
        row.Children.Add(action);
        return row;
    }

    private static string ProfileKindText(ProfileKind kind) => Loc.Get(kind switch
    {
        ProfileKind.Portable => K.DialogAboutProfilePortable,
        ProfileKind.User => K.DialogAboutProfileUser,
        _ => K.DialogAboutProfileCustom,
    });

    private static string PluginStateText(AboutPlugin plugin) => plugin.Origin switch
    {
        PluginOrigin.Bundled => Loc.Get(K.PluginsOriginBuiltin),
        PluginOrigin.Installed => Loc.Get(K.PluginsOriginInstalled),
        PluginOrigin.Disabled => Loc.Get(K.PluginsOriginInstalled) + ", " + Loc.Get(K.PluginsOriginDisabled).ToLowerInvariant(),
        _ => Loc.Get(K.PluginsRowProblem, ("reason", plugin.Problem ?? "")),
    };

    private static Window BuildAbout(App app, AboutInfo info, string? notices, Window owner)
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        Window? window = null;

        // folders and links: each press is handled below, once the window exists
        Button FolderButton(string folder, string nameKey)
        {
            var button = new Button { Content = Loc.Get(K.DialogAboutOpenFolder), VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetName(button, Loc.Get(nameKey));
            button.Click += async (_, _) => await OpenFolderAsync(folder);
            return button;
        }
        Control LinkRow(string labelKey, string url)
        {
            string label = Loc.Get(labelKey);
            var link = new Button { Content = url, HorizontalAlignment = HorizontalAlignment.Left };
            link.Classes.Add("previewlink");
            AutomationProperties.SetName(link, label + ": " + url);
            link.Click += async (_, _) => await OpenLinkAsync(url);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), ColumnSpacing = 10, Margin = new Thickness(0, 2), Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 }, link } };
            Grid.SetColumn(link, 1);
            return row;
        }

        async Task OpenFolderAsync(string folder)
        {
            try { Directory.CreateDirectory(folder); } // the log folder appears with the first line written
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the system then says it cannot open it */ }
            bool opened = app.PathOpener is { } open ? await open(folder) : SystemLauncher.Start(SystemLauncher.StartInfo(folder, SystemLauncher.Current));
            status.Text = opened ? "" : Loc.Get(K.SpecialLaunchFailed, ("path", folder));
        }

        async Task OpenLinkAsync(string url)
        {
            if (PreviewPolicy.LinkTarget(url) is not { } uri) return;
            var question = new ChoiceRequest(Loc.Get(K.PreviewLinkTitle), Loc.Get(K.PreviewLinkMessage, ("url", uri.AbsoluteUri)),
                [new DialogButton(Loc.Get(K.DialogOk), IsDefault: true), new DialogButton(Loc.Get(K.DialogCancel), IsCancel: true)]);
            if (await app.Dialogs.ChooseAsync(window!, question) != 0) return;
            bool opened = app.UriOpener is { } open ? await open(uri) : await window!.Launcher.LaunchUriAsync(uri);
            status.Text = opened ? "" : Loc.Get(K.PreviewLinkFailed, ("url", uri.AbsoluteUri));
        }

        // header
        var mark = new Image { Width = 56, Height = 56, VerticalAlignment = VerticalAlignment.Top, Source = new Bitmap(AssetLoader.Open(new Uri(IconUri))) };
        RenderOptions.SetBitmapInterpolationMode(mark, BitmapInterpolationMode.HighQuality);
        AutomationProperties.SetAccessibilityView(mark, AccessibilityView.Raw); // the product name next to it says the same
        var titles = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new SelectableTextBlock { Text = Loc.Get(K.AppName), FontSize = 22, FontWeight = FontWeight.SemiBold },
                new SelectableTextBlock { Text = Loc.Get(K.DialogAboutVersion, ("version", info.Version)), TextWrapping = TextWrapping.Wrap },
                new SelectableTextBlock { Text = Loc.Get(K.DialogAboutBuild, ("commit", OrUnknown(info.Commit)), ("date", OrUnknown(info.BuildDate))), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 12 },
                LinkRow(K.DialogAboutLinkProject, AboutInfo.ProjectUrl),
                LinkRow(K.DialogAboutLinkIssues, AboutInfo.IssuesUrl),
            },
        };
        Grid.SetColumn(titles, 1);
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 14, Margin = new Thickness(20, 18, 20, 4), Children = { mark, titles } };

        // the facts about the system, the plugins, and the licence with the notices of the libraries
        var facts = new StackPanel
        {
            Children =
            {
                FactRow(Loc.Get(K.DialogAboutFactRuntime), info.Runtime),
                FactRow(Loc.Get(K.DialogAboutFactCompiled), info.Compilation),
                FactRow(Loc.Get(K.DialogAboutFactOs), info.Os),
                FactRow(Loc.Get(K.DialogAboutFactArchitecture), info.Architecture),
                FactRow(Loc.Get(K.DialogAboutFactLanguage), LanguageText(info.Language)),
                FactRow(Loc.Get(K.DialogAboutFactProfile), ProfileKindText(info.Profile)),
                FactRow(Loc.Get(K.DialogAboutFactProfileFolder), info.ProfileFolder, FolderButton(info.ProfileFolder, K.DialogAboutOpenProfile)),
                FactRow(Loc.Get(K.DialogAboutFactLogFolder), info.LogFolder, FolderButton(info.LogFolder, K.DialogAboutOpenLog)),
            },
        };
        var plugins = new StackPanel();
        foreach (var plugin in info.Plugins)
            plugins.Children.Add(FactRow(plugin.Name, Loc.Get(K.DialogAboutPluginRow, ("version", plugin.Version), ("state", PluginStateText(plugin)))));

        var noticesBox = new TextBox
        {
            Text = notices ?? Loc.Get(K.DialogAboutNoticesMissing, ("file", AboutInfo.NoticesFile)), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            FontFamily = Mono, FontSize = 12, MinHeight = 120,
        };
        noticesBox.Classes.Add("outputtext");
        AutomationProperties.SetName(noticesBox, Loc.Get(K.DialogAboutNotices));
        var noticesFrame = new Border { Child = noticesBox, Margin = new Thickness(0, 4, 0, 0) };
        noticesFrame.Classes.Add("previewcode");
        var licence = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Children =
            {
                new SelectableTextBlock { Text = string.Join('\n', new[] { Loc.Get(K.DialogAboutMit), info.Copyright }.Where(l => l.Length > 0)), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = Loc.Get(K.DialogAboutNotices), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Opacity = 0.75 },
                noticesFrame,
            },
        };
        Grid.SetRow(licence.Children[1], 1);
        Grid.SetRow(noticesFrame, 2);

        var tabs = new TabControl { Margin = new Thickness(16, 6, 16, 0) };
        tabs.Items.Add(Page(Loc.Get(K.DialogAboutSectionSystem), facts));
        tabs.Items.Add(Page(Loc.Get(K.A11yPluginsManager), plugins));
        tabs.Items.Add(new TabItem { Header = Loc.Get(K.DialogAboutSectionLicense), Content = licence });
        licence.Margin = new Thickness(4, 12, 4, 8);

        // footer: the report for a bug, then Close
        var includePaths = new CheckBox { Content = new TextBlock { Text = Loc.Get(K.DialogAboutIncludePaths), TextWrapping = TextWrapping.Wrap }, IsChecked = false };
        AutomationProperties.SetName(includePaths, Loc.Get(K.DialogAboutIncludePaths));
        var copy = new Button { Content = Loc.Get(K.DialogAboutCopy), MinWidth = 84 };
        var close = new Button { Content = Loc.Get(K.DialogAboutClose), IsDefault = true, IsCancel = true, MinWidth = 84 };
        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 10, RowSpacing = 8,
            Margin = new Thickness(20, 10, 20, 14), Children = { includePaths, copy, status, close },
        };
        Grid.SetColumnSpan(includePaths, 3);
        Grid.SetRow(copy, 1);
        Grid.SetRow(status, 1);
        Grid.SetColumn(status, 1);
        Grid.SetRow(close, 1);
        Grid.SetColumn(close, 2);
        var footerFrame = new Border { Child = footer };
        footerFrame.Classes.Add("outputpanel");

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Children = { header, tabs, footerFrame } };
        Grid.SetRow(tabs, 1);
        Grid.SetRow(footerFrame, 2);

        var dialog = Frame(Loc.Get(K.DialogAboutTitle, ("name", Loc.Get(K.AppName))), root, 640, "help.about");
        window = dialog;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.CanResize = true;
        dialog.MinWidth = 480;
        dialog.MinHeight = 440;
        dialog.Height = Math.Clamp(owner.Bounds.Height - 40, 560, 720);
        copy.Click += async (_, _) =>
        {
            if (dialog.Clipboard is not { } clipboard) return;
            await clipboard.SetTextAsync(info.ToReport(includePaths.IsChecked == true));
            status.Text = Loc.Get(K.DialogAboutCopied);
        };
        close.Click += (_, _) => dialog.Close();
        dialog.Opened += (_, _) => close.Focus();
        return dialog;
    }

    /// <summary>The language of the interface by its own name and its tag, like "čeština (cs)".</summary>
    private static string LanguageText(string tag) => $"{System.Globalization.CultureInfo.GetCultureInfo(tag).NativeName} ({tag})";
}
