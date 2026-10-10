using Avalonia;

namespace Coworkee.Studio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        App.Arguments = StudioArguments.Parse(args);
        return BuildApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
