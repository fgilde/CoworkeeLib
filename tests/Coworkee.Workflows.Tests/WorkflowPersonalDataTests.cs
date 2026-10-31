using System.Net.Http.Json;
using System.Text.Json;
using Coworkee.Application.Privacy;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Tests;

public sealed class WorkflowPersonalDataTests(WorkflowApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _ann;
    private Guid _bob;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        var managers = await ReadAsync<Guid>(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest("Manager", null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{managers}", new NameListRequest([WorkflowPermissions.View]), Ct)).EnsureSuccessStatusCode();
        _ann = await CreateUserAsync("ann@acme.test", managers);
        _bob = await CreateUserAsync("bob@acme.test", managers);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    [Fact]
    public async Task The_export_holds_his_decisions_with_comments_and_his_steps()
    {
        var approval = await SubmitAsync("Expenses");
        await DecideAsync(_bob, approval, "Too pricey");

        var section = (await app.As(_bob, _setup.TenantId).GetFromJsonAsync<JsonElement>("/api/v1/identity/me/personal-data", Ct)).GetProperty("workflows");

        var decision = section.GetProperty("decisions").EnumerateArray().Single();
        (decision.GetProperty("title").GetString(), decision.GetProperty("approved").GetBoolean(), decision.GetProperty("comment").GetString()).ShouldBe(("Laptop", true, "Too pricey"));
        section.GetProperty("steps").EnumerateArray().Single().GetProperty("comment").GetString().ShouldBe("Too pricey");
    }

    [Fact]
    public async Task Erasing_keeps_his_decision_and_history_without_his_id_and_words_and_leaves_the_others_waiting()
    {
        var approval = await SubmitAsync("Expenses");
        await DecideAsync(_bob, approval, "Too pricey");
        var due = (await ApprovalAsync(approval)).DueAt;

        await EraseBobAsync();

        var after = await ApprovalAsync(approval);
        (after.Status, after.DueAt).ShouldBe((ApprovalStatus.Pending, due));
        after.Approvers.Select(a => (a.UserId, a.Approved, a.Comment)).ShouldBe([(_ann, null, null), (PersonalDataSubject.ErasedUserId, true, null)], ignoreOrder: true);
        var steps = await InDbAsync(db => db.Set<WorkflowStep>().IgnoreQueryFilters().Where(s => s.Kind == WorkflowStepKind.Approved).ToListAsync(Ct));
        steps.Select(s => (s.UserId, s.Comment)).ShouldBe([(PersonalDataSubject.ErasedUserId, null)]);
    }

    [Fact]
    public async Task An_approval_that_waited_for_him_alone_is_overdue_at_once_and_follows_its_timeout()
    {
        await RemoveAnnFromManagersAsync();
        var approval = await SubmitAsync("Trips");
        (await ApprovalAsync(approval)).Approvers.Select(a => a.UserId).ShouldBe([_bob]);

        await EraseBobAsync();
        await app.RunDeadlineJobAsync();

        var after = await ApprovalAsync(approval);
        (after.Status, after.Approvers.Count).ShouldBe((ApprovalStatus.Expired, 0));
        (await Admin.GetFromJsonAsync<WorkflowStatusDto>($"/api/v1/workflows/Trips/{after.EntityId}", Ct))!.State.ShouldBe("Expired");
    }

    [Fact]
    public async Task An_escalated_approval_that_waited_for_him_alone_is_cancelled()
    {
        await RemoveAnnFromManagersAsync();
        var approval = await SubmitAsync("Claims");
        WorkflowApp.Clock.Advance(TimeSpan.FromHours(5));
        await app.RunDeadlineJobAsync();
        (await ApprovalAsync(approval)).EscalatedAt.ShouldNotBeNull();

        await EraseBobAsync();

        (await ApprovalAsync(approval)).Status.ShouldBe(ApprovalStatus.Canceled);
    }

    private async Task RemoveAnnFromManagersAsync() =>
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{_ann}/roles", new IdListRequest([]), Ct)).EnsureSuccessStatusCode();

    private async Task<Guid> SubmitAsync(string set)
    {
        var expense = await app.AddExpenseAsync(_setup.TenantId);
        WorkflowApp.Clock.Advance(TimeSpan.FromSeconds(1));
        var status = await ReadAsync<WorkflowStatusDto>(await Admin.PostAsJsonAsync($"/api/v1/workflows/{set}/{expense}/Submit", new TransitionRequest(null), Ct));
        return status.Approval!.Id;
    }

    private async Task DecideAsync(Guid userId, Guid approvalId, string comment)
    {
        WorkflowApp.Clock.Advance(TimeSpan.FromSeconds(1));
        (await app.As(userId, _setup.TenantId).PostAsJsonAsync($"/api/v1/workflows/approvals/{approvalId}", new DecisionRequest(true, comment), Ct)).EnsureSuccessStatusCode();
    }

    private async Task EraseBobAsync() => (await Admin.DeleteAsync($"/api/v1/identity/users/{_bob}", Ct)).EnsureSuccessStatusCode();

    private Task<WorkflowApproval> ApprovalAsync(Guid id) =>
        InDbAsync(db => db.Set<WorkflowApproval>().IgnoreQueryFilters().AsNoTracking().Include(a => a.Approvers).SingleAsync(a => a.Id == id, Ct));

    private async Task<T> InDbAsync<T>(Func<WorkflowTestDbContext, Task<T>> read)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<WorkflowTestDbContext>());
    }

    private async Task<Guid> CreateUserAsync(string email, Guid role)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{user.Id}/roles", new IdListRequest([role]), Ct)).EnsureSuccessStatusCode();
        return user.Id;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
