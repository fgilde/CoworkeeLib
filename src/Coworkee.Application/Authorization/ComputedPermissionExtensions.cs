using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Authorization;

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
