using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.BackgroundJobs;
using Coworkee.Contracts.Workflows;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.Notifications;
using Coworkee.Realtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Workflows;

/// <summary>States, transitions, approvals with deadlines and a history for the entity types an app declares with AddCoworkeeWorkflows.</summary>
[DependsOn(typeof(Coworkee.OData.CoworkeeODataModule), typeof(CoworkeeRealtimeModule), typeof(CoworkeeNotificationsModule), typeof(CoworkeeIdentityModule))]
public sealed class CoworkeeWorkflowsModule : CoworkeeModule, IWebModule
{
    public const string Section = "Coworkee:Workflows";

    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddCoworkeeWorkflows(_ => { });
        services.AddMessagingFromAssembly(typeof(CoworkeeWorkflowsModule).Assembly);
        services.AddSingleton<IModelContributor, WorkflowModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, WorkflowPermissionDefinitions>();
        services.AddScoped<WorkflowEngine>();
        services.AddScoped<ApproverDirectory>();
        // workflows find approvers and notified users by role name: those roles keep their names
        services.AddOptions<Identity.Users.IdentityAdministrationOptions>().PostConfigure<WorkflowOptions>((o, w) =>
        {
            foreach (var group in w.Workflows.Values.SelectMany(x => x.RoleNames.Select(role => (Role: role, x.EntityType))).GroupBy(r => r.Role, StringComparer.OrdinalIgnoreCase))
            {
                o.FixedRoleNames[group.Key] = $"The workflow of {string.Join(", ", group.Select(r => r.EntityType))}";
            }
        });
        services.AddScoped<Application.Privacy.IPersonalDataContributor, WorkflowPersonalData>();
        services.AddHttpContextAccessor();
        services.AddSingleton<WorkflowUser>();
        services.AddSingleton<Coworkee.OData.Computed.IODataComputedPropertySource, WorkflowODataProperties>();
        services.AddScoped<IRealtimeTopicAuthorizer, WorkflowTopicAuthorizer>();
        services.AddRecurringJob<WorkflowDeadlineJob>(WorkflowDeadlineJob.Id,
            context.Configuration[$"{Section}:DeadlineCron"] is { Length: > 0 } cron ? cron : "*/5 * * * *");
        services.TryAddSingleton(TimeProvider.System);
    }

    public void ConfigureApplication(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/workflows").WithTags("Workflows").RequireAuthorization();
        api.MapGet("/approvals", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetMyApprovalsQuery(), ct).ToHttpResult());
        api.MapPost("/approvals/{id:guid}", (Guid id, DecisionRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new DecideApprovalCommand(id, body.Approve, body.Comment), ct).ToHttpResult());
        api.MapPost("/{entityType}/states", (string entityType, WorkflowStatesRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetWorkflowStatesQuery(entityType, body.Ids), ct).ToHttpResult());
        api.MapGet("/{entityType}/{entityId:guid}", (string entityType, Guid entityId, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetWorkflowQuery(entityType, entityId), ct).ToHttpResult());
        api.MapPost("/{entityType}/{entityId:guid}/{transition}", (string entityType, Guid entityId, string transition, TransitionRequest? body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RunTransitionCommand(entityType, entityId, transition, body?.Comment), ct).ToHttpResult());
    }
}
