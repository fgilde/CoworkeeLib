using System.Security.Claims;
using Bunit;
using Coworkee.Client.Blazor.Components;
using Coworkee.Client.Blazor.Pages;
using Coworkee.Client.Blazor.Workflows;
using Coworkee.Contracts.Realtime;
using Coworkee.Contracts.Workflows;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using NSubstitute;

namespace Coworkee.Client.Blazor.Tests;

public sealed class WorkflowComponentsTests : ClientTestBase
{
    private static readonly Guid Me = Guid.CreateVersion7();
    private static readonly Guid Bob = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
    private readonly IWorkflowApi _api = Substitute.For<IWorkflowApi>();
    private readonly Guid _id = Guid.CreateVersion7();

    public WorkflowComponentsTests()
    {
        Services.AddSingleton(_api);
        Services.AddScoped<WorkflowCommands>();
        AddAuthorization().SetAuthorized("Ada").SetClaims(new Claim("sub", Me.ToString()));
        Render<MudPopoverProvider>();
        Render<MudSnackbarProvider>();
    }

    [Fact]
    public void The_timeline_shows_the_newest_step_first_with_who_and_why()
    {
        var timeline = Render<CoworkeeWorkflowTimeline>(p => p.Add(t => t.Steps,
        [
            new WorkflowStepDto(WorkflowStepKind.Transition, "Draft", "Review", "Submit", Me, "Please", Start),
            new WorkflowStepDto(WorkflowStepKind.Reminded, "Review", null, null, null, null, Start.AddDays(1)),
            new WorkflowStepDto(WorkflowStepKind.Rejected, "Review", null, null, Bob, "Too expensive", Start.AddDays(2)),
        ]));

        timeline.FindAll("[data-step]").Select(s => s.GetAttribute("data-step")).ShouldBe(["Rejected", "Reminded", "Transition"]);
        timeline.FindAll("[data-step]")[2].TextContent.ShouldContain("Submit: Draft → Review");
        timeline.FindAll("[data-step]")[1].TextContent.ShouldContain("System");
        timeline.FindAll("[data-testid='workflow-step-comment']").Select(c => c.TextContent).ShouldBe(["Too expensive", "Please"]);
    }

    [Fact]
    public async Task The_detail_panel_runs_allowed_transitions_and_asks_for_a_required_comment()
    {
        _api.GetAsync("Products", _id, default).ReturnsForAnyArgs(Status("Approved", [new("Archive", "Archived"), new("Reopen", "Draft", CommentRequired: true)]));
        _api.RunAsync("Products", _id, "Reopen", "Fix the price", Arg.Any<CancellationToken>()).Returns(Status("Draft", [new("Archive", "Archived")]));
        _api.RunAsync("Products", _id, "Archive", null, Arg.Any<CancellationToken>()).Returns(Status("Archived", []));
        var changed = new List<WorkflowStatusDto>();
        var panel = Render<CoworkeeWorkflow>(p => p.Add(w => w.EntityType, "Products").Add(w => w.EntityId, _id).Add(w => w.OnChanged, changed.Add));
        panel.WaitForAssertion(() => panel.Find("[data-testid='workflow-state']").TextContent.ShouldContain("Approved"));
        var dialogs = Render<MudDialogProvider>();

        await panel.InvokeAsync(() => panel.Find("[data-transition='Reopen']").Click());
        dialogs.WaitForAssertion(() => dialogs.Find("[data-testid='workflow-comment-ok']").HasAttribute("disabled").ShouldBeTrue());
        await _api.DidNotReceiveWithAnyArgs().RunAsync(default!, default, default!, default, default);

        await dialogs.InvokeAsync(() => dialogs.Find("textarea[data-testid='workflow-comment'], [data-testid='workflow-comment'] textarea").Input("Fix the price"));
        await dialogs.InvokeAsync(() => dialogs.Find("[data-testid='workflow-comment-ok']").Click());
        await _api.Received(1).RunAsync("Products", _id, "Reopen", "Fix the price", Arg.Any<CancellationToken>());
        panel.WaitForAssertion(() => panel.Find("[data-testid='workflow-state']").TextContent.ShouldContain("Draft"));

        await panel.InvokeAsync(() => panel.Find("[data-transition='Archive']").Click());
        panel.WaitForAssertion(() => panel.Find("[data-testid='workflow-state']").TextContent.ShouldContain("Archived"));
        changed.Last().State.ShouldBe("Archived");
    }

    [Fact]
    public async Task The_inbox_lists_open_approvals_decides_and_follows_changes_live()
    {
        var approval = Approval(canDecide: true);
        _api.GetMyApprovalsAsync(default).ReturnsForAnyArgs([approval]);
        _api.DecideAsync(approval.Id, true, null, default).ReturnsForAnyArgs(Status("Approved", []));
        var inbox = Render<Approvals>();
        inbox.WaitForAssertion(() => inbox.Find($"[data-approval='{approval.Id}']").TextContent.ShouldContain("Laptop"));
        inbox.Find($"[data-approval='{approval.Id}']").TextContent.ShouldContain("1 of 2 approved");

        _api.GetMyApprovalsAsync(default).ReturnsForAnyArgs([]);
        await inbox.InvokeAsync(() => inbox.Find("[data-testid='workflow-approve']").Click());

        await _api.Received(1).DecideAsync(approval.Id, true, null, Arg.Any<CancellationToken>());
        inbox.WaitForAssertion(() => inbox.Find("[data-testid='approvals-empty']"));

        _api.GetMyApprovalsAsync(default).ReturnsForAnyArgs([approval]);
        Services.GetRequiredService<FakeRealtimeConnection>().Push(RealtimeTopics.User(Me), RealtimeEventTypes.EntityChanged,
            new EntityChangedPayload("WorkflowApprover", Guid.NewGuid().ToString(), EntityChangedPayload.Created, []));
        inbox.WaitForAssertion(() => inbox.Find($"[data-approval='{approval.Id}']"));
    }

    [Fact]
    public void A_row_shows_the_state_and_a_menu_only_when_there_is_something_to_do()
    {
        var other = Guid.CreateVersion7();
        _api.GetAsync("Products", _id, default).Returns(Status("Review", [], Approval(canDecide: true)));
        _api.GetAsync("Products", other, default).Returns(Status("Archived", []));

        var row = Render<CoworkeeWorkflowActions>(p => p.Add(a => a.EntityType, "Products").Add(a => a.EntityId, _id));
        var done = Render<CoworkeeWorkflowActions>(p => p.Add(a => a.EntityType, "Products").Add(a => a.EntityId, other));

        row.WaitForAssertion(() => row.Find("[data-testid='workflow-state']").TextContent.ShouldContain("Review"));
        row.Find("[data-testid='workflow-menu']").ShouldNotBeNull();
        done.WaitForAssertion(() => done.Find("[data-testid='workflow-state']").TextContent.ShouldContain("Archived"));
        done.FindAll("[data-testid='workflow-menu']").ShouldBeEmpty();
    }

    private WorkflowStatusDto Status(string state, IReadOnlyList<WorkflowTransitionDto> transitions, ApprovalDto? approval = null) =>
        new("Products", _id, state, transitions, approval, []);

    private ApprovalDto Approval(bool canDecide) => new(
        Guid.CreateVersion7(), "Products", _id, "Review", "Laptop", RequiresAll: true, ApprovalStatus.Pending, Start, Start.AddDays(2),
        [new ApproverDto(Bob, true, null, Start, false), new ApproverDto(Me, null, null, null, false)], canDecide, "/products/1");
}
