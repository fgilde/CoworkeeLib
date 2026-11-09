using System.Diagnostics;
using Avalonia;
using Coworkee.Studio.Model;

namespace Coworkee.Studio;

internal static class Program
{
    /// <summary>Runs from the start of Main; the start-up times in the trace are measured with it.</summary>
    public static Stopwatch Clock { get; } = new();

    [STAThread]
    public static int Main(string[] args)
    {
        Clock.Start();
        App.Arguments = StudioArguments.Parse(args);

        // read while Avalonia initializes: the splash needs the theme and language of the settings
        App.Settings = Task.Run(() => StudioSettings.Load());
        return BuildApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
