using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Theming;

namespace Coworkee.Theming.Tests;

public sealed class KeycloakThemeSyncTests(ThemeApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private string Login => Path.Combine(app.KeycloakThemes, "coworkee-test", "login");

    [Fact]
    public async Task Saving_the_default_theme_rewrites_the_keycloak_login_theme()
    {
        var palette = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#123456" });
        var created = await Admin.PostAsJsonAsync("/api/v1/themes", new ThemeRequest("Keycloak", palette, palette, null, null, "<svg viewBox=\"0 0 1 1\"></svg>", null), Ct);
        var theme = (await created.Content.ReadFromJsonAsync<ThemeDto>(Ct))!;

        (await Admin.PostAsync($"/api/v1/themes/{theme.Id}/default", null, Ct)).EnsureSuccessStatusCode();

        await EventuallyAsync(() => Read("resources/css/coworkee.css").Contains("--cw-primary:#123456", StringComparison.Ordinal));
        File.Exists(Path.Combine(Login, "resources", "img", "logo.svg")).ShouldBeTrue();
        Read("messages/messages_en.properties").ShouldContain("Acme App");
    }

    [Fact]
    public async Task Saving_the_sign_in_design_rewrites_the_layout_with_media_on_the_web_app()
    {
        try
        {
            (await Admin.PutAsJsonAsync($"/api/v1/configuration/{SignInDesign.Section}", new SignInDesign { Layout = "Split", MediaUrl = "/media/hero.jpg" }, Ct)).EnsureSuccessStatusCode();

            await EventuallyAsync(() => Read("resources/js/coworkee.js").Contains("\"layout\":\"split\"", StringComparison.Ordinal));
            Read("resources/js/coworkee.js").ShouldContain("\"media\":\"https://web.test/media/hero.jpg\"");
        }
        finally
        {
            await Admin.DeleteAsync($"/api/v1/configuration/{SignInDesign.Section}", Ct);
        }
    }

    [Fact]
    public async Task Admins_see_the_status_and_write_the_theme_at_once()
    {
        if (Directory.Exists(Path.Combine(app.KeycloakThemes, "coworkee-test")))
        {
            Directory.Delete(Path.Combine(app.KeycloakThemes, "coworkee-test"), recursive: true);
        }

        var applied = await Admin.PostAsync("/api/v1/themes/keycloak/apply", null, Ct);

        applied.EnsureSuccessStatusCode();
        var status = (await applied.Content.ReadFromJsonAsync<KeycloakThemeStatusDto>(Ct))!;
        status.Configured.ShouldBeTrue();
        status.ThemeName.ShouldBe("coworkee-test");
        status.WrittenAt.ShouldNotBeNull();
        status.Error.ShouldBeNull();
        KeycloakLoginTheme.Exists(app.KeycloakThemes, "coworkee-test").ShouldBeTrue();
        (await Admin.GetFromJsonAsync<KeycloakThemeStatusDto>("/api/v1/themes/keycloak", Ct))!.WrittenAt.ShouldNotBeNull();
        (await app.Anonymous().GetAsync("/api/v1/themes/keycloak", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private string Read(string path)
    {
        try
        {
            return File.ReadAllText(Path.Combine(Login, path));
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return string.Empty;
        }
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(100, Ct);
        }

        condition().ShouldBeTrue();
    }
}
