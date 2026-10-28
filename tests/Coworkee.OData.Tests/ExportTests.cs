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
        lines.Select(l => l.JobId).Distinct().ShouldHaveSingleItem().ShouldNotBeNull();
        var writing = lines.Single(l => l.Phase == DataExportProgress.Writing);
        (writing.Rows, writing.Total).ShouldBe((2, 2));
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
    public async Task Cancel_stops_a_running_export_frees_its_slot_and_mails_nothing()
    {
        SleeperGate.Closed = true;
        try
        {
            using var response = await Admin.GetAsync("/api/v1/data/Sleepers/export/stream", HttpCompletionOption.ResponseHeadersRead, Ct);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
            var first = JsonSerializer.Deserialize<DataExportProgress>((await reader.ReadLineAsync(Ct))!, JsonSerializerOptions.Web)!;
            var id = first.JobId.ShouldNotBeNull();

            // the second slot is taken too: another export is refused
            await app.InDbAsync(_setup.TenantId, db =>
            {
                db.Add(new DataTransferJob
                {
                    TenantId = _setup.TenantId, UserId = _setup.AdminUserId, Kind = DataTransferKind.Export, EntitySet = "Gadgets",
                    Status = DataTransferStatus.Running, CreatedAt = DateTimeOffset.UtcNow, StartedAt = DateTimeOffset.UtcNow,
                });
                return db.SaveChangesAsync(Ct);
            });
            (await Admin.GetAsync("/api/v1/data/Gadgets/export/stream", Ct)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

            // another user neither sees nor cancels the job
            var other = app.As(await UserWithAsync(ODataApp.ViewGadgets), _setup.TenantId);
            (await other.PostAsync($"/api/v1/data/jobs/{id}/cancel", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await other.GetAsync($"/api/v1/data/jobs/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

            var cancelled = await (await Admin.PostAsync($"/api/v1/data/jobs/{id}/cancel", null, Ct)).Content.ReadFromJsonAsync<DataTransferJobDto>(Ct);
            cancelled!.Status.ShouldBe(DataTransferStatus.Cancelled);
            var rest = await reader.ReadToEndAsync(Ct);
            JsonSerializer.Deserialize<DataExportProgress>(rest.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1], JsonSerializerOptions.Web)!.Phase.ShouldBe(DataExportProgress.Cancelled);

            // the slot is free at once, nothing is mailed and the job stays cancelled without a file
            (await StreamAsync(Admin, "?format=csv"))[^1].Phase.ShouldBe(DataExportProgress.Ready);
            app.Mails.Sent.ShouldBeEmpty();
            var job = await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().AsNoTracking().SingleAsync(j => j.Id == id, Ct));
            (job.Status, job.Result).ShouldBe((DataTransferStatus.Cancelled, (byte[]?)null));
        }
        finally
        {
            SleeperGate.Closed = false;
        }
    }

    [Fact]
    public async Task A_cancel_on_another_instance_stops_the_export_through_the_job_row()
    {
        var cancellations = app.App.Services.GetRequiredService<ExportCancellations>();
        var poll = cancellations.Poll;
        (SleeperGate.Closed, cancellations.Poll) = (true, TimeSpan.FromMilliseconds(100));
        try
        {
            using var response = await Admin.GetAsync("/api/v1/data/Sleepers/export/stream", HttpCompletionOption.ResponseHeadersRead, Ct);
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
            var id = JsonSerializer.Deserialize<DataExportProgress>((await reader.ReadLineAsync(Ct))!, JsonSerializerOptions.Web)!.JobId!.Value;

            // what the cancel endpoint of another instance writes; this instance only reads the row
            await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().Where(j => j.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, DataTransferStatus.Cancelled), Ct));

            var rest = await reader.ReadToEndAsync(Ct).WaitAsync(TimeSpan.FromSeconds(30), Ct);
            JsonSerializer.Deserialize<DataExportProgress>(rest.Split('\n', StringSplitOptions.RemoveEmptyEntries)[^1], JsonSerializerOptions.Web)!.Phase.ShouldBe(DataExportProgress.Cancelled);
            app.Mails.Sent.ShouldBeEmpty();
        }
        finally
        {
            (SleeperGate.Closed, cancellations.Poll) = (false, poll);
        }
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
    public async Task A_recurring_sweep_fails_exports_stuck_past_the_timeout_of_every_user_and_organisation()
    {
        var now = DateTimeOffset.UtcNow;
        var other = Guid.CreateVersion7();
        DataTransferJob Job(Guid tenant, DateTimeOffset createdAt, DataTransferStatus status) => new()
        {
            TenantId = tenant, UserId = Guid.CreateVersion7(), Kind = DataTransferKind.Export, EntitySet = "Gadgets", Status = status, CreatedAt = createdAt,
        };
        var stuck = new[] { Job(_setup.TenantId, now.AddHours(-2), DataTransferStatus.Running), Job(other, now.AddDays(-2), DataTransferStatus.Queued) };
        var fresh = Job(_setup.TenantId, now.AddMinutes(-1), DataTransferStatus.Running);
        var waited = Job(_setup.TenantId, now.AddHours(-3), DataTransferStatus.Queued);
        var startedLate = Job(_setup.TenantId, now.AddHours(-3), DataTransferStatus.Running);
        startedLate.StartedAt = now.AddMinutes(-1);
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(stuck[0], fresh, waited, startedLate);
            return await db.SaveChangesAsync(Ct);
        });
        await app.InDbAsync(other, async db =>
        {
            db.Add(stuck[1]);
            return await db.SaveChangesAsync(Ct);
        });

        await using (var scope = app.App.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<StaleExportJob>().ExecuteAsync(Ct);
        }

        var jobs = await app.InDbAsync(null, db => db.Set<DataTransferJob>().IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(j => j.Id, Ct));
        stuck.ShouldAllBe(j => jobs[j.Id].Status == DataTransferStatus.Failed && jobs[j.Id].Message == StaleExportJob.Message);
        jobs[fresh.Id].Status.ShouldBe(DataTransferStatus.Running);
        jobs[waited.Id].Status.ShouldBe(DataTransferStatus.Queued, "the time in the queue does not count");
        jobs[startedLate.Id].Status.ShouldBe(DataTransferStatus.Running, "staleness counts from the start");
    }

    [Fact]
    public async Task The_export_job_runs_only_queued_jobs_and_keeps_a_failure_the_sweep_wrote_meanwhile()
    {
        var failed = await QueuedJobAsync(DataTransferStatus.Failed);
        await RunExportJobAsync(failed);
        (await JobAsync(failed)).ShouldSatisfyAllConditions(j => j.Status.ShouldBe(DataTransferStatus.Failed), j => j.StartedAt.ShouldBeNull(), j => j.Result.ShouldBeNull());

        // the sweep fails it while it writes: the job keeps the failure, without file and mail
        var swept = await QueuedJobAsync(DataTransferStatus.Queued, "Sleepers");
        SleeperGate.During = () => SetStatusAsync(swept, DataTransferStatus.Failed, StaleExportJob.Message);
        await RunExportJobAsync(swept);

        var kept = await JobAsync(swept);
        (kept.Status, kept.Message, kept.Result).ShouldBe((DataTransferStatus.Failed, StaleExportJob.Message, null));
        app.Mails.Sent.ShouldBeEmpty();

        // a cancel while it writes: cancelled, without file and mail
        var cancelled = await QueuedJobAsync(DataTransferStatus.Queued, "Sleepers");
        SleeperGate.During = () => SetStatusAsync(cancelled, DataTransferStatus.Cancelled, "Cancelled");
        await RunExportJobAsync(cancelled);

        (await JobAsync(cancelled)).ShouldSatisfyAllConditions(j => j.Status.ShouldBe(DataTransferStatus.Cancelled), j => j.Result.ShouldBeNull());
        app.Mails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancel_racing_the_handoff_to_the_queue_wins_and_nothing_is_handed_over()
    {
        await SleepersAsync(8);
        SleeperGate.During = () => SetStatusAsync(null, DataTransferStatus.Cancelled, "Cancelled");

        var lines = await StreamAsync(Admin, "?format=csv", "Sleepers");

        lines[^1].Phase.ShouldBe(DataExportProgress.Cancelled);
        (await JobAsync(lines[^1].JobId!.Value)).ShouldSatisfyAllConditions(j => j.Status.ShouldBe(DataTransferStatus.Cancelled), j => j.Result.ShouldBeNull());
        app.Mails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_sweep_racing_the_handoff_to_the_queue_wins_and_nothing_is_handed_over()
    {
        await SleepersAsync(8);
        SleeperGate.During = () => SetStatusAsync(null, DataTransferStatus.Failed, StaleExportJob.Message);

        var lines = await StreamAsync(Admin, "?format=csv", "Sleepers");

        (lines[^1].Phase, lines[^1].Message).ShouldBe((DataExportProgress.Failed, StaleExportJob.Message));
        (await JobAsync(lines[^1].JobId!.Value)).ShouldSatisfyAllConditions(j => j.Status.ShouldBe(DataTransferStatus.Failed), j => j.Result.ShouldBeNull());
        app.Mails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_cancel_after_completion_keeps_the_job_completed()
    {
        var ready = (await StreamAsync(Admin, "?format=csv"))[^1];
        ready.Phase.ShouldBe(DataExportProgress.Ready);

        var answer = await (await Admin.PostAsync($"/api/v1/data/jobs/{ready.JobId}/cancel", null, Ct)).Content.ReadFromJsonAsync<DataTransferJobDto>(Ct);

        answer!.Status.ShouldBe(DataTransferStatus.Completed);
        (await JobAsync(ready.JobId!.Value)).ShouldSatisfyAllConditions(j => j.Status.ShouldBe(DataTransferStatus.Completed), j => j.Result.ShouldNotBeNull());
    }

    private Task<int> SleepersAsync(int count) => app.InDbAsync(_setup.TenantId, db =>
    {
        db.AddRange(Enumerable.Range(0, count).Select(i => new Sleeper { Name = $"S{i}", TenantId = _setup.TenantId }));
        return db.SaveChangesAsync(Ct);
    });

    // what another instance (a cancel there) or the stale sweep writes while this one exports; without an id, the running Sleepers export
    private Task<int> SetStatusAsync(Guid? id, DataTransferStatus status, string message) => app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>()
        .Where(j => id == null ? j.EntitySet == "Sleepers" && j.Status == DataTransferStatus.Running : j.Id == id)
        .ExecuteUpdateAsync(j => j.SetProperty(x => x.Status, status).SetProperty(x => x.Message, message), Ct));

    private async Task<Guid> QueuedJobAsync(DataTransferStatus status, string entitySet = "Gadgets")
    {
        var job = new DataTransferJob
        {
            TenantId = _setup.TenantId, UserId = _setup.AdminUserId, Kind = DataTransferKind.Export, EntitySet = entitySet, Status = status, Settings = "?format=csv",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.Add(job);
            return await db.SaveChangesAsync(Ct);
        });
        return job.Id;
    }

    private async Task RunExportJobAsync(Guid id)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ExportJob>().ExecuteAsync(new ExportJobArgs(id, Mail: true), Ct);
    }

    private Task<DataTransferJob> JobAsync(Guid id) => app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().AsNoTracking().SingleAsync(j => j.Id == id, Ct));

    [Fact]
    public async Task Parallel_background_exports_of_one_user_stop_at_the_limit_on_every_route()
    {
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Enumerable.Range(1, 4).Select(i => new Gadget { Name = $"Bulk {i}", Category = "Bulk", Price = i, TenantId = _setup.TenantId }));
            return await db.SaveChangesAsync(Ct);
        });
        var root = app.App.Services.GetRequiredService<InlineBackgroundJobs.Root>();
        root.Held = [];
        try
        {
            var mailed = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Admin.PostAsJsonAsync("/api/v1/data/Gadgets/export/mail", new MailExportRequest(null), Ct)));
            mailed.Count(r => r.StatusCode == HttpStatusCode.Accepted).ShouldBe(2);
            mailed.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).ShouldBe(4);

            await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().ExecuteDeleteAsync(Ct));
            var exported = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Admin.GetAsync("/api/v1/data/Gadgets/export?format=csv", Ct)));
            exported.Count(r => r.StatusCode == HttpStatusCode.Accepted).ShouldBe(2);
            exported.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).ShouldBe(3);
            (await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().CountAsync(Ct))).ShouldBe(2);
        }
        finally
        {
            root.Held = null;
        }
    }

    [Fact]
    public async Task Scheduled_and_streamed_exports_pass_the_query_guard_of_the_sets()
    {
        var planner = app.As(await UserWithAsync(DataTransferPermissions.ExportSchedules, ODataApp.ViewGadgets), _setup.TenantId);

        (await planner.PostAsJsonAsync("/api/v1/data/export-schedules", Request() with { Query = "?$filter=Category eq 'Tools'" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await planner.PostAsJsonAsync("/api/v1/data/export-schedules", Request() with { Query = "?$filter=Name eq @n&@n='Drill'" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await planner.PostAsJsonAsync("/api/v1/data/export-schedules", Request() with { Query = "?$filter=Name eq 'Drill'" }, Ct)).EnsureSuccessStatusCode();

        var failed = (await StreamAsync(Admin, "?$filter=Name eq @n&@n='Drill'"))[^1];
        failed.Phase.ShouldBe(DataExportProgress.Failed);
        failed.Message.ShouldStartWith("The query is not valid");
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
        var (owner, role) = await UserAndRoleAsync(DataTransferPermissions.ExportSchedules, ODataApp.ViewGadgets, ODataApp.SeeCategories);
        (await app.As(owner, _setup.TenantId).PostAsJsonAsync("/api/v1/data/export-schedules", Request(), Ct)).EnsureSuccessStatusCode();

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([ODataApp.ViewGadgets, ODataApp.SeeCategories]), Ct)).EnsureSuccessStatusCode();
        (await RunDueSchedulesAsync()).LastError.ShouldBe("The owner may no longer schedule exports.");

        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest([DataTransferPermissions.ExportSchedules, ODataApp.ViewGadgets, ODataApp.SeeCategories]), Ct))
            .EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{owner}", new UpdateUserRequest("Ex", "Owner", IsActive: false), Ct)).EnsureSuccessStatusCode();
        (await RunDueSchedulesAsync()).LastError.ShouldBe("The owner is no longer active.");

        app.Mails.Sent.ShouldBeEmpty();
        (await app.InDbAsync(_setup.TenantId, db => db.Set<DataTransferJob>().CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Exports_of_groups_that_are_no_longer_accessible_fail_instead_of_listing_every_group()
    {
        var gone = Guid.CreateVersion7();
        (await Admin.PostAsJsonAsync("/api/v1/data/export-schedules", Request(), Ct)).EnsureSuccessStatusCode();
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            (await db.Set<ExportSchedule>().SingleAsync(Ct)).Groups = [gone];
            return await db.SaveChangesAsync(Ct);
        });

        (await RunDueSchedulesAsync()).LastError.ShouldBe(DataExports.NoGroupsLeft);
        app.Mails.Sent.ShouldBeEmpty();

        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GadgetDbContext>();
        var job = new DataTransferJob
        {
            TenantId = _setup.TenantId, UserId = _setup.AdminUserId, Kind = DataTransferKind.Export, EntitySet = "Gadgets", Status = DataTransferStatus.Queued,
            Settings = "?format=csv", CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Add(job);
        await db.SaveChangesAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<ExportJob>().ExecuteAsync(new ExportJobArgs(job.Id, [gone], Mail: true), Ct);

        var failed = await app.InDbAsync(_setup.TenantId, d => d.Set<DataTransferJob>().AsNoTracking().SingleAsync(j => j.Id == job.Id, Ct));
        (failed.Status, failed.Message, failed.Result).ShouldBe((DataTransferStatus.Failed, DataExports.NoGroupsLeft, null));
        app.Mails.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failed_run_takes_back_only_what_it_added()
    {
        (await Admin.PostAsJsonAsync("/api/v1/data/export-schedules", Request(), Ct)).EnsureSuccessStatusCode();
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(_setup.AdminUserId, _setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GadgetDbContext>();
        var pending = new Gadget { Name = "Pending", Category = "Tools", TenantId = _setup.TenantId };
        db.Add(pending);
        var schedule = await db.Set<ExportSchedule>().SingleAsync(Ct);
        app.Mails.Fail = true;

        await scope.ServiceProvider.GetRequiredService<ExportScheduleRunner>().RunAsync(schedule, DateTimeOffset.UtcNow, advance: false, Ct);

        schedule.LastError.ShouldBe("The export failed.");
        db.Entry(pending).State.ShouldBe(EntityState.Added);
        db.ChangeTracker.Entries<DataTransferJob>().ShouldBeEmpty();
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

    private static async Task<List<DataExportProgress>> StreamAsync(HttpClient client, string query, string set = "Gadgets")
    {
        using var response = await client.GetAsync($"/api/v1/data/{set}/export/stream" + query, HttpCompletionOption.ResponseHeadersRead, Ct);
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
