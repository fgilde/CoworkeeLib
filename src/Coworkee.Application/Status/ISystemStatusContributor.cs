using Coworkee.Contracts.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Coworkee.Application.Status;

/// <summary>A section of the admin page System status; sections run one after the other in a scope of the request. A throwing one shows as unavailable.</summary>
public interface ISystemStatusContributor
{
    Task<SystemStatusSection> GetAsync(CancellationToken cancellationToken);
}

public static class SystemStatusExtensions
{
    public static IServiceCollection AddSystemStatus<T>(this IServiceCollection services) where T : class, ISystemStatusContributor
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISystemStatusContributor, T>());
        return services;
    }

    /// <summary>Bytes as "1.5 MB".</summary>
    public static string Bytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{value:0.#} {units[unit]}");
    }
}
