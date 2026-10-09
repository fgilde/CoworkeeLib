using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Modularity;
using Coworkee.OData;
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
        context.Services.AddScoped<Application.Privacy.IPersonalDataContributor, AuditPersonalData>();
        context.Services.AddODataEntity<Coworkee.Infrastructure.Auditing.AuditEntry>("AuditEntries", Contracts.Auditing.AuditPermissions.View);
        context.Services.AddScoped<IODataEntityFilter<Coworkee.Infrastructure.Auditing.AuditEntry>, OData.AuditEntryODataFilter>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        var audit = app.MapCoworkeeApi("/api/v1/audit").WithTags("Audit").RequireAuthorization();
        audit.MapGet("/", ([AsParameters] AuditQuery query, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAuditEntries(query), ct).ToHttpResult());

        var versions = app.MapCoworkeeApi("/api/v1/versions").WithTags("Versions").RequireAuthorization();
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
