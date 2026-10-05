using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Settings.Tests;

public sealed class AppConfigurationTests(SettingsApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        app.RefreshConfiguration();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private TestAppConfig Options => app.App.Services.GetRequiredService<IOptionsMonitor<TestAppConfig>>().CurrentValue;

    private async Task<AppConfigurationValuesDto> SaveAsync(object values)
    {
        var response = await Admin.PutAsJsonAsync("/api/v1/configuration/TestApp", values, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AppConfigurationValuesDto>(Ct))!;
    }

    [Fact]
    public void Defaults_from_the_section_file_lie_below_appsettings() =>
        (Options.Mode, Options.Name).ShouldBe(("file", "Default"));

    [Fact]
    public async Task Lists_the_registered_sections() =>
        (await Admin.GetFromJsonAsync<AppConfigurationDto[]>("/api/v1/configuration", Ct))!.ShouldBe([new AppConfigurationDto("TestApp", "Test app")]);

    [Fact]
    public async Task Shows_the_section_typed_with_secrets_masked()
    {
        var section = (await Admin.GetFromJsonAsync<AppConfigurationValuesDto>("/api/v1/configuration/TestApp", Ct))!;

        var values = section.Values.Deserialize<TestAppConfig>(JsonSerializerOptions.Web)!;
        (values.Name, values.Limit, values.ApiKey, string.Join(",", values.Tags)).ShouldBe(("Default", 10, AppConfigurationMask.Value, "a,b"));
        section.ChangedKeys.ShouldBeEmpty();
    }

    [Fact]
    public async Task Saving_stores_only_what_differs_and_applies_at_once()
    {
        var saved = await SaveAsync(new TestAppConfig { Mode = "file", Name = "Changed", Limit = 10, ApiKey = AppConfigurationMask.Value, Tags = ["a", "b"] });

        (Options.Name, Options.Limit, Options.ApiKey).ShouldBe(("Changed", 10, "secret-default"));
        app.App.Services.GetRequiredService<IConfiguration>()["TestApp:Name"].ShouldBe("Changed");
        saved.ChangedKeys.ShouldBe(["TestApp:Name"]);
        (await app.InDbAsync(db => db.Set<ConfigurationEntry>().Select(e => e.Key).ToListAsync())).ShouldBe(["TestApp:Name"]);
    }

    [Fact]
    public async Task A_new_secret_is_kept_and_a_shorter_list_empties_the_default_items()
    {
        await SaveAsync(new TestAppConfig { Mode = "file", Name = "Default", Limit = 3, ApiKey = "new-secret", Tags = ["x"] });

        // configuration sources only add keys: the default's further items stay, empty
        (Options.Limit, Options.ApiKey, string.Join(",", Options.Tags)).ShouldBe((3, "new-secret", "x,"));
        var shown = (await Admin.GetFromJsonAsync<AppConfigurationValuesDto>("/api/v1/configuration/TestApp", Ct))!;
        shown.Values.GetProperty("apiKey").GetString().ShouldBe(AppConfigurationMask.Value);
    }

    [Fact]
    public async Task Reset_restores_the_configured_values()
    {
        await SaveAsync(new TestAppConfig { Mode = "file", Name = "Changed", Limit = 99, ApiKey = AppConfigurationMask.Value, Tags = ["a", "b"] });

        (await Admin.DeleteAsync("/api/v1/configuration/TestApp", Ct)).EnsureSuccessStatusCode();

        (Options.Name, Options.Limit).ShouldBe(("Default", 10));
        (await app.InDbAsync(db => db.Set<ConfigurationEntry>().CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Another_service_reads_the_change_from_the_database()
    {
        await SaveAsync(new TestAppConfig { Mode = "file", Name = "Shared", Limit = 10, ApiKey = AppConfigurationMask.Value, Tags = ["a", "b"] });

        var other = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:test"] = app.ConnectionString, ["TestApp:Name"] = "Default" })
            .AddCoworkeeDatabaseConfiguration("test")
            .Build();

        other["TestApp:Name"].ShouldBe("Shared");
    }

    [Fact]
    public async Task Unknown_sections_and_ill_fitting_values_are_refused()
    {
        (await Admin.PutAsJsonAsync("/api/v1/configuration/Logging", new { LogLevel = new { Default = "None" } }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PutAsJsonAsync("/api/v1/configuration/TestApp", new { limit = "many" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_settings_managers_of_the_system_organisation_see_the_configuration()
    {
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();
        var plain = await app.InDbAsync(async db =>
        {
            var email = $"user-{Guid.NewGuid():N}@acme.test";
            var created = new Coworkee.Identity.Domain.User { TenantId = _setup.TenantId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email, NormalizedEmail = email.ToUpperInvariant() };
            db.Add(created);
            await db.SaveChangesAsync();
            return created.Id;
        });

        (await app.As(plain, _setup.TenantId).GetAsync("/api/v1/configuration/TestApp", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(otherAdmin, otherTenant).GetAsync("/api/v1/configuration/TestApp", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(otherAdmin, otherTenant).DeleteAsync("/api/v1/configuration/TestApp", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
