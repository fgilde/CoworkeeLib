using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Privacy;
using Coworkee.Contracts.Reporting;
using Coworkee.Reporting.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Coworkee.Reporting.Tests;

public sealed class ReportPersonalDataTests(ReportingApp app) : ReportingTestBase(app)
{
    private Guid _bob;
    private Guid _carl;
    private Guid _private;
    private Guid _shared;
    private Guid _carls;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _bob = (await CreateUserAsync("bob@acme.test", ReportPermissions.View)).Id;
        _carl = (await CreateUserAsync("carl@acme.test", ReportPermissions.View)).Id;
        _private = await AddReportAsync(_bob, "Bob's notes", ReportVisibility.Private);
        _shared = await AddReportAsync(_bob, "Team numbers", ReportVisibility.Organisation);
        _carls = await AddReportAsync(_carl, "Carl's board", ReportVisibility.Organisation);
        await App.InDbAsync(Setup.TenantId, async db =>
        {
            db.AddRange(new ReportFavorite { UserId = _bob, ReportId = _carls }, new ReportFavorite { UserId = _carl, ReportId = _shared });
            db.AddRange(Subscription(_bob, _carls), Subscription(_carl, _shared));
            db.AddRange(
                new DashboardReport { TenantId = Setup.TenantId, UserId = _bob, ReportId = _private },
                new DashboardReport { TenantId = Setup.TenantId, UserId = _carl, ReportId = _shared });
            await db.SaveChangesAsync(Ct);
        });
    }

    [Fact]
    public async Task The_export_holds_his_reports_favourites_subscriptions_and_dashboard()
    {
        var section = (await App.As(_bob, Setup.TenantId).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct)).GetProperty("reports");

        section.GetProperty("reports").EnumerateArray().Select(r => r.GetProperty("name").GetString()).ShouldBe(["Bob's notes", "Team numbers"], ignoreOrder: true);
        section.GetProperty("favorites").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Carl's board");
        section.GetProperty("subscriptions").EnumerateArray().Single().GetProperty("report").GetString().ShouldBe("Carl's board");
        section.GetProperty("dashboard").GetGuid().ShouldBe(_private);
    }

    [Fact]
    public async Task Erasing_deletes_his_private_reports_and_choices_and_hands_his_shared_reports_to_nobody()
    {
        (await Admin.DeleteAsync($"/api/v1/identity/users/{_bob}", Ct)).EnsureSuccessStatusCode();

        var reports = await ReadAsync(db => db.Set<Report>().ToDictionaryAsync(r => r.Id, r => r.OwnerId, Ct));
        reports.ShouldNotContainKey(_private);
        reports[_shared].ShouldBe(PersonalDataSubject.ErasedUserId);
        reports[_carls].ShouldBe(_carl);
        (await ReadAsync(db => db.Set<ReportFavorite>().Select(f => f.UserId).ToListAsync(Ct))).ShouldBe([_carl]);
        (await ReadAsync(db => db.Set<ReportSubscription>().IgnoreQueryFilters().Select(s => s.UserId).ToListAsync(Ct))).ShouldBe([_carl]);
        (await ReadAsync(db => db.Set<DashboardReport>().Select(d => d.UserId).ToListAsync(Ct))).ShouldBe([_carl]);
    }

    private async Task<T> ReadAsync<T>(Func<ReportingTestDbContext, Task<T>> read)
    {
        T result = default!;
        await App.InDbAsync(Setup.TenantId, async db => result = await read(db));
        return result;
    }

    private ReportSubscription Subscription(Guid userId, Guid reportId) => new()
    {
        TenantId = Setup.TenantId, UserId = userId, ReportId = reportId, Frequency = SubscriptionFrequency.Daily, TimeZone = "UTC", NextRunAt = DateTimeOffset.UtcNow.AddDays(1),
    };

    private async Task<Guid> AddReportAsync(Guid owner, string name, ReportVisibility visibility)
    {
        var report = new Report { TenantId = Setup.TenantId, OwnerId = owner, Name = name, Visibility = visibility, Definition = "{\"widgets\":[]}" };
        await App.InDbAsync(Setup.TenantId, async db =>
        {
            db.Add(report);
            await db.SaveChangesAsync(Ct);
        });
        return report.Id;
    }
}
