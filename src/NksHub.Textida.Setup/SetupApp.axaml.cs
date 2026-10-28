using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace NksHub.Textida.Setup;

public sealed partial class SetupApp : Application
{
    /// <summary>What the command line asked for; the window is made from it once the framework runs.</summary>
    internal static (SetupCommand Command, SetupEnvironment Env)? Startup { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Startup is var (command, env))
        {
            var model = new SetupModel(env, new SetupText(command.Language ?? env.UiLanguage));
            model.Apply(command);
            desktop.MainWindow = new SetupWindow(model, command.Mode == SetupMode.Uninstall);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
