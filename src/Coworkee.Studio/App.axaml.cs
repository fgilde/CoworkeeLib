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

    public static Task<StudioSettings> Settings { get; set; } = Task.FromResult(new StudioSettings());

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// The window opens with the splash right away; the view model and the main view (seconds: probing my apps, building the view) follow
    /// below it, then the splash reveals them.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = Settings.GetAwaiter().GetResult();
            UseArguments(settings, Arguments);
            var window = new MainWindow(new SplashView { ReduceMotion = settings.ReduceMotion });
            desktop.MainWindow = window;
            MainViewModel? started = null;
            desktop.Exit += (_, _) =>
            {
                started?.Generate.App.Stop();
                started?.Apps.StopAll();
            };
            window.Opened += async (_, _) => started = await window.StartAsync(() =>
            {
                var main = CreateViewModel(settings, Arguments);
                main.Dialogs.Open = Processes.Open;
                main.Dialogs.Restart = start =>
                {
                    System.Diagnostics.Process.Start(start)?.Dispose();
                    desktop.Shutdown();
                };
                _ = main.Update.CheckAsync(cancellationToken => ToolUpdates.FetchVersionsAsync(ToolUpdates.Studio, cancellationToken));
                return main;
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>The language and theme of the arguments over those of the settings; set before the splash, so it shows in them.</summary>
    public static void UseArguments(StudioSettings settings, StudioArguments arguments)
    {
        L.Use(arguments.Language ?? settings.Language);
        if (arguments.Theme is { } theme)
        {
            settings.Theme = theme.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "Light" : null;
        }

        MainViewModel.ApplyTheme(settings.Theme);
    }

    public static MainViewModel CreateViewModel(StudioSettings settings, StudioArguments arguments, string? settingsFile = null)
    {
        UseArguments(settings, arguments);
        var main = new MainViewModel(settings, new Generator(Processes.RunAsync), null, settingsFile);
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
