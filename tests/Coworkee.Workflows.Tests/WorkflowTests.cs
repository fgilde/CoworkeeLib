using System.Net;
using System.Net.Http.Json;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Workflows;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Coworkee.Workflows.Tests;

public sealed class WorkflowTests(WorkflowApp app) : IAsyncLifetime
{
    private SetupResultDto _setup = null!;
    private Guid _expense;

    public async ValueTask InitializeAsync()
    {
        _setup = await app.SetupAsync();
        _expense = await app.AddExpenseAsync(_setup.TenantId);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Admin => app.As(_setup.AdminUserId, _setup.TenantId);

    private string Url(Guid id, string type = "Expenses") => $"/api/v1/workflows/{type}/{id}";

    [Fact]
    public async Task Transitions_offer_only_what_the_guards_allow_and_refuse_the_rest()
    {
        var viewer = await CreateUserAsync("viewer@acme.test", await CreateRoleAsync("Viewers", WorkflowPermissions.View));
        var empty = await app.AddExpenseAsync(_setup.TenantId, amount: 0);

        var status = await Admin.GetFromJsonAsync<WorkflowStatusDto>(Url(_expense), Ct);
        status!.State.ShouldBe("Draft");
        Names(status).ShouldBe(["Submit"]);
        (await app.As(viewer.Id, _setup.TenantId).GetFromJsonAsync<WorkflowStatusDto>(Url(_expense), Ct))!.Transitions.ShouldBeEmpty();
        (await app.As(viewer.Id, _setup.TenantId).PostAsync($"{Url(_expense)}/Submit", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.GetFromJsonAsync<WorkflowStatusDto>(Url(empty), Ct))!.Transitions.ShouldBeEmpty();
        (await Admin.PostAsync($"{Url(empty)}/Submit", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await Admin.PostAsync($"{Url(_expense)}/Pay", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await Admin.GetAsync(Url(_expense, "Unknown"), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Admin.GetAsync(Url(await app.AddExpenseAsync(Guid.NewGuid())), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_states_of_a_table_page_come_in_one_request_without_history_and_without_foreign_rows()
    {
        var empty = await app.AddExpenseAsync(_setup.TenantId, amount: 0);
        var foreign = await app.AddExpenseAsync(Guid.NewGuid());

        var states = await ReadAsync<List<WorkflowStatusDto>>(
            await Admin.PostAsJsonAsync("/api/v1/workflows/Expenses/states", new WorkflowStatesRequest([_expense, empty, foreign, _expense]), Ct));

        states.Select(s => s.EntityId).ShouldBe([_expense, empty], ignoreOrder: true);
        states.Single(s => s.EntityId == _expense).Transitions.Select(t => t.Name).ShouldBe(["Submit"]);
        states.ShouldAllBe(s => s.History.Count == 0);
        (await Admin.PostAsJsonAsync("/api/v1/workflows/Expenses/states", new WorkflowStatesRequest([.. Enumerable.Range(0, 201).Select(_ => Guid.NewGuid())]), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_approval_of_all_managers_moves_on_after_the_last_one_and_the_history_tells_who_did_what()
    {
        var managers = await CreateRoleAsync("Manager", WorkflowPermissions.View);
        var (ann, bob) = (await CreateUserAsync("ann@acme.test", managers), await CreateUserAsync("bob@acme.test", managers));

        var submitted = await ReadAsync<WorkflowStatusDto>(await Admin.PostAsJsonAsync($"{Url(_expense)}/Submit", new TransitionRequest("Please"), Ct));

        submitted.State.ShouldBe("Review");
        submitted.Approval!.Approvers.Select(a => a.UserId).ShouldBe([ann.Id, bob.Id], ignoreOrder: true);
        (submitted.Approval.Title, submitted.Approval.CanDecide, submitted.Approval.DueAt).ShouldBe(("Laptop", false, WorkflowApp.Clock.GetUtcNow().AddDays(2)));
        (await app.NotificationsAsync()).Where(n => n.Title == "Approval requested").Select(n => n.UserId).ShouldBe([ann.Id, bob.Id], ignoreOrder: true);
        var inbox = (await app.As(ann.Id, _setup.TenantId).GetFromJsonAsync<List<ApprovalDto>>("/api/v1/workflows/approvals", Ct))!.ShouldHaveSingleItem();
        (inbox.EntityId, inbox.CanDecide, inbox.Link).ShouldBe((_expense, true, $"/expenses/{_expense}"));

        (await Decide(ann.Id, inbox.Id, true, "Fine")).State.ShouldBe("Review");
        (await app.As(ann.Id, _setup.TenantId).PostAsJsonAsync($"/api/v1/workflows/approvals/{inbox.Id}", new DecisionRequest(true), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var approved = await Decide(bob.Id, inbox.Id, true, null);

        approved.State.ShouldBe("Approved");
        approved.Approval.ShouldBeNull();
        approved.History.Select(s => (s.Kind, s.From, s.To, s.UserId, s.Comment)).ShouldBe(
        [
            (WorkflowStepKind.Transition, "Draft", "Review", _setup.AdminUserId, "Please"),
            (WorkflowStepKind.Approved, "Review", null, ann.Id, "Fine"),
            (WorkflowStepKind.Approved, "Review", null, bob.Id, null),
            (WorkflowStepKind.Transition, "Review", "Approved", bob.Id, null),
        ]);
        (await app.ExpenseAsync(_expense)).Status.ShouldBe("Approved");
        (await app.As(ann.Id, _setup.TenantId).GetFromJsonAsync<List<ApprovalDto>>("/api/v1/workflows/approvals", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rows_the_user_cannot_see_have_no_workflow_for_him_but_their_approvers_see_and_decide()
    {
        var viewer = await CreateUserAsync("viewer@acme.test", await CreateRoleAsync("Viewers", WorkflowPermissions.View, WorkflowPermissions.Transition));
        var ann = await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View, WorkflowPermissions.Transition));
        var secret = await app.AddExpenseAsync(_setup.TenantId, title: "Secret bonus");
        var (asViewer, asAnn) = (app.As(viewer.Id, _setup.TenantId), app.As(ann.Id, _setup.TenantId));

        (await asViewer.GetAsync(Url(secret), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asViewer.PostAsync($"{Url(secret)}/Submit", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync<List<WorkflowStatusDto>>(await asViewer.PostAsJsonAsync("/api/v1/workflows/Expenses/states", new WorkflowStatesRequest([secret, _expense]), Ct)))
            .Select(s => s.EntityId).ShouldBe([_expense]);
        (await asAnn.GetAsync(Url(secret), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var approval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsJsonAsync($"{Url(secret)}/Submit", new TransitionRequest("Q3"), Ct))).Approval!;

        (await asViewer.GetAsync(Url(secret), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asAnn.GetFromJsonAsync<WorkflowStatusDto>(Url(secret), Ct))!.History.ShouldContain(s => s.Comment == "Q3");
        (await asAnn.PostAsync($"{Url(secret)}/Withdraw", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await asAnn.GetFromJsonAsync<List<ApprovalDto>>("/api/v1/workflows/approvals", Ct))!.ShouldHaveSingleItem().EntityId.ShouldBe(secret);
        (await Decide(ann.Id, approval.Id, true, null)).State.ShouldBe("Approved");
        (await asAnn.GetAsync(Url(secret), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task One_rejection_decides_and_reopening_needs_the_role_and_a_comment()
    {
        var ann = await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View));
        var approval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(_expense)}/Submit", null, Ct))).Approval!;

        (await Decide(ann.Id, approval.Id, false, "Too expensive")).State.ShouldBe("Rejected");

        (await Admin.GetFromJsonAsync<WorkflowStatusDto>(Url(_expense), Ct))!.Transitions.ShouldBeEmpty();
        await AddRoleAsync(_setup.AdminUserId, await CreateRoleAsync("Auditor"));
        (await Admin.PostAsync($"{Url(_expense)}/Reopen", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsJsonAsync($"{Url(_expense)}/Reopen", new TransitionRequest("Cheaper now"), Ct))).State.ShouldBe("Draft");
    }

    [Fact]
    public async Task Withdrawing_cancels_the_open_approval()
    {
        var ann = await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View));
        (await Admin.PostAsync($"{Url(_expense)}/Submit", null, Ct)).EnsureSuccessStatusCode();

        var withdrawn = await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(_expense)}/Withdraw", null, Ct));

        (withdrawn.State, withdrawn.Approval).ShouldBe(("Draft", null));
        (await app.As(ann.Id, _setup.TenantId).GetFromJsonAsync<List<ApprovalDto>>("/api/v1/workflows/approvals", Ct))!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reminders_come_once_and_at_the_deadline_the_escalation_role_joins_and_decides_alone()
    {
        var ann = await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View));
        var auditor = await CreateUserAsync("audit@acme.test", await CreateRoleAsync("Auditor", WorkflowPermissions.View));
        var approval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(_expense)}/Submit", null, Ct))).Approval!;

        await app.RunDeadlineJobAsync();
        (await app.NotificationsAsync()).ShouldNotContain(n => n.Title == "Reminder: approval needed");

        WorkflowApp.Clock.Advance(TimeSpan.FromDays(1.5));
        await app.RunDeadlineJobAsync();
        await app.RunDeadlineJobAsync();
        (await app.NotificationsAsync()).Where(n => n.Title == "Reminder: approval needed").Select(n => n.UserId).ShouldBe([ann.Id]);

        WorkflowApp.Clock.Advance(TimeSpan.FromDays(1));
        await app.RunDeadlineJobAsync();
        (await app.NotificationsAsync()).Where(n => n.Title == "Approval overdue").Select(n => n.UserId).ShouldBe([ann.Id, auditor.Id], ignoreOrder: true);

        var approved = await Decide(auditor.Id, approval.Id, true, "Overdue, fine");
        approved.State.ShouldBe("Approved");
        approved.History.Select(s => s.Kind).ShouldBe(
            [WorkflowStepKind.Transition, WorkflowStepKind.Reminded, WorkflowStepKind.Escalated, WorkflowStepKind.Approved, WorkflowStepKind.Transition]);
    }

    [Fact]
    public async Task A_deadline_with_a_timeout_state_expires_the_approval_and_four_eyes_keep_the_requester_out()
    {
        var managers = await CreateRoleAsync("Manager", WorkflowPermissions.View, WorkflowPermissions.Transition);
        var ann = await CreateUserAsync("ann@acme.test", managers);
        await CreateUserAsync("bob@acme.test", managers);

        var approval = (await ReadAsync<WorkflowStatusDto>(await app.As(ann.Id, _setup.TenantId).PostAsync($"{Url(_expense, "Trips")}/Submit", null, Ct))).Approval!;
        approval.Approvers.ShouldNotContain(a => a.UserId == ann.Id);

        WorkflowApp.Clock.Advance(TimeSpan.FromHours(5));
        await app.RunDeadlineJobAsync();

        var status = (await Admin.GetFromJsonAsync<WorkflowStatusDto>(Url(_expense, "Trips"), Ct))!;
        status.State.ShouldBe("Expired");
        status.History.Select(s => (s.Kind, s.To)).ShouldBe([(WorkflowStepKind.Transition, "Review"), (WorkflowStepKind.Expired, null), (WorkflowStepKind.Transition, "Expired")]);
    }

    [Fact]
    public async Task The_escalation_keeps_the_requester_out_too()
    {
        var managers = await CreateRoleAsync("Manager", WorkflowPermissions.View, WorkflowPermissions.Transition);
        var ann = await CreateUserAsync("ann@acme.test", managers);
        var bob = await CreateUserAsync("bob@acme.test", managers);
        (await app.As(ann.Id, _setup.TenantId).PostAsync($"{Url(_expense, "Claims")}/Submit", null, Ct)).EnsureSuccessStatusCode();

        WorkflowApp.Clock.Advance(TimeSpan.FromHours(5));
        await app.RunDeadlineJobAsync();

        var status = (await Admin.GetFromJsonAsync<WorkflowStatusDto>(Url(_expense, "Claims"), Ct))!;
        status.History.ShouldContain(s => s.Kind == WorkflowStepKind.Escalated);
        status.Approval!.Approvers.Select(a => a.UserId).ShouldBe([bob.Id]);
    }

    [Fact]
    public async Task Titles_can_use_navigations_loaded_with_the_entity()
    {
        await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View));
        var purchase = await app.AddExpenseAsync(_setup.TenantId, category: "IT");

        var approval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(purchase, "Purchases")}/Submit", null, Ct))).Approval!;

        approval.Title.ShouldBe("Laptop for IT");
    }

    [Fact]
    public async Task Approvers_can_be_found_from_the_entity_when_the_approval_starts()
    {
        var ann = await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View));
        var leave = await app.AddExpenseAsync(_setup.TenantId, title: ann.Id.ToString());

        var approval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(leave, "Leaves")}/Submit", null, Ct))).Approval!;

        approval.Approvers.Select(a => a.UserId).ShouldBe([ann.Id]);
        (await Decide(ann.Id, approval.Id, true, null)).State.ShouldBe("Approved");
    }

    [Fact]
    public async Task One_submit_takes_large_amounts_through_a_second_approval_chosen_from_the_entity()
    {
        var ann = await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View));
        var fay = await CreateUserAsync("fay@acme.test", await CreateRoleAsync("Finance", WorkflowPermissions.View));
        var (small, large) = (await app.AddExpenseAsync(_setup.TenantId, amount: 500), await app.AddExpenseAsync(_setup.TenantId, amount: 5000));

        var smallApproval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(small, "Invoices")}/Submit", null, Ct))).Approval!;
        var largeApproval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(large, "Invoices")}/Submit", null, Ct))).Approval!;

        (await Decide(ann.Id, smallApproval.Id, true, null)).State.ShouldBe("Approved");
        var review = await Decide(ann.Id, largeApproval.Id, true, "Over budget line");
        review.State.ShouldBe("Finance review");
        review.Approval!.Approvers.Select(a => a.UserId).ShouldBe([fay.Id]);
        var inbox = (await app.As(fay.Id, _setup.TenantId).GetFromJsonAsync<List<ApprovalDto>>("/api/v1/workflows/approvals", Ct))!.ShouldHaveSingleItem();
        inbox.EntityId.ShouldBe(large);
        var approved = await Decide(fay.Id, inbox.Id, true, null);
        approved.State.ShouldBe("Approved");
        approved.History.Where(s => s.Kind == WorkflowStepKind.Transition).Select(s => (s.From, s.To)).ShouldBe([("Draft", "Review"), ("Review", "Finance review"), ("Finance review", "Approved")]);
    }

    [Fact]
    public async Task Actions_set_fields_and_transitions_reach_subscribers_live()
    {
        var payer = await CreateRoleAsync("Payer", WorkflowApp.PayPermission);
        await AddRoleAsync(_setup.AdminUserId, payer);
        var ann = await CreateUserAsync("ann@acme.test", await CreateRoleAsync("Manager", WorkflowPermissions.View));
        var (connection, events) = await app.ConnectAsync(_setup.AdminUserId, _setup.TenantId);
        await using var _ = connection;
        await connection.InvokeAsync(RealtimeHubMethods.Subscribe, WorkflowTopics.Entity("Expenses", _expense), Ct);

        var approval = (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(_expense)}/Submit", null, Ct))).Approval!;
        await Decide(ann.Id, approval.Id, true, null);
        (await ReadAsync<WorkflowStatusDto>(await Admin.PostAsync($"{Url(_expense)}/Pay", null, Ct))).State.ShouldBe("Paid");

        (await app.ExpenseAsync(_expense)).Paid.ShouldBeTrue();
        for (var i = 0; i < 50 && events.IsEmpty; i++)
        {
            await Task.Delay(100, Ct);
        }

        events.ShouldContain(e => e.Topic == WorkflowTopics.Entity("Expenses", _expense));
        await Should.ThrowAsync<HubException>(() => connection.InvokeAsync(RealtimeHubMethods.Subscribe, WorkflowTopics.Entity("Unknown", _expense), Ct));
    }

    // the clock stands still otherwise: history steps of separate requests would share one time and come back in any order (SQL Server)
    private async Task<WorkflowStatusDto> Decide(Guid userId, Guid approvalId, bool approve, string? comment)
    {
        WorkflowApp.Clock.Advance(TimeSpan.FromSeconds(1));
        return await ReadAsync<WorkflowStatusDto>(await app.As(userId, _setup.TenantId).PostAsJsonAsync($"/api/v1/workflows/approvals/{approvalId}", new DecisionRequest(approve, comment), Ct));
    }

    private static List<string> Names(WorkflowStatusDto status) => status.Transitions.Select(t => t.Name).ToList();

    private async Task<Guid> CreateRoleAsync(string name, params string[] permissions)
    {
        var role = await ReadAsync<Guid>(await Admin.PostAsJsonAsync("/api/v1/identity/roles", new RoleRequest(name, null), Ct));
        (await Admin.PutAsJsonAsync($"/api/v1/identity/permissions/grants/Role/{role}", new NameListRequest(permissions), Ct)).EnsureSuccessStatusCode();
        return role;
    }

    private async Task<UserDto> CreateUserAsync(string email, Guid role)
    {
        var user = await ReadAsync<UserDto>(await Admin.PostAsJsonAsync("/api/v1/identity/users", new CreateUserRequest(email, "Passw0rd!x", null, null), Ct));
        await AddRoleAsync(user.Id, role);
        return user;
    }

    private async Task AddRoleAsync(Guid userId, Guid role)
    {
        var current = await Admin.GetFromJsonAsync<UserDetailDto>($"/api/v1/identity/users/{userId}", Ct);
        (await Admin.PutAsJsonAsync($"/api/v1/identity/users/{userId}/roles", new IdListRequest([.. RoleIds(current!), role]), Ct)).EnsureSuccessStatusCode();
    }

    private static IEnumerable<Guid> RoleIds(UserDetailDto user) => user.Roles.Select(r => r.Id);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
