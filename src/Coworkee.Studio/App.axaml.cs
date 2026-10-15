using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Coworkee.Cli.Spec;
using Coworkee.Studio.Cli;
using Coworkee.Studio.Localization;
using Coworkee.Studio.Model;
using Coworkee.Studio.ViewModels;
using Coworkee.Studio.Views;

namespace Coworkee.Studio;

public sealed class App : Application
{
    public static StudioArguments Arguments { get; set; } = new(null, null, null, null);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = CreateViewModel(StudioSettings.Load(), Arguments);
            var window = new MainWindow { DataContext = main };
            main.Dialogs.Open = Processes.Open;
            main.Dialogs.Restart = start =>
            {
                System.Diagnostics.Process.Start(start)?.Dispose();
                desktop.Shutdown();
            };
            window.Attach(main);
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                main.Generate.App.Stop();
                main.Apps.StopAll();
            };
            _ = main.Update.CheckAsync(cancellationToken => ToolUpdates.FetchVersionsAsync(ToolUpdates.Studio, cancellationToken));
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static MainViewModel CreateViewModel(StudioSettings settings, StudioArguments arguments, string? settingsFile = null)
    {
        L.Use(arguments.Language ?? settings.Language);
        if (arguments.Theme is { } theme)
        {
            settings.Theme = theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "Light" : null;
        }

        var main = new MainViewModel(settings, new Generator(Processes.RunAsync), null, settingsFile);
        main.ApplyTheme();
        if (arguments.Output is { } output)
        {
            main.Basics.Location = Path.GetFullPath(output);
        }

        if (arguments.Spec is { } spec)
        {
            main.Open(spec);
        }

        return main;
    }
}
