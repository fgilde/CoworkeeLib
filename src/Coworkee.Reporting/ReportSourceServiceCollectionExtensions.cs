using Coworkee.Reporting.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Reporting;

public static class ReportSourceServiceCollectionExtensions
{
    /// <summary>
    /// A data source for reports that the app computes, e.g. headcount per month: rows of <typeparamref name="TRow"/>, whose simple properties become
    /// the fields widgets group, aggregate and filter by. Return an IQueryable of the database to aggregate there, or a list to aggregate in memory.
    /// </summary>
    /// <param name="name">The name widgets use like an entity set; must not be the name of an OData set.</param>
    /// <param name="permission">The permission a user needs to see and query the source; null for every report reader.</param>
    public static IServiceCollection AddReportSource<TRow>(
        this IServiceCollection services, string name, string label, string? permission, Func<ReportSourceContext, CancellationToken, Task<IEnumerable<TRow>>> rows)
        where TRow : class
    {
        services.AddSingleton<ReportSourceRegistration>(new ReportSourceRegistration<TRow>(name, label, permission, rows));
        return services;
    }

    /// <inheritdoc cref="AddReportSource{TRow}(IServiceCollection, string, string, string?, Func{ReportSourceContext, CancellationToken, Task{IEnumerable{TRow}}})"/>
    public static IServiceCollection AddReportSource<TRow>(this IServiceCollection services, string name, string label, string? permission, Func<ReportSourceContext, IEnumerable<TRow>> rows)
        where TRow : class =>
        services.AddReportSource<TRow>(name, label, permission, (context, _) => Task.FromResult(rows(context)));
}
