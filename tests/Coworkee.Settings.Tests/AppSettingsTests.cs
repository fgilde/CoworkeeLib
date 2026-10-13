using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Coworkee.Contracts.Configuration;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Coworkee.Settings.Tests;

public sealed class AppSettingsTests(SettingsApp app) : IAsyncLifetime
{
    private const string Url = "/api/v1/configuration/" + CoworkeeAppSettings.Section;
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        app.RefreshConfiguration();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_built_in_settings_come_with_protected_and_hidden_values()
    {
        var section = (await Admin.GetFromJsonAsync<AppConfigurationValuesDto>(Url, Ct))!;

        section.Protected!.ShouldBe(["Jobs:ConnectionStringName", "Jobs:RunServer", "Jobs:DashboardPath", "Notifications:PublicAppUrl"], ignoreOrder: true);
        section.Locked.ShouldBeEmpty();
        section.Hidden!.ShouldContain("Jobs:Queues");
        section.Values.GetProperty("jobs").TryGetProperty("queues", out _).ShouldBeFalse();
        section.Values.GetProperty("jobs").GetProperty("workerCount").GetInt32().ShouldBe(5);
    }

    [Fact]
    public async Task Protected_values_are_saved_hidden_ones_keep_what_applies()
    {
        var values = (await Admin.GetFromJsonAsync<JsonObject>(Url, Ct))!["values"]!.AsObject();
        values["jobs"]!["workerCount"] = 9;
        values["jobs"]!["dashboardPath"] = "/elsewhere";
        values["jobs"]!["queues"] = new JsonArray("only");

        (await Admin.PutAsJsonAsync(Url, values, Ct)).EnsureSuccessStatusCode();

        var jobs = app.App.Services.GetRequiredService<IOptionsMonitor<CoworkeeAppSettings>>().CurrentValue.Jobs;
        (jobs.WorkerCount, jobs.DashboardPath, string.Join(",", jobs.Queues)).ShouldBe((9, "/elsewhere", "default,mail"));
    }

    [Fact]
    public void A_derived_type_replaces_the_default_and_keeps_its_locks()
    {
        var services = new ServiceCollection();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        services.AddCoworkeeSettings<CoworkeeAppSettings>(configuration);

        services.AddCoworkeeSettings<MyAppSettings>(configuration, rules => rules.Lock(s => s.Billing.Currency).Protect(s => s.Billing.Region).Hide(s => s.Billing.ApiKey));

        var registration = services.BuildServiceProvider().GetServices<AppConfigurationRegistration>().ShouldHaveSingleItem();
        (registration.Type, registration.Section, registration.IsAppSettings).ShouldBe((typeof(MyAppSettings), CoworkeeAppSettings.Section, true));
        registration.Protected.ShouldContain("Jobs:DashboardPath");
        registration.Protected.ShouldContain("Billing:Region");
        registration.Locked.ShouldBe(["Billing:Currency"]);
        registration.Hidden.ShouldContain("Billing:ApiKey");
    }

    [Fact]
    public void Another_type_replaces_the_default_completely()
    {
        var services = new ServiceCollection();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        services.AddCoworkeeSettings<CoworkeeAppSettings>(configuration);

        services.AddCoworkeeSettings<BillingSettings>(configuration, section: "Billing", title: "Billing");

        var registration = services.BuildServiceProvider().GetServices<AppConfigurationRegistration>().ShouldHaveSingleItem();
        (registration.Type, registration.Section, registration.Locked.Count).ShouldBe((typeof(BillingSettings), "Billing", 0));
    }

    [Fact]
    public async Task Services_show_with_their_live_status()
    {
        var result = (await Admin.GetFromJsonAsync<ServiceDirectoryResult>("/api/v1/services", Ct))!;
        var services = result.Services;

        services.Select(s => (s.Name, s.Title, s.Health, s.Source)).ShouldBe(
        [
            ("probed", "probed", ServiceHealth.Healthy, "app"),
            ("self", "Self", ServiceHealth.Healthy, "app"),
            ("sick", "sick", ServiceHealth.Unhealthy, "app"),
            ("billing", "billing", ServiceHealth.Healthy, "aspireui"),
        ]);
        services.Last().Urls.ShouldBe(["http://localhost:5001"]);
        result.UnavailableSources.ShouldBeEmpty();
        app.App.Services.GetRequiredService<IOptions<CoworkeeServicesOptions>>().Value["SELF"].Url.ShouldBe("http://localhost/");
    }

    [Fact]
    public async Task Services_are_shown_in_the_system_organisation_only()
    {
        var (otherAdmin, otherTenant) = await app.CreateTenantAdminAsync();

        (await app.As(otherAdmin, otherTenant).GetAsync("/api/v1/services", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
    }

    public sealed class MyAppSettings : CoworkeeAppSettings
    {
        public BillingSettings Billing { get; set; } = new();
    }

    public sealed class BillingSettings
    {
        public string Currency { get; set; } = "EUR";

        public string Region { get; set; } = "EU";

        public string? ApiKey { get; set; }
    }
}
