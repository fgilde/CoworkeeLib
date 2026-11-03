using System.Net.Http.Json;
using Coworkee.Application.Caching;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Settings.Tests;

/// <summary>The settings group "Cache" gives the cached queries their defaults; 0 minutes means none.</summary>
public sealed class CacheDefaultsTests(SettingsApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_cache_settings_are_the_defaults_of_cached_queries()
    {
        (await ReadAsync()).ShouldBe(new CacheDefaults(TimeSpan.FromMinutes(5), null, true));

        (await app.As(_setup.AdminUserId, _setup.TenantId).PutAsJsonAsync("/api/v1/settings/global", new SetSettingsRequest(new Dictionary<string, string?>
        {
            [CacheSettings.DurationMinutes] = "15",
            [CacheSettings.RefreshAfterMinutes] = "2",
            [CacheSettings.RefreshInBackground] = "false",
        }), Ct)).EnsureSuccessStatusCode();

        (await ReadAsync()).ShouldBe(new CacheDefaults(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(2), false));
    }

    private async Task<CacheDefaults> ReadAsync()
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICacheDefaults>().GetAsync(Ct);
    }
}
