using System.Net.Http.Json;
using Coworkee.Application.Messaging;
using Coworkee.Contracts.Identity;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Results;
using Coworkee.Core.Security;
using Coworkee.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Tests;

public sealed record AddSubmittedExpense(string Title, decimal Amount, string EntityType = "Expenses", Guid? CategoryId = null) : ICommand<Result<Guid>>;

/// <summary>Adds an expense and submits it in the same unit of work: nothing is saved before the dispatcher saves once.</summary>
internal sealed class AddSubmittedExpenseHandler(CoworkeeDbContext db, WorkflowEngine workflows, ICurrentUser currentUser) : IHandler<AddSubmittedExpense, Result<Guid>>
{
    public async Task<Result<Guid>> HandleAsync(AddSubmittedExpense command, CancellationToken cancellationToken)
    {
        var expense = new Expense { Title = command.Title, Amount = command.Amount, CategoryId = command.CategoryId, TenantId = currentUser.TenantId!.Value };
        db.Add(expense);
        var subject = await workflows.LoadAsync(command.EntityType, expense.Id, cancellationToken);
        if (!subject.IsSuccess)
        {
            return subject.Error!;
        }

        var submitted = await workflows.RunAsync(subject.Value, "Submit", null, cancellationToken);
        return submitted.IsSuccess ? expense.Id : submitted.Error!;
    }
}

public sealed class WorkflowSameUnitOfWorkTests(WorkflowApp app)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_adds_an_entity_and_runs_its_first_transition_in_one_unit_of_work()
    {
        var setup = await app.SetupAsync();

        var id = await SendAsync(setup, new AddSubmittedExpense("Monitor", 250));

        var status = await app.As(setup.AdminUserId, setup.TenantId).GetFromJsonAsync<WorkflowStatusDto>($"/api/v1/workflows/Expenses/{id}", Ct);
        status!.State.ShouldBe("Review");
        status.Approval.ShouldNotBeNull();
        status.History.ShouldNotBeEmpty();
        (await app.ExpenseAsync(id)).Status.ShouldBe("Review");
    }

    [Fact]
    public async Task Included_references_of_an_added_entity_are_loaded_from_its_foreign_key()
    {
        var setup = await app.SetupAsync();
        var category = (await app.ExpenseAsync(await app.AddExpenseAsync(setup.TenantId, category: "IT"))).CategoryId;

        var id = await SendAsync(setup, new AddSubmittedExpense("Monitor", 250, "Purchases", category));

        var status = await app.As(setup.AdminUserId, setup.TenantId).GetFromJsonAsync<WorkflowStatusDto>($"/api/v1/workflows/Purchases/{id}", Ct);
        status!.Approval!.Title.ShouldBe("Monitor for IT");
    }

    [Fact]
    public async Task A_refused_first_transition_saves_neither_the_entity_nor_its_workflow()
    {
        var setup = await app.SetupAsync();

        var result = await SendResultAsync(setup, new AddSubmittedExpense("Nothing", 0));

        result.IsSuccess.ShouldBeFalse();
        (await app.ExpensesAsync()).ShouldBeEmpty();
    }

    private async Task<Guid> SendAsync(SetupResultDto setup, AddSubmittedExpense command) => (await SendResultAsync(setup, command)).Value;

    private async Task<Result<Guid>> SendResultAsync(SetupResultDto setup, AddSubmittedExpense command)
    {
        using var actor = CurrentUserScope.Begin(new ImpersonatedUser(setup.AdminUserId, setup.TenantId));
        await using var scope = app.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(command, Ct);
    }
}
