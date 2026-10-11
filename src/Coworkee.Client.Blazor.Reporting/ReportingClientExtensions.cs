using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.State;
using Coworkee.Contracts.Reporting;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Reporting;

public static class ReportingClientExtensions
{
    /// <summary>Reports and dashboards: the pages under /reports, their navigation entry and the API client; add the assembly to the router.</summary>
    public static IServiceCollection AddCoworkeeReporting(this IServiceCollection services, Uri baseAddress, Action<ReportingOptions>? configure = null)
    {
        var options = new ReportingOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddHttpClient<IReportsApi, ReportsApi>(client => client.BaseAddress = baseAddress);
        services.AddScoped<ReportTheme>();
        services.AddSingleton<INavigationContributor, ReportingNavigation>();
        return services;
    }
}

internal sealed class ReportingNavigation : INavigationContributor
{
    public IEnumerable<CoworkeeNavItem> Items => [new("Reports", "/reports", MudBlazor.Icons.Material.Outlined.SpaceDashboard, ReportPermissions.View, Order: -7)];
}
