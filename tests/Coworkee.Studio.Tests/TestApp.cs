using Avalonia;
using Avalonia.Headless;

[assembly: AssemblyFixture(typeof(Coworkee.Studio.Tests.UiSession))]

// the language of the Studio is process wide and the screenshots switch it
[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Coworkee.Studio.Tests;

/// <summary>Starts the headless app before any test, so its thread owns the dispatcher, whatever test touches Avalonia first.</summary>
public sealed class UiSession
{
    public UiSession() => TestApp.Ui(() => { }).GetAwaiter().GetResult();
}

/// <summary>
/// Headless with Skia, so the UI tests render real frames and the screenshots look like the app.
/// The session is used directly: Avalonia.Headless.XUnit is built against an older xunit v3 than the tests.
/// </summary>
public static class TestApp
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(TestApp), AvaloniaTestIsolationLevel.PerAssembly);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
        .WithInterFont();

    /// <summary>Runs <paramref name="test"/> on the UI thread of the headless app.</summary>
    public static Task Ui(Action test) => Session.Dispatch(test, TestContext.Current.CancellationToken);
}
