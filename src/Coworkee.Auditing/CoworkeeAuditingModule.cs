using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing;

[DependsOn(typeof(CoworkeeApplicationModule))]
public sealed class CoworkeeAuditingModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(CoworkeeAuditingModule).Assembly);
        context.Services.AddSingleton<IPermissionDefinitionContributor, AuditPermissionDefinitions>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var audit = app.MapGroup("/api/v1/audit").WithTags("Audit").RequireAuthorization();
        audit.MapGet("/", ([AsParameters] AuditQuery query, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAuditEntries(query), ct).ToHttpResult());

        var versions = app.MapGroup("/api/v1/versions").WithTags("Versions").RequireAuthorization();
        versions.MapGet("/{type}/{id:guid}", (string type, Guid id, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetEntityVersions(type, id), ct).ToHttpResult());
        versions.MapGet("/{type}/{id:guid}/{revision:int}", (string type, Guid id, int revision, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetEntityVersion(type, id, revision), ct).ToHttpResult());
        versions.MapPost("/{type}/{id:guid}/{revision:int}/restore", (string type, Guid id, int revision, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RestoreEntityVersion(type, id, revision), ct).ToHttpResult());
    }
}

internal sealed class AuditPermissionDefinitions : IPermissionDefinitionContributor
{
    public void Define(PermissionDefinitionContext context) =>
        context.Group(AuditPermissions.GroupName, "Audit").Add(AuditPermissions.View, "View audit log");
}
