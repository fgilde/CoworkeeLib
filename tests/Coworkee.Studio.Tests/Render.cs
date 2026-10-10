using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Coworkee.Studio.ViewModels;
using Coworkee.Studio.Views;

namespace Coworkee.Studio.Tests;

internal static class Render
{
    /// <summary>The main window on <paramref name="step"/> (shown without a page transition) as a frame of 1400x900.</summary>
    public static WriteableBitmap Step(MainViewModel main, StepViewModel step, bool dark, Action<MainWindow>? arrange = null)
    {
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        main.GoTo(step);
        var window = new MainWindow { DataContext = main, Width = 1400, Height = 900 };
        window.Show();
        arrange?.Invoke(window);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        window.Close();
        return frame;
    }
}
