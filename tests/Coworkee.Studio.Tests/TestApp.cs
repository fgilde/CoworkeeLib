using Avalonia;
using Avalonia.Headless;

namespace Coworkee.Studio.Tests;

/// <summary>
/// Headless with Skia, so the UI tests render real frames and the screenshots look like the app.
/// The session is used directly: Avalonia.Headless.XUnit is built against an older xunit v3 than the tests.
/// </summary>
public static class TestApp
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestApp));

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();

    /// <summary>Runs <paramref name="test"/> on the UI thread of the headless app.</summary>
    public static Task Ui(Action test) => Session.Dispatch(test, TestContext.Current.CancellationToken);
}
