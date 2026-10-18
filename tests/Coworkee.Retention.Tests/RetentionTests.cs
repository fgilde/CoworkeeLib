using System.Net;
using System.Net.Http.Json;
using Coworkee.Application.Retention;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Retention;
using Coworkee.Contracts.Settings;
using Coworkee.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Retention.Tests;

public sealed class RetentionTests(RetentionApp app) : IAsyncLifetime
{
    private static readonly string[] LibraryTargets =
    [
        "audit", "snapshots", "outbox", "soft-deleted", "inbox", "ai-tool-calls", "ai-sessions", "webhook-deliveries", "mail-log", "notifications",
        "data-transfer-jobs", "deleted-files", "backups", "auth-tokens",
    ];

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync() => _setup = await app.SetupAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private Task<List<RetentionTargetDto>?> ListAsync() => Admin.GetFromJsonAsync<List<RetentionTargetDto>>("/api/v1/retention", Ct);

    [Fact]
    public async Task Every_registered_target_is_listed_with_the_defaults_of_code_and_configuration()
    {
        var registered = new List<string>();
        await app.InScopeAsync(services =>
        {
            registered.AddRange(services.GetServices<IRetentionTarget>().Select(t => t.Key));
            return Task.CompletedTask;
        });

        var targets = (await ListAsync())!;

        targets.Select(t => t.Key).ShouldBe(registered);
        registered.ShouldBe(LibraryTargets, ignoreOrder: true);
        targets.ShouldAllBe(t => t.Title.Length > 0 && !t.Running && t.LastRunAt == null);
        targets.Where(t => t.Days != null).Select(t => (t.Key, t.Days, t.DefaultDays)).ShouldBe([("outbox", (int?)14, (int?)14), ("webhook-deliveries", (int?)30, (int?)30)], ignoreOrder: true);
        (await Admin.GetFromJsonAsync<List<RetentionTargetDto>>("/api/v1/retention?counts=false", Ct))!.ShouldAllBe(t => t.Affected == null);
    }

    [Fact]
    public async Task Only_the_system_organisation_manages_retention()
    {
        var response = await app.As(_setup.AdminUserId, Guid.NewGuid()).GetAsync("/api/v1/retention", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Days_are_stored_as_settings_and_never_beats_a_default()
    {
        (await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["audit"] = 365, ["webhook-deliveries"] = null }, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.NoContent);

        var targets = (await ListAsync())!;
        targets.Single(t => t.Key == "audit").Days.ShouldBe(365);
        targets.Single(t => t.Key == "webhook-deliveries").Days.ShouldBeNull();
        var global = await Admin.GetFromJsonAsync<List<SettingValueDto>>("/api/v1/settings/global", Ct);
        global!.Single(s => s.Name == "Retention.webhook-deliveries.Days").Value.ShouldBe("0");

        await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["webhook-deliveries"] = 30, ["audit"] = 0 }, Ct);

        targets = (await ListAsync())!;
        (targets.Single(t => t.Key == "audit").Days, targets.Single(t => t.Key == "webhook-deliveries").Days).ShouldBe(((int?)null, (int?)30));
        global = await Admin.GetFromJsonAsync<List<SettingValueDto>>("/api/v1/settings/global", Ct);
        global!.Where(s => s.Name.StartsWith("Retention.", StringComparison.Ordinal)).ShouldAllBe(s => !s.HasValue);
        await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["outbox"] = null }, Ct);
        (await ListAsync())!.Single(t => t.Key == "outbox").Days.ShouldBeNull("never beats a default of the configuration too");
        global = await Admin.GetFromJsonAsync<List<SettingValueDto>>("/api/v1/settings/global", Ct);
        global!.Single(s => s.Name == "Retention.outbox.Days").Value.ShouldBe("0");
        await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["outbox"] = 14 }, Ct);
        global = await Admin.GetFromJsonAsync<List<SettingValueDto>>("/api/v1/settings/global", Ct);
        global!.Single(s => s.Name == "Retention.outbox.Days").HasValue.ShouldBeFalse();
        (await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["nothing"] = 3 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["audit"] = -1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_nightly_job_deletes_what_is_older_than_the_days_of_every_target()
    {
        var oldBlobs = await Seed.EverythingAsync(app, _setup.TenantId, _setup.AdminUserId, "old");
        app.Clock.Advance(TimeSpan.FromDays(100));
        var newBlobs = await Seed.EverythingAsync(app, _setup.TenantId, _setup.AdminUserId, "new");
        await Admin.PutAsJsonAsync("/api/v1/retention", LibraryTargets.ToDictionary(k => k, _ => (int?)30), Ct);
        (await ListAsync())!.Where(t => !(t.Affected > 0)).Select(t => t.Key).ShouldBeEmpty("every target sees its old row");

        await app.RunNightlyAsync();

        var targets = (await ListAsync())!;
        targets.Where(t => t.LastError != null).Select(t => $"{t.Key}: {t.LastError}").ShouldBeEmpty();
        targets.ShouldAllBe(t => t.Affected == 0 && t.LastDeleted > 0 && t.LastRunAt == app.Clock.GetUtcNow() && !t.Running);
        await app.InScopeAsync(async services =>
        {
            var blobs = services.GetRequiredService<IBlobStorage>();
            foreach (var key in oldBlobs)
            {
                (await blobs.OpenReadAsync(key, Ct)).ShouldBeNull(key);
            }

            foreach (var key in newBlobs)
            {
                await using var kept = await blobs.OpenReadAsync(key, Ct);
                kept.ShouldNotBeNull(key);
            }
        });
        var left = await app.InDbAsync(null, async db => (
            await db.Set<Gadget>().IgnoreQueryFilters().CountAsync(Ct),
            await db.Set<Infrastructure.Outbox.OutboxMessage>().CountAsync(m => m.Type == "pending", Ct),
            await db.Set<Webhooks.WebhookDelivery>().IgnoreQueryFilters().CountAsync(Ct),
            await db.Set<Mailing.OutgoingMail>().IgnoreQueryFilters().CountAsync(Ct),
            await db.Set<OData.Transfer.DataTransferJob>().IgnoreQueryFilters().CountAsync(Ct),
            await db.Set<Files.FileFolder>().IgnoreQueryFilters().CountAsync(Ct),
            await db.Set<Ai.Sessions.AiChatMessage>().CountAsync(Ct)));
        // the old deleted gadget went; pending, queued and running rows of the old seed stay
        left.ShouldBe((3, 2, 3, 3, 3, 2, 1));
    }

    [Fact]
    public async Task A_run_from_the_page_is_queued_once_and_remembers_who_started_it()
    {
        await Seed.EverythingAsync(app, _setup.TenantId, _setup.AdminUserId, "old");
        app.Clock.Advance(TimeSpan.FromDays(10));
        (await Admin.GetFromJsonAsync<long>("/api/v1/retention/notifications/count?days=5", Ct)).ShouldBe(1);

        (await Admin.PostAsJsonAsync("/api/v1/retention/notifications/run", new RunRetentionRequest(5), Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await Admin.PostAsJsonAsync("/api/v1/retention/notifications/run", new RunRetentionRequest(5), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ListAsync())!.Single(t => t.Key == "notifications").Running.ShouldBeTrue();
        app.Jobs.Count.ShouldBe(1);

        await app.RunQueuedJobsAsync();

        var notifications = (await ListAsync())!.Single(t => t.Key == "notifications");
        (notifications.Running, notifications.LastDeleted, notifications.Days).ShouldBe((false, (long?)1, (int?)null));
        var run = await app.InDbAsync(null, db => db.Set<RetentionRun>().SingleAsync(r => r.Key == "notifications", Ct));
        run.LastRunBy.ShouldBe(_setup.AdminUserId);
        (await Admin.GetFromJsonAsync<long>("/api/v1/retention/notifications/count?days=5", Ct)).ShouldBe(0);
        (await Admin.PostAsJsonAsync("/api/v1/retention/notifications/run", new RunRetentionRequest(0), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Admin.PostAsJsonAsync("/api/v1/retention/nothing/run", new RunRetentionRequest(5), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_soft_deleted_type_held_by_a_foreign_key_is_reported_and_the_others_still_go()
    {
        var owner = new Owner { Name = "held", IsDeleted = true, DeletedAt = app.Clock.GetUtcNow() };
        await app.InDbAsync(_setup.TenantId, db =>
        {
            db.AddRange(owner, new Pet { OwnerId = owner.Id }, new Gadget { Name = "gone", IsDeleted = true, DeletedAt = app.Clock.GetUtcNow() });
            return db.SaveChangesAsync(Ct);
        });
        app.Clock.Advance(TimeSpan.FromDays(10));
        await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["soft-deleted"] = 5, ["webhook-deliveries"] = null, ["outbox"] = null }, Ct);

        await app.RunNightlyAsync();

        var target = (await ListAsync())!.Single(t => t.Key == "soft-deleted");
        (target.LastDeleted, target.Affected).ShouldBe(((long?)1, (long?)1));
        target.LastError.ShouldNotBeNull().ShouldStartWith("Owner:");
        (await app.InDbAsync(null, db => db.Set<Gadget>().IgnoreQueryFilters().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Soft_deleted_walks_the_hierarchy_and_leaves_types_of_other_targets_alone()
    {
        var deletedAt = app.Clock.GetUtcNow();
        await app.InDbAsync(_setup.TenantId, db =>
        {
            db.AddRange(
                new Device { Name = "device", IsDeleted = true, DeletedAt = deletedAt },
                new Phone { Name = "phone", IsDeleted = true, DeletedAt = deletedAt },
                new Vehicle { Name = "vehicle" },
                new Car { Name = "car", IsDeleted = true, DeletedAt = deletedAt });
            return db.SaveChangesAsync(Ct);
        });
        app.Clock.Advance(TimeSpan.FromDays(10));
        await Admin.PutAsJsonAsync("/api/v1/retention", new Dictionary<string, int?> { ["soft-deleted"] = 5, ["webhook-deliveries"] = null, ["outbox"] = null }, Ct);

        await app.RunNightlyAsync();

        var target = (await ListAsync())!.Single(t => t.Key == "soft-deleted");
        (target.LastDeleted, target.LastError).ShouldBe(((long?)2, (string?)null));
        var left = await app.InDbAsync(null, async db => (
            await db.Set<Device>().IgnoreQueryFilters().Select(d => d.Name).OrderBy(n => n).ToListAsync(Ct),
            await db.Set<Vehicle>().IgnoreQueryFilters().Select(v => v.Name).OrderBy(n => n).ToListAsync(Ct)));
        left.Item1.ShouldBe(["phone"]);
        left.Item2.ShouldBe(["vehicle"]);
    }

    [Fact]
    public async Task A_run_that_lost_its_claim_to_a_later_run_leaves_that_claim_alone()
    {
        Guid? first = null, second = null;
        await app.InScopeAsync(async services => first = await services.GetRequiredService<RetentionRunner>().TryClaimAsync("notifications", Ct));
        await app.InScopeAsync(async services => (await services.GetRequiredService<RetentionRunner>().TryClaimAsync("notifications", Ct)).ShouldBeNull());
        app.Clock.Advance(RetentionRunner.StaleAfter + TimeSpan.FromMinutes(1));
        await app.InScopeAsync(async services => second = await services.GetRequiredService<RetentionRunner>().TryClaimAsync("notifications", Ct));

        await app.InScopeAsync(services => services.GetRequiredService<RetentionRunner>().RunAsync("notifications", 5, null, first!.Value, Ct));

        (await ListAsync())!.Single(t => t.Key == "notifications").Running.ShouldBeTrue();
        await app.InScopeAsync(services => services.GetRequiredService<RetentionRunner>().RunAsync("notifications", 5, null, second!.Value, Ct));
        var notifications = (await ListAsync())!.Single(t => t.Key == "notifications");
        (notifications.Running, notifications.LastRunAt).ShouldBe((false, (DateTimeOffset?)app.Clock.GetUtcNow()));
    }
}
