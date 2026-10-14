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
    public async Task A_signed_in_user_gets_the_theme_they_chose_when_everyone_in_their_organisation_may_choose_it()
    {
        var admin = app.As(_setup.AdminUserId, _setup.TenantId);
        var published = await CreateAsync(admin, "Published", true);
        var draft = await CreateAsync(admin, "Draft", false);
        var forest = (await admin.GetFromJsonAsync<ThemeDto[]>("/api/v1/themes", Ct))!.Single(t => t.Name == "Forest");
        var (otherUser, otherTenant) = await app.CreateTenantAdminAsync();
        var stranger = app.As(otherUser, otherTenant);

        (await ScreenAsync(admin, published.Id)).ShouldBe("Published");
        (await ScreenAsync(admin, forest.Id)).ShouldBe("Forest");
        (await ScreenAsync(admin, draft.Id)).ShouldBe("Ocean");
        (await ScreenAsync(app.Anonymous(), published.Id)).ShouldBe("Ocean");
        (await ScreenAsync(stranger, published.Id)).ShouldBe("Ocean");
        (await ScreenAsync(admin, Guid.NewGuid())).ShouldBe("Ocean");
    }

    private static async Task<ThemeDto> CreateAsync(HttpClient admin, string name, bool published)
    {
        var palette = System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" });
        var response = await admin.PostAsJsonAsync("/api/v1/themes", new ThemeRequest(name, palette, palette, null, null, null, null, IsPublished: published), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ThemeDto>(Ct))!;
    }

    private static async Task<string?> ScreenAsync(HttpClient client, Guid themeId) =>
        (await client.GetFromJsonAsync<LoadingScreenDto>($"/api/v1/themes/loading-screen?themeId={themeId}", Ct))!.Theme?.Name;

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
