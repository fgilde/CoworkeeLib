using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Reporting;

public static class ReportingClientExtensions
{
    /// <summary>Reports and dashboards: the pages under /reports (keys reports, report, report-new, report-data), their navigation entry and the API client.</summary>
    public static IServiceCollection AddCoworkeeReporting(this IServiceCollection services, Uri baseAddress, Action<ReportingOptions>? configure = null)
    {
        var options = new ReportingOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddHttpClient<IReportsApi, ReportsApi>(client => client.BaseAddress = baseAddress);
        services.AddScoped<ReportTheme>();
        services.AddScoped<ReportViewChoices>();
        services.AddScoped<ReportDrafts>();
        services.AddSingleton<INavigationContributor, ReportingNavigation>();
        services.AddSingleton(new Navigation.CoworkeePage("reports", typeof(Pages.Reports), "reports"));
        services.AddSingleton(new Navigation.CoworkeePage("report", typeof(Pages.ReportPage), "reports/{Id:guid}"));
        services.AddSingleton(new Navigation.CoworkeePage("report-new", typeof(Pages.ReportPage), "reports/new"));
        services.AddSingleton(new Navigation.CoworkeePage("report-data", typeof(Pages.ReportData), "reports/data/{EntitySet}"));
        return services;
    }
}

internal sealed class ReportingNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items => [new("Reports", "/reports", MudBlazor.Icons.Material.Outlined.SpaceDashboard, ReportPermissions.View, Order: -7)];
}
