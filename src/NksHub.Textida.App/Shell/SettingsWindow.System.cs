using System.Runtime.Versioning;
using System.Security;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using NksHub.Textida.App.Services;
using NksHub.Textida.App.Text;
using NksHub.Textida.Install;
using NksHub.Textida.Workbench.Matching;

namespace NksHub.Textida.App.Shell;

/// <summary>
/// The System integration section of the settings (Windows only): the Explorer menu entry, the file associations and the two shortcuts.
/// It is found by the search like a setting. Nothing is stored in the settings file: every switch shows what the registry and the disk say, and a change is written at once.
/// </summary>
internal sealed partial class SettingsWindow
{
    /// <summary>Tag of the section among the rows, for tests.</summary>
    internal const string SystemSectionId = "system.integration";

    private static bool MatchesSystemSection(string query)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var words = TextMatching.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        var text = TextMatching.Fold(string.Join(' ', Loc.Get(K.SettingsSystemTitle), Loc.Get(K.SettingsSystemKeywords), string.Join(' ', ShellIntegration.Extensions)));
        return words.All(w => text.Contains(w, StringComparison.Ordinal));
    }

    [SupportedOSPlatform("windows")]
    private Border SystemSection()
    {
        var integration = ShellHost.Open();
        var problem = new TextBlock { Foreground = ThemeColors.Danger, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(new TextBlock { Text = Loc.Get(K.SettingsSystemTitle), FontWeight = FontWeight.SemiBold });
        stack.Children.Add(new TextBlock { Text = Loc.Get(integration is null ? K.ShellUnavailable : K.SettingsSystemDescription), TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });

        if (integration is not null)
        {
            stack.Children.Add(Toggle(Loc.Get(K.SettingsSystemMenu, ("label", Loc.Get(K.ShellMenuLabel))), integration.HasMenu(), integration, problem,
                on => integration.SetMenu(on, Loc.Get(K.ShellMenuLabel))));

            stack.Children.Add(new TextBlock { Text = Loc.Get(K.SettingsSystemAssoc), Margin = new Thickness(0, 6, 0, 0) });
            var extensions = new WrapPanel { Orientation = Orientation.Horizontal };
            var associated = integration.AssociatedExtensions().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var extension in ShellIntegration.Extensions)
            {
                var box = Toggle(extension, associated.Contains(extension), integration, problem, on => integration.SetAssociated(extension, on));
                box.MinWidth = 84;
                extensions.Children.Add(box);
            }
            stack.Children.Add(extensions);

            stack.Children.Add(Toggle(Loc.Get(K.SettingsSystemStartMenu), integration.HasStartMenuShortcut(), integration, problem, integration.SetStartMenuShortcut));
            stack.Children.Add(Toggle(Loc.Get(K.SettingsSystemDesktop), integration.HasDesktopShortcut(), integration, problem, integration.SetDesktopShortcut));
        }

        stack.Children.Add(new TextBlock { Text = Loc.Get(K.SettingsSystemDefaultNote), TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 6, 0, 0) });
        var defaults = new Button { Content = Loc.Get(K.SettingsSystemDefaultButton), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 3) };
        defaults.Click += async (_, _) =>
        {
            if (await OpenDefaultAppsAsync()) problem.IsVisible = false;
            else { problem.Text = Loc.Get(K.ShellFailed, ("reason", ShellHost.DefaultAppsUri)); problem.IsVisible = true; }
        };
        stack.Children.Add(defaults);
        stack.Children.Add(problem);

        var row = new Border { Child = stack, Padding = new Thickness(12, 8), Tag = SystemSectionId, CornerRadius = new CornerRadius(6) };
        row.Classes.Add("settingrow");
        return row;
    }

    /// <summary>A switch that applies <paramref name="apply"/> when it is flipped and flips back, with the reason shown, when Windows refuses.</summary>
    [SupportedOSPlatform("windows")]
    private static CheckBox Toggle(string label, bool state, SystemIntegration integration, TextBlock problem, Action<bool> apply)
    {
        var box = new CheckBox { Content = label, IsChecked = state };
        var reverting = false;
        box.IsCheckedChanged += (_, _) =>
        {
            if (reverting) return;
            var on = box.IsChecked == true;
            try
            {
                apply(on);
                integration.Notify();
                problem.IsVisible = false;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
            {
                problem.Text = Loc.Get(K.ShellFailed, ("reason", e.Message));
                problem.IsVisible = true;
                reverting = true;
                box.IsChecked = !on;
                reverting = false;
            }
        };
        return box;
    }

    private Task<bool> OpenDefaultAppsAsync() =>
        _app.PathOpener is { } open ? open(ShellHost.DefaultAppsUri) : Task.FromResult(SystemLauncher.Start(SystemLauncher.StartInfo(ShellHost.DefaultAppsUri, SystemLauncher.Platform.Windows)));
}
