using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Contracts.Data;
using Coworkee.Contracts.Identity;
using Coworkee.OData.Transfer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.OData.Tests;

public sealed class DataTransferPersonalDataTests(ODataApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _bob;
    private Guid _carl;
    private readonly Guid _otherTenant = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _bob = await CreateAsync("bob@acme.test");
        _carl = await CreateAsync("carl@acme.test");
        await app.InDbAsync(_setup.TenantId, async db =>
        {
            db.AddRange(Job(_bob, "bob.xlsx"), Job(_carl, "carl.xlsx"));
            db.AddRange(
                Schedule(_setup.TenantId, _bob, "Bob's weekly", []),
                Schedule(_setup.TenantId, _carl, "Carl's weekly", ["BOB@acme.test", "x@acme.test"]));
            return await db.SaveChangesAsync();
        });
        await app.InDbAsync(_otherTenant, async db =>
        {
            db.Add(Schedule(_otherTenant, Guid.CreateVersion7(), "Elsewhere", ["bob@acme.test"]));
            return await db.SaveChangesAsync();
        });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_export_lists_his_jobs_with_their_files_his_schedules_and_the_schedules_mailing_him()
    {
        var section = (await app.As(_bob, _setup.TenantId).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct)).GetProperty("dataTransfers");

        var job = section.GetProperty("jobs").EnumerateArray().Single();
        (job.GetProperty("resultFileName").GetString(), job.GetProperty("resultSize").GetInt32()).ShouldBe(("bob.xlsx", 3));
        section.GetProperty("schedules").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Bob's weekly");
        section.GetProperty("mailedBy").EnumerateArray().Single().GetProperty("name").GetString().ShouldBe("Carl's weekly");
    }

    [Fact]
    public async Task Erasing_deletes_his_jobs_and_schedules_and_takes_his_address_out_of_the_others_in_his_organisation()
    {
        (await app.As(_setup.AdminUserId, _setup.TenantId).DeleteAsync($"/api/v1/identity/users/{_bob}", Ct)).EnsureSuccessStatusCode();

        (await app.InDbAsync(null, db => db.Set<DataTransferJob>().IgnoreQueryFilters().Select(j => j.UserId).ToListAsync(Ct))).ShouldBe([_carl]);
        var schedules = await app.InDbAsync(null, db => db.Set<ExportSchedule>().IgnoreQueryFilters().ToDictionaryAsync(s => s.Name, Ct));
        schedules.ShouldNotContainKey("Bob's weekly");
        schedules["Carl's weekly"].Recipients.ShouldBe(["x@acme.test"]);
        schedules["Elsewhere"].Recipients.ShouldBe(["bob@acme.test"]);
    }

    [Fact]
    public async Task A_running_export_stops_once_its_job_is_gone()
    {
        var cancellations = new ExportCancellations(app.App.Services.GetRequiredService<IServiceScopeFactory>()) { Poll = TimeSpan.FromMilliseconds(50) };

        using var watch = cancellations.Start(Guid.CreateVersion7(), Ct);

        await Task.Delay(TimeSpan.FromSeconds(5), watch.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        watch.Token.IsCancellationRequested.ShouldBeTrue();
    }

    private DataTransferJob Job(Guid userId, string file) => new()
    {
        TenantId = _setup.TenantId, UserId = userId, Kind = DataTransferKind.Export, EntitySet = "Gadgets", Status = DataTransferStatus.Completed,
        Result = [1, 2, 3], ResultFileName = file, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ExportSchedule Schedule(Guid tenantId, Guid userId, string name, List<string> recipients) => new()
    {
        TenantId = tenantId, UserId = userId, EntitySet = "Gadgets", Name = name, Recipients = recipients, TimeZone = "UTC", NextRunAt = DateTimeOffset.UtcNow.AddDays(1),
    };

    private async Task<Guid> CreateAsync(string email) =>
        (await (await app.As(_setup.AdminUserId, _setup.TenantId).PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct))
            .Content.ReadFromJsonAsync<UserDto>(Ct))!.Id;
}
