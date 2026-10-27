using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Tokens;
using Coworkee.OData.Transfer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

public sealed class ExportTests(ODataApp app) : IAsyncLifetime
{
    private static readonly ReportSchedule Weekly = new(SubscriptionFrequency.Weekly, new TimeOnly(7, 0), "UTC", DayOfWeek.Monday);

    private SetupResultDto _setup = null!;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(
                new Gadget { Name = "Drill", Category = "Tools", Price = 99.5m, TenantId = _setup.TenantId },
                new Gadget { Name = "Hammer", Category = "Tools", Price = 19, TenantId = _setup.TenantId },
                new Gadget { Name = "Lamp", Category = "Light", Price = 35, TenantId = _setup.TenantId });
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_stream_reports_progress_and_ends_with_a_link_that_downloads_once()
    {
        var lines = await StreamAsync(Admin, "?$filter=Category eq 'Tools'");

        lines[0].Phase.ShouldBe(DataExportProgress.Counting);
        lines.Select(l => l.Percent).ShouldBeInOrder();
        var ready = lines[^1];
        (ready.Phase, ready.Percent, ready.FileName).ShouldBe((DataExportProgress.Ready, 100, "Gadgets.xlsx"));
        var anonymous = app.App.GetTestClient();
        using (var file = await anonymous.GetAsync("/" + ready.Url, Ct))
        {
            file.StatusCode.ShouldBe(HttpStatusCode.OK);
            using var workbook = new XLWorkbook(await file.Content.ReadAsStreamAsync(Ct));
            workbook.Worksheet(1).RowsUsed().Count().ShouldBe(3);
        }

        (await anonymous.GetAsync("/" + ready.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetFromJsonAsync<DataTransferJobDto>($"/api/v1/data/jobs/{ready.JobId}", Ct))!.Status.ShouldBe(DataTransferStatus.Completed);
        app.Mails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_stream_of_more_rows_than_a_request_takes_hands_over_to_the_export_job()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Enumerable.Range(1, 4).Select(i => new Gadget { Name = $"Bulk {i}", Category = "Bulk", Price = i, TenantId = _setup.TenantId }));
            return await db.SaveChangesAsync(Ct);
        });

        var last = (await StreamAsync(Admin, "?format=csv"))[^1];

        (last.Phase, last.Url).ShouldBe((DataExportProgress.Queued, null));
        var job = (await Admin.GetFromJsonAsync<DataTransferJobDto>($"/api/v1/data/jobs/{last.JobId}", Ct))!;
        (job.Status, job.ResultFileName).ShouldBe((DataTransferStatus.Completed, "Gadgets.csv"));
    }

    [Fact]
    public async Task Each_user_streams_only_a_few_exports_at_once_and_stale_ones_fail()
    {
        var now = DateTimeOffset.UtcNow;
        var stale = Job(now.AddHours(-2));
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(stale, Job(now.AddMinutes(-1)), Job(now));
            return await db.SaveChangesAsync(Ct);
        });

        var refused = await Admin.GetAsync("/api/v1/data/Gadgets/export/stream", Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var failed = await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().AsNoTracking().SingleAsync(j => j.Id == stale.Id, Ct));
        (failed.Status, failed.Message).ShouldBe((DataTransferStatus.Failed, "The export took too long."));
        await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().Where(j => j.Status == DataTransferStatus.Running).ExecuteDeleteAsync(Ct));
        (await StreamAsync(Admin, "?format=csv"))[^1].Phase.ShouldBe(DataExportProgress.Ready);

        DataTransferJob Job(DateTimeOffset createdAt) => new()
        {
            TenantId = _setup.TenantId, UserId = _setup.AdminUserId, Kind = DataTransferKind.Export, EntitySet = "Gadgets", Status = DataTransferStatus.Running, CreatedAt = createdAt,
        };
    }

    [Fact]
    public async Task The_download_link_expires_after_five_minutes()
    {
        var ready = (await StreamAsync(Admin, "?format=csv"))[^1];
        var token = await app.InDbAsync(null, db => db.Set<AccessToken>().SingleAsync(Ct));
        token.ExpiresAt.ShouldBeInRange(DateTimeOffset.UtcNow.AddMinutes(4), DateTimeOffset.UtcNow.AddMinutes(5));

        await app.InDbAsync(null, db => db.Set<AccessToken>().ExecuteUpdateAsync(t => t.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)), Ct));

        (await app.App.GetTestClient().GetAsync("/" + ready.Url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task When_the_client_leaves_the_export_finishes_and_is_mailed()
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        using var aborted = new CancellationTokenSource();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = aborted.Token };
        http.Request.QueryString = new QueryString("?format=csv");
        http.Response.Body = new LeavingClient(aborted);

        await scope.ServiceProvider.GetRequiredService<DataExports>().StreamAsync("Gadgets", http);

        var mail = app.Mails.Sent.ShouldHaveSingleItem();
        (mail.OwnerId, mail.File.Name, mail.Recipients.Count).ShouldBe((_setup.AdminUserId, "Gadgets.csv", 0));
        (await app.InDbAsync(null, db => db.Set<AccessToken>().CountAsync(Ct))).ShouldBe(0);
        (await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().SingleAsync(Ct))).Status.ShouldBe(DataTransferStatus.Completed);
    }

    [Fact]
    public async Task A_queued_export_is_mailed_to_the_requester_and_the_recipients()
    {
        var response = await Admin.PostAsJsonAsync("/api/v1/data/Gadgets/export/mail", new MailExportRequest("$filter=Category eq 'Light'", ["boss@acme.test"], "Lights"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var mail = app.Mails.Sent.ShouldHaveSingleItem();
        (mail.File.Name, mail.Recipients.Single()).ShouldBe(("Lights.xlsx", "boss@acme.test"));
        (await Admin.PostAsJsonAsync("/api/v1/data/Gadgets/export/mail", new MailExportRequest(null, ["no address"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Streams_and_mails_to_others_need_their_permissions()
    {
        var nobody = app.As(await UserWithAsync(), _setup.TenantId);
        var viewer = app.As(await UserWithAsync(ODataApp.ViewGadgets), _setup.TenantId);

        (await nobody.GetAsync("/api/v1/data/Gadgets/export/stream", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/v1/data/Gadgets/export/mail", new MailExportRequest(null, ["boss@acme.test"]), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/v1/data/Gadgets/export/mail", new MailExportRequest(null), Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await viewer.GetAsync("/api/v1/data/export-schedules", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var planner = app.As(await UserWithAsync(DataTransferPermissions.ExportSchedules), _setup.TenantId);
        (await planner.PostAsJsonAsync("/api/v1/data/export-schedules", Request(), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_schedule_runs_as_its_owner_mails_the_recipients_and_moves_on()
    {
        var saved = await (await Admin.PostAsJsonAsync("/api/v1/data/export-schedules", Request(), Ct)).Content.ReadFromJsonAsync<ExportScheduleDto>(Ct);
        saved!.NextRunAt.ShouldBe(ReportSchedules.Next(Weekly, DateTimeOffset.UtcNow), TimeSpan.FromMinutes(1));
        saved.Recipients.ShouldBe(["boss@acme.test"]);
        await app.InDbAsync(null, db => db.Set<ExportSchedule>().IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExportScheduleJob>().ExecuteAsync(Ct);
        }

        var mail = app.Mails.Sent.ShouldHaveSingleItem();
        (mail.OwnerId, mail.Name, mail.File.Name, mail.Recipients.Single()).ShouldBe((_setup.AdminUserId, "Tools weekly", "tools.csv", "boss@acme.test"));
        var schedule = (await Admin.GetFromJsonAsync<ExportScheduleDto[]>("/api/v1/data/export-schedules", Ct))!.Single();
        schedule.LastRunAt.ShouldNotBeNull();
        schedule.LastError.ShouldBeNull();
        schedule.NextRunAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        var jobs = await Admin.GetFromJsonAsync<DataTransferJobDto[]>("/api/v1/data/jobs", Ct);
        jobs!.Single().ResultFileName.ShouldBe("tools.csv");
    }

    [Fact]
    public async Task Schedules_belong_to_their_owner_in_their_organisation()
    {
        var saved = await (await Admin.PostAsJsonAsync("/api/v1/data/export-schedules", Request(), Ct)).Content.ReadFromJsonAsync<ExportScheduleDto>(Ct);
        var colleague = app.As(await UserWithAsync(DataTransferPermissions.ExportSchedules, ODataApp.ViewGadgets), _setup.TenantId);

        (await app.InDbAsync(Guid.CreateVersion7(), db => db.Set<ExportSchedule>().CountAsync(Ct))).ShouldBe(0);
        (await app.InDbAsync(_setup.TenantId, db => db.Set<ExportSchedule>().CountAsync(Ct))).ShouldBe(1);
        (await colleague.GetFromJsonAsync<ExportScheduleDto[]>("/api/v1/data/export-schedules", Ct))!.ShouldBeEmpty();
        (await colleague.PostAsync($"/api/v1/data/export-schedules/{saved!.Id}/run", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await colleague.DeleteAsync($"/api/v1/data/export-schedules/{saved.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var ran = await (await Admin.PostAsync($"/api/v1/data/export-schedules/{saved.Id}/run", null, Ct)).Content.ReadFromJsonAsync<ExportScheduleDto>(Ct);
        ran!.LastRunAt.ShouldNotBeNull();
        ran.NextRunAt.ShouldBe(saved.NextRunAt);
        app.Mails.Sent.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_schedule_sends_nothing_once_its_owner_lost_the_permission_or_was_deactivated()
    {
        var (owner, role) = await UserAndRoleAsync(DataTransferPermissions.ExportSchedules, ODataApp.ViewGadgets);
        (await app.As(owner, _setup.TenantId).PostAsJsonAsync("/api/v1/data/export-schedules", Request(), Ct)).EnsureSuccessStatusCode();

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([ODataApp.ViewGadgets]), Ct)).EnsureSuccessStatusCode();
        (await RunDueSchedulesAsync()).LastError.ShouldBe("The owner may no longer schedule exports.");

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([DataTransferPermissions.ExportSchedules, ODataApp.ViewGadgets]), Ct))
            .EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{owner}", new UpdateUserRequest("Ex", "Owner", IsActive: false), Ct)).EnsureSuccessStatusCode();
        (await RunDueSchedulesAsync()).LastError.ShouldBe("The owner is no longer active.");

        app.Mails.Sent.ShouldBeEmpty();
        (await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().CountAsync(Ct))).ShouldBe(0);
    }

    /// <summary>Makes every schedule due, runs the job and returns the (single) schedule afterwards.</summary>
    private async Task<ExportSchedule> RunDueSchedulesAsync()
    {
        await app.InDbAsync(null, db => db.Set<ExportSchedule>().IgnoreQueryFilters().ExecuteUpdateAsync(s => s.SetProperty(x => x.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-1)), Ct));
        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExportScheduleJob>().ExecuteAsync(Ct);
        }

        return await app.InDbAsync(null, db => db.Set<ExportSchedule>().IgnoreQueryFilters().AsNoTracking().SingleAsync(Ct));
    }

    private static SaveExportScheduleRequest Request() =>
        new("Gadgets", "Tools weekly", "?$filter=Category eq 'Tools'&format=csv", Weekly, "tools", ["boss@acme.test", " "]);

    private static async Task<List<DataExportProgress>> StreamAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/v1/data/Gadgets/export/stream" + query, HttpCompletionOption.ResponseHeadersRead, Ct);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/x-ndjson");
        var text = await response.Content.ReadAsStringAsync(Ct);
        return [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonSerializer.Deserialize<DataExportProgress>(line, JsonSerializerOptions.Web)!)];
    }

    private async Task<(Guid User, Guid Role)> UserAndRoleAsync(params string[] permissions)
    {
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"u-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct);
        var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("R " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user!.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return (user.Id, role);
    }

    private async Task<Guid> UserWithAsync(params string[] permissions)
    {
        var user = await (await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest($"u-{Guid.NewGuid():N}@acme.test", "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct);
        if (permissions.Length > 0)
        {
            var role = await (await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("R " + Guid.NewGuid().ToString("N")[..6], null), Ct)).Content.ReadFromJsonAsync<Guid>(Ct);
            (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
            (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user!.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        }

        return user!.Id;
    }

    /// <summary>A browser that went away: the first line cancels the request and fails.</summary>
    private sealed class LeavingClient(CancellationTokenSource aborted) : MemoryStream
    {
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Leave();

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => new(Leave());

        private Task Leave()
        {
            aborted.Cancel();
            throw new IOException("The client left.");
        }
    }
}
