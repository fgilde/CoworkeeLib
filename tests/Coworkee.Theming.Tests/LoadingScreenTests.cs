using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Theming;

namespace Coworkee.Theming.Tests;

public sealed class LoadingScreenTests(ThemeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Anyone_reads_the_default_theme_with_an_animated_background()
    {
        var screen = await app.Anonymous().GetFromJsonAsync<LoadingScreenDto>("/api/v1/themes/loading-screen", Ct);

        screen!.Theme!.Name.ShouldBe("Ocean");
        screen.Background.ShouldBe(LoadingScreenBackground.Animated);
        screen.MediaUrl.ShouldBeNull();
    }

    [Fact]
    public async Task The_sign_in_design_chooses_the_background_and_media()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        try
        {
            var design = new SignInDesign { MediaUrl = "https://web.test/api/v1/files/public/1/hero.mp4", LoadingScreen = LoadingScreenBackground.SignInMedia };
            (await admin.PutAsJsonAsync($"/api/v1/configuration/{SignInDesign.Section}", design, Ct)).EnsureSuccessStatusCode();

            LoadingScreenDto? screen = null;
            for (var attempt = 0; attempt < 50 && screen?.Background != LoadingScreenBackground.SignInMedia; attempt++)
            {
                await Task.Delay(100, Ct);
                screen = await app.Anonymous().GetFromJsonAsync<LoadingScreenDto>("/api/v1/themes/loading-screen", Ct);
            }

            screen!.Background.ShouldBe(LoadingScreenBackground.SignInMedia);
            screen.MediaUrl.ShouldBe(design.MediaUrl);
        }
        finally
        {
            await admin.DeleteAsync($"/api/v1/configuration/{SignInDesign.Section}", Ct);
        }
    }
}
