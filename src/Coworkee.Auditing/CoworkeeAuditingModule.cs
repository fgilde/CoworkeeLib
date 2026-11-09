using Coworkee.Application;
using Coworkee.Application.Authorization;
using Coworkee.Application.Messaging;
using Coworkee.Application.Retention;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Auditing.Requests;
using Coworkee.Contracts.Auditing;
using Coworkee.Core.Modularity;
using Coworkee.Infrastructure.Auditing;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing;

/// <summary>The audit log, entity history, the security log (written by every host, see <c>ISecurityLog</c>) and the request log of this host's API.</summary>
[DependsOn(typeof(CoworkeeApplicationModule), typeof(CoworkeeSettingsModule))]
public sealed class CoworkeeAuditingModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        context.Services.AddMessagingFromAssembly(typeof(CoworkeeAuditingModule).Assembly);
        context.Services.AddSingleton<IPermissionDefinitionContributor, AuditPermissionDefinitions>();
        context.Services.AddScoped<Application.Privacy.IPersonalDataContributor, AuditPersonalData>();
        context.Services.AddODataEntity<Coworkee.Infrastructure.Auditing.AuditEntry>("AuditEntries", Contracts.Auditing.AuditPermissions.View);
        context.Services.AddScoped<IODataEntityFilter<Coworkee.Infrastructure.Auditing.AuditEntry>, OData.AuditEntryODataFilter>();

        context.Services.AddODataEntity<SecurityEvent>("SecurityEvents", AuditPermissions.SecurityLog);
        context.Services.AddScoped<IODataEntityFilter<SecurityEvent>, OData.SecurityEventODataFilter>();

        // the list reads no headers and bodies; one request with them comes from /api/v1/audit/requests/{id}
        context.Services.AddSingleton<IModelContributor, RequestLogModel>();
        context.Services.AddSingleton<ISettingDefinitionContributor, RequestLogSettingDefinitions>();
        context.Services.AddODataEntity<RequestLogEntry>("RequestLog", AuditPermissions.Requests,
            o => o.Hide(e => e.RequestHeaders, e => e.ResponseHeaders, e => e.RequestBody, e => e.ResponseBody));
        context.Services.AddScoped<IODataEntityFilter<RequestLogEntry>, OData.RequestLogODataFilter>();
        context.Services.AddSingleton<RequestLogWriter>();
        context.Services.AddHostedService(provider => provider.GetRequiredService<RequestLogWriter>());
        context.Services.AddRequestMiddleware<RequestTypeCapture>();
        context.Services.AddRetentionTarget<RequestLogRetention>();
        context.Services.AddScoped<Application.Privacy.IPersonalDataContributor, RequestLogPersonalData>();
    }

    public void ConfigureApplication(WebApplication app)
    {
        app.Use(RequestLogMiddleware.InvokeAsync);

        var audit = app.MapCoworkeeApi("/api/v1/audit").WithTags("Audit").RequireAuthorization();
        audit.MapGet("/", ([AsParameters] AuditQuery query, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetAuditEntries(query), ct).ToHttpResult());
        audit.MapGet("/requests/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetRequestLogEntry(id), ct).ToHttpResult());
        audit.MapGet("/trace-viewer", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetTraceViewer(), ct).ToHttpResult());

        var versions = app.MapCoworkeeApi("/api/v1/versions").WithTags("Versions").RequireAuthorization();
        versions.MapGet("/{type}/{id:guid}", (string type, Guid id, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetEntityVersions(type, id), ct).ToHttpResult());
        versions.MapGet("/{type}/{id:guid}/{revision:int}", (string type, Guid id, int revision, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GetEntityVersion(type, id, revision), ct).ToHttpResult());
        versions.MapPost("/{type}/{id:guid}/{revision:int}/restore", (string type, Guid id, int revision, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new RestoreEntityVersion(type, id, revision), ct).ToHttpResult());

        var history = app.MapCoworkeeApi("/api/v1/history").WithTags("History").RequireAuthorization();
        history.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new History.GetEntityHistorySets(), ct).ToHttpResult());
        history.MapGet("/{entitySet}/deleted", (string entitySet, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new History.GetDeletedRecords(entitySet), ct).ToHttpResult());
        history.MapGet("/{entitySet}/{id:guid}", (string entitySet, Guid id, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new History.GetEntityHistory(entitySet, id), ct).ToHttpResult());
        history.MapPost("/{entitySet}/{id:guid}/preview", (string entitySet, Guid id, EntityHistoryRestoreRequest request, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new History.PreviewEntityRestore(entitySet, id, request), ct).ToHttpResult());
        history.MapPost("/{entitySet}/{id:guid}/restore", (string entitySet, Guid id, EntityHistoryRestoreRequest request, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new History.RestoreEntity(entitySet, id, request), ct).ToHttpResult());
    }
}
