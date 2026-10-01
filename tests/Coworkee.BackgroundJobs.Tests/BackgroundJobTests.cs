using System.Net;
using Coworkee.Core.Security;
using Hangfire;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.BackgroundJobs.Tests;

public sealed class BackgroundJobTests(JobsApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Enqueued_job_runs_with_captured_actor()
    {
        var (user, tenant, marker) = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.NewGuid().ToString());

        EnqueueAs<RecordingJob>(user, tenant, marker);

        await Eventually(() => RecordingJob.Calls.Any(c => c.Value == marker));
        RecordingJob.Calls.Single(c => c.Value == marker).ShouldBe((marker, user, tenant));
    }

    [Fact]
    public async Task Job_runs_in_requested_queue()
    {
        var marker = Guid.NewGuid().ToString();

        var id = EnqueueAs<RecordingJob>(Guid.CreateVersion7(), Guid.CreateVersion7(), marker, "mail");

        await Eventually(() => RecordingJob.Calls.Any(c => c.Value == marker));
        var details = app.App.Services.GetRequiredService<JobStorage>().GetMonitoringApi().JobDetails(id);
        details.Job.Queue.ShouldBe("mail");
    }

    [Fact]
    public async Task Failing_job_is_retried()
    {
        var marker = Guid.NewGuid().ToString();

        EnqueueAs<FailingOnceJob>(null, null, marker);

        await Eventually(() => FailingOnceJob.Attempts.GetValueOrDefault(marker) >= 2, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task Dashboard_requires_jobs_view()
    {
        var setup = await app.SetupAsync();

        (await app.App.GetTestClient().GetAsync("/admin/jobs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await app.As(Guid.CreateVersion7(), setup.TenantId).GetAsync("/admin/jobs", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await app.As(setup.AdminUserId, setup.TenantId).GetAsync("/admin/jobs", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private string EnqueueAs<TJob>(Guid? user, Guid? tenant, string args, string queue = "default")
        where TJob : IBackgroundJob<string>
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(user, tenant));
        using var scope = app.App.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IBackgroundJobs>().Enqueue<TJob, string>(args, queue);
    }

    private static async Task Eventually(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!condition())
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "condition not met in time");
            await Task.Delay(200, Ct);
        }
    }
}
