using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Theming;
using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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
        Read("resources/css/coworkee.css").ShouldContain("url(\"data:image/svg+xml;base64,");
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

    [Fact]
    public async Task Other_organisations_see_no_keycloak_theme_and_cannot_apply_it()
    {
        var (user, tenant) = await app.CreateTenantAdminAsync();
        var other = app.As(user, tenant);

        (await other.GetFromJsonAsync<KeycloakThemeStatusDto>("/api/v1/themes/keycloak", Ct))!.ShouldBe(new KeycloakThemeStatusDto(false, null, null, null));
        (await other.PostAsync("/api/v1/themes/keycloak/apply", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_theme_is_the_system_organisations_whoever_asks()
    {
        var (user, tenant) = await app.CreateTenantAdminAsync();
        var palette = JsonSerializer.SerializeToElement(new Dictionary<string, string> { ["Primary"] = "#654321" });
        var created = await app.As(user, tenant).PostAsJsonAsync("/api/v1/themes", new ThemeRequest("Theirs", palette, palette, null, null, null, null), Ct);
        var theme = (await created.Content.ReadFromJsonAsync<ThemeDto>(Ct))!;
        (await app.As(user, tenant).PostAsync($"/api/v1/themes/{theme.Id}/default", null, Ct)).EnsureSuccessStatusCode();

        using (CurrentUserScope.Begin(new ImpersonatedUser(user, tenant)))
        {
            await app.App.Services.GetRequiredService<KeycloakThemeSync>().WriteAsync(Ct);
        }

        Read("resources/css/coworkee.css").ShouldContain("--cw-primary:#1565c0");
        Read("resources/css/coworkee.css").ShouldNotContain("#654321");
    }

    [Fact]
    public async Task A_failed_write_reports_a_generic_error_and_keeps_paths_out_of_it()
    {
        var folder = Directory.CreateTempSubdirectory("keycloak-fail-").FullName;
        try
        {
            using var sync = new KeycloakThemeSync(app.App.Services.GetRequiredService<IServiceScopeFactory>(),
                new FixedOptions<KeycloakThemeOptions>(new() { ThemeDirectory = folder, ThemeName = "../Bad" }), new FixedOptions<SignInDesign>(new()), TimeProvider.System,
                NullLogger<KeycloakThemeSync>.Instance);

            var status = await sync.WriteAsync(Ct);

            status.Error.ShouldBe(KeycloakThemeSync.Failed);
            status.Error!.ShouldNotContain(folder);
            status.WrittenAt.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class FixedOptions<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
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
