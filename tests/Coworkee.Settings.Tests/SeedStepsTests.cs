using Coworkee.Contracts.Settings;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Settings.Tests;

public sealed class SeedStepsTests(SettingsApp app) : IAsyncLifetime
{
    public async ValueTask InitializeAsync() => await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_seed_step_runs_once_per_tenant()
    {
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        var name = "Test.Seed." + Guid.NewGuid().ToString("N");

        await app.InDbAsync(async db =>
        {
            (await db.ClaimOnceAsync(tenant, name, Ct)).ShouldBeTrue();
            (await db.ClaimOnceAsync(tenant, name, Ct)).ShouldBeFalse();
            await db.SaveChangesAsync(Ct);
            return 0;
        });

        await app.InDbAsync(async db =>
        {
            (await db.ClaimOnceAsync(tenant, name, Ct)).ShouldBeFalse();
            (await db.ClaimOnceAsync(other, name, Ct)).ShouldBeTrue();
            (await db.ClaimOnceAsync(null, name, Ct)).ShouldBeTrue();
            return 0;
        });

        var stored = await app.InDbAsync(db => db.Set<SettingValue>().IgnoreQueryFilters().SingleAsync(v => v.Name == name, Ct));
        stored.Scope.ShouldBe(SettingScope.Tenant);
        stored.ScopeKey.ShouldBe(tenant);
        stored.Value.ShouldBe("done");
    }

    [Fact]
    public async Task An_existing_marker_with_any_value_counts_as_done()
    {
        var tenant = Guid.NewGuid();
        var name = "Test.Seed." + Guid.NewGuid().ToString("N");

        await app.InDbAsync(async db =>
        {
            db.Add(new SettingValue { Name = name, Scope = SettingScope.Tenant, ScopeKey = tenant, Value = "granted" });
            await db.SaveChangesAsync(Ct);
            return 0;
        });

        (await app.InDbAsync(db => db.ClaimOnceAsync(tenant, name, Ct))).ShouldBeFalse();
    }
}
