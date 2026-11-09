using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Coworkee.Studio.Views;

namespace Coworkee.Studio.Tests;

public sealed class SplashTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("STUDIO_SCREENSHOTS");

    [Fact]
    public Task The_splash_shows_first_and_the_studio_replaces_it_once_built() => TestApp.Ui(() =>
    {
        using var folder = new TempFolder();
        var window = new MainWindow(new SplashView { Animate = false });
        window.Show();
        var created = false;
        var start = window.StartAsync(() =>
        {
            created = true;
            return Studio.Create(folder);
        });
        Dispatcher.UIThread.RunJobs();

        // nothing heavy before the splash was rendered
        created.ShouldBeFalse();
        window.GetVisualDescendants().OfType<SplashView>().ShouldHaveSingleItem();
        window.GetVisualDescendants().OfType<MainView>().ShouldBeEmpty();

        Pump(start);
        created.ShouldBeTrue();
        window.DataContext.ShouldBeSameAs(start.Result);
        window.GetVisualDescendants().OfType<SplashView>().ShouldBeEmpty();
        window.GetVisualDescendants().OfType<MainView>().ShouldHaveSingleItem();
        window.Close();
    });

    [Fact]
    public Task A_failed_start_shows_the_reason_on_the_splash() => TestApp.Ui(() =>
    {
        Localization.L.Use("en");
        var splash = new SplashView { Animate = false };
        var window = new MainWindow(splash);
        window.Show();

        var start = window.StartAsync(() => throw new IOException("disk gone"));
        Pump(start);

        start.Result.ShouldBeNull();
        splash.Status.ShouldBe("The Studio could not start: disk gone");
        window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CloseButton").IsVisible.ShouldBeTrue();
        window.GetVisualDescendants().OfType<MainView>().ShouldBeEmpty();
        window.Close();
    });

    [Theory]
    [InlineData(false, "en")]
    [InlineData(true, "en")]
    [InlineData(false, "de")]
    public Task Renders_the_splash(bool dark, string language) => TestApp.Ui(() =>
    {
        Localization.L.Use(language);
        Avalonia.Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        var splash = new SplashView { Animate = false, Status = Localization.L.Get("Looking for your apps…") };
        var window = new MainWindow(splash) { Width = 1400, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(5);
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame rendered.");
        window.Close();

        frame.PixelSize.Width.ShouldBe(1400);
        if (Folder is not null)
        {
            Directory.CreateDirectory(Folder);
            frame.Save(Path.Combine(Folder, $"studio-splash-{(dark ? "dark" : "light")}{(language == "en" ? string.Empty : "-" + language)}.png"), new PngBitmapEncoderOptions());
        }
    });

    /// <summary>Renders frames until the start-up is through; each step of it waits for a frame.</summary>
    private static void Pump(Task start)
    {
        for (var i = 0; i < 100 && !start.IsCompleted; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        start.IsCompleted.ShouldBeTrue();
    }
}
