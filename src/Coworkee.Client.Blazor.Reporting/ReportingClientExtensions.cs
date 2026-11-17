using Coworkee.Client.Blazor.Reporting.Api;
using Coworkee.Client.Blazor.Reporting.State;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Client.Blazor.Reporting;

public static class ReportingClientExtensions
{
    /// <summary>
    /// Reports and dashboards: the pages under /reports (<see cref="ReportingOptions.BasePath"/>; keys reports, report, report-new, report-subscriptions,
    /// report-data), their navigation entry and the API client.
    /// </summary>
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
        services.AddSingleton(new Navigation.CoworkeePage("reports", typeof(Pages.Reports), options.Path()));
        services.AddSingleton(new Navigation.CoworkeePage("report", typeof(Pages.ReportPage), options.Path("{Id:guid}")));
        services.AddSingleton(new Navigation.CoworkeePage("report-new", typeof(Pages.ReportPage), options.Path("new")));
        services.AddSingleton(new Navigation.CoworkeePage("report-subscriptions", typeof(Pages.ReportSubscriptions), options.Path("subscriptions")));
        services.AddSingleton(new Navigation.CoworkeePage("report-data", typeof(Pages.ReportData), options.Path("data/{EntitySet}")));
        return services;
    }
}
