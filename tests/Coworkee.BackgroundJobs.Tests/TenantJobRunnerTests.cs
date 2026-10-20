using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.BackgroundJobs.Tests;

public sealed class TenantJobRunnerTests(JobsApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Work_runs_in_every_active_organisation_as_its_system_in_an_own_scope_and_a_failing_one_does_not_stop_the_others()
    {
        var setup = await app.SetupAsync();
        var (second, inactive) = (await AddTenantAsync("Second", true), await AddTenantAsync("Gone", false));
        var calls = new List<(Guid TenantId, Guid? Actor, Guid? User, IServiceProvider Services)>();

        var failed = await app.App.Services.GetRequiredService<TenantJobRunner>().ForEachTenantAsync((services, tenantId, _) =>
        {
            var current = services.GetRequiredService<ICurrentUser>();
            calls.Add((tenantId, current.TenantId, current.UserId, services));
            return tenantId == setup.TenantId ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }, Ct);

        failed.ShouldBe([setup.TenantId]);
        calls.Select(c => c.TenantId).ShouldBe([setup.TenantId, second], ignoreOrder: true);
        calls.ShouldAllBe(c => c.Actor == c.TenantId && c.User == null);
        calls.Select(c => c.Services).Distinct().Count().ShouldBe(2);
        calls.ShouldNotContain(c => c.TenantId == inactive);
    }

    private async Task<Guid> AddTenantAsync(string name, bool active)
    {
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsTestDbContext>();
        var tenant = new Tenant { Name = name, Identifier = name.ToLowerInvariant(), IsActive = active };
        db.Add(tenant);
        await db.SaveChangesAsync(Ct);
        return tenant.Id;
    }
}
