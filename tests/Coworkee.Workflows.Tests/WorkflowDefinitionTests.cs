namespace Coworkee.Workflows.Tests;

public sealed class WorkflowDefinitionTests
{
    [Fact]
    public void A_workflow_needs_an_initial_state()
    {
        Should.Throw<InvalidOperationException>(() => new WorkflowOptions().Entity<Expense>("Expenses", e => e.Transition("Submit", "Draft", "Review")))
            .Message.ShouldContain("initial state");
    }

    [Fact]
    public void Mirroring_takes_a_property_and_writes_the_state_name()
    {
        var options = new WorkflowOptions().Entity<Expense>("Expenses", e => e.Initial("Draft").MirrorTo(x => x.Status));
        var expense = new Expense { Title = "Laptop" };

        options.For("Expenses")!.Mirror!(expense, "Review");

        expense.Status.ShouldBe("Review");
        Should.Throw<ArgumentException>(() => new WorkflowOptions().Entity<Expense>("Expenses", e => e.Initial("Draft").MirrorTo(x => x.Title.ToUpperInvariant())));
    }

    [Fact]
    public void Transitions_leave_several_states_and_states_collect_their_approval_and_actions()
    {
        var options = new WorkflowOptions().Entity<Expense>("Expenses", e =>
        {
            e.Initial("Draft").Describe(x => x.Title);
            e.Transition("Reopen", ["Approved", "Rejected"], "Draft").RequiresRole("Auditor").RequiresComment();
            e.State("Review").Approval("Approved", "Rejected", a => a.Role("Manager").All().DueIn(TimeSpan.FromDays(2)).OnTimeout("Expired"));
            e.State("Review").OnEnter(_ => Task.CompletedTask);
        });
        var workflow = options.For("Expenses")!;

        var reopen = workflow.Transitions.ShouldHaveSingleItem();
        reopen.From.ShouldBe(["Approved", "Rejected"]);
        reopen.Roles.ShouldBe(["Auditor"]);
        (reopen.To, reopen.CommentRequired).ShouldBe(("Draft", true));
        var review = workflow.FindState("Review")!;
        (review.Step!.RequiresAll, review.Step.Deadline, review.Step.TimeoutState, review.OnEnterActions.Count).ShouldBe((true, TimeSpan.FromDays(2), "Expired", 1));
        workflow.Title(new Expense { Title = "Laptop" }, Guid.NewGuid()).ShouldBe("Laptop");
        options.For("Unknown").ShouldBeNull();
    }
}
