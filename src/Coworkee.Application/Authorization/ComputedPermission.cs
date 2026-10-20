using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Authorization;

/// <summary>A permission granted from data instead of a role ("leads someone"); evaluated once per request and user, after the role grants.</summary>
public interface IComputedPermission
{
    /// <summary>A defined permission (it implies what its definition implies).</summary>
    string Permission { get; }

    Task<bool> IsGrantedAsync(ComputedPermissionContext context, CancellationToken cancellationToken);
}

/// <param name="Granted">What the roles and grants already give the user, e.g. to grant also to holders of an HR permission.</param>
public sealed record ComputedPermissionContext(Guid UserId, Guid? TenantId, IReadOnlyCollection<string> Granted, IServiceProvider Services);

public static class ComputedPermissionExtensions
{
    /// <summary>Grants <paramref name="permission"/> to every user for whom <paramref name="isGranted"/> answers true; query the database directly, never through the permission checker.</summary>
    public static IServiceCollection AddComputedPermission(
        this IServiceCollection services, string permission, Func<ComputedPermissionContext, CancellationToken, Task<bool>> isGranted) =>
        services.AddSingleton<IComputedPermission>(new DelegateComputedPermission(permission, isGranted));

    private sealed class DelegateComputedPermission(string permission, Func<ComputedPermissionContext, CancellationToken, Task<bool>> isGranted) : IComputedPermission
    {
        public string Permission => permission;

        public Task<bool> IsGrantedAsync(ComputedPermissionContext context, CancellationToken cancellationToken) => isGranted(context, cancellationToken);
    }
}
