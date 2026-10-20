using Coworkee.Application.Authorization;
using Coworkee.BackgroundJobs;
using Coworkee.Application.GlobalSearch;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Reporting;
using Coworkee.Core.Modularity;
using Coworkee.Identity;
using Coworkee.Infrastructure.Persistence;
using Coworkee.OData;
using Coworkee.Realtime;
using Coworkee.Reporting.Querying;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Reporting;

/// <summary>Reports and dashboards over the app's OData entity sets and own sources (AddReportSource), aggregated on the server, shared with roles or the organisation.</summary>
[DependsOn(typeof(CoworkeeIdentityModule), typeof(CoworkeeRealtimeModule), typeof(CoworkeeODataModule), typeof(Mailing.CoworkeeMailingModule))]
public sealed class CoworkeeReportingModule : CoworkeeModule, IWebModule
{
    public override void ConfigureServices(ModuleServiceContext context)
    {
        var services = context.Services;
        services.AddMessagingFromAssembly(typeof(CoworkeeReportingModule).Assembly);
        services.AddSingleton<IModelContributor, ReportModelContributor>();
        services.AddSingleton<IPermissionDefinitionContributor, ReportPermissionDefinitions>();
        services.AddSingleton<ReportSchema>();
        services.AddScoped<ReportQueries>();
        services.AddScoped<ReportDefinitionCheck>();
        services.AddScoped<ReportAccess>();
        services.AddGlobalSearchProvider<ReportSearchProvider>();
        services.AddScoped<Subscriptions.ReportSubscriptionMail>();
        services.AddSingleton<Subscriptions.ReportMailLinks>();
        services.AddSingleton<Subscriptions.ReportUnsubscribe>();
        services.AddSingleton<Mailing.IMailTemplateContributor, Subscriptions.ReportMailTemplates>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddRecurringJob<Subscriptions.ReportSubscriptionJob>(Subscriptions.ReportSubscriptionJob.Id,
            context.Configuration["Coworkee:Reporting:SubscriptionCron"] is { Length: > 0 } cron ? cron : Subscriptions.ReportSubscriptionJob.DefaultCron);
    }

    public void ConfigureApplication(WebApplication app)
    {
        var reports = app.MapCoworkeeApi("/api/v1/reports").WithTags("Reports").RequireAuthorization();
        reports.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetReportsQuery(), ct).ToHttpResult());
        reports.MapPost("/", (SaveReportRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new CreateReportCommand(body), ct).ToHttpResult());
        reports.MapGet("/sources", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetReportSourcesQuery(), ct).ToHttpResult());
        reports.MapGet("/roles", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetReportRolesQuery(), ct).ToHttpResult());
        reports.MapPost("/query", (ReportQueryRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new RunReportQuery(body), ct).ToHttpResult());
        reports.MapGet("/ai", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetReportAiStatusQuery(), ct).ToHttpResult());
        reports.MapPost("/generate", (GenerateReportRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new GenerateReportDefinitionQuery(body.Prompt, body.Current), ct).ToHttpResult());
        reports.MapPost("/rows", (ReportRowsRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new ReadReportRowsQuery(body), ct).ToHttpResult());
        reports.MapPost("/sheet", async (ReportSheetRequest body, IPermissionChecker permissions, CancellationToken ct) =>
                await permissions.IsGrantedAsync(ReportPermissions.View, ct) ? Results.File(ReportSheet.Write(body), ReportSheet.ContentType, $"{body.Name}.xlsx") : Results.Forbid())
            .Produces(StatusCodes.Status200OK, contentType: ReportSheet.ContentType);
        reports.MapGet("/dashboard", (IDispatcher d, CancellationToken ct) => d.SendAsync(new GetDashboardReportQuery(), ct).ToHttpResult());
        reports.MapPut("/dashboard", (SetDashboardReportRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new SetMyDashboardReportCommand(body.ReportId), ct).ToHttpResult());
        reports.MapPut("/dashboard/organisation", (SetDashboardReportRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetOrganisationDashboardReportCommand(body.ReportId), ct).ToHttpResult());
        var subscriptions = reports.MapGroup("/subscriptions");
        subscriptions.MapGet("/", (IDispatcher d, CancellationToken ct) => d.SendAsync(new Subscriptions.GetMyReportSubscriptionsQuery(), ct).ToHttpResult());
        subscriptions.MapGet("/all", (IDispatcher d, CancellationToken ct) => d.SendAsync(new Subscriptions.GetAllReportSubscriptionsQuery(), ct).ToHttpResult());
        subscriptions.MapPost("/", (SaveReportSubscriptionRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new Subscriptions.CreateReportSubscriptionCommand(body), ct).ToHttpResult());
        subscriptions.MapPut("/{id:guid}", (Guid id, SaveReportSubscriptionRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new Subscriptions.UpdateReportSubscriptionCommand(id, body), ct).ToHttpResult());
        subscriptions.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new Subscriptions.DeleteReportSubscriptionCommand(id), ct).ToHttpResult());
        subscriptions.MapPost("/{id:guid}/send", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new Subscriptions.SendReportSubscriptionNowCommand(id), ct).ToHttpResult());
        subscriptions.MapGet("/unsubscribe", (HttpContext http, string? token, Subscriptions.ReportUnsubscribe unsubscribe, CancellationToken ct) => unsubscribe.ConfirmAsync(http, token, ct))
            .AllowAnonymous().ExcludeFromDescription();
        subscriptions.MapPost("/unsubscribe", (HttpContext http, Subscriptions.ReportUnsubscribe unsubscribe, CancellationToken ct) => unsubscribe.UnsubscribeAsync(http, ct))
            .AllowAnonymous().DisableAntiforgery().ExcludeFromDescription();
        reports.MapGet("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetReportQuery(id), ct).ToHttpResult());
        reports.MapPut("/{id:guid}", (Guid id, SaveReportRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new UpdateReportCommand(id, body), ct).ToHttpResult());
        reports.MapDelete("/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteReportCommand(id), ct).ToHttpResult());
        reports.MapPost("/{id:guid}/duplicate", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DuplicateReportCommand(id), ct).ToHttpResult());
        reports.MapPut("/{id:guid}/favorite", (Guid id, FavoriteReportRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetReportFavoriteCommand(id, body.Favorite), ct).ToHttpResult());
    }
}
