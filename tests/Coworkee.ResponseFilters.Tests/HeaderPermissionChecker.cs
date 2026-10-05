using Coworkee.Application.Authorization;
using Microsoft.AspNetCore.Http;

namespace Coworkee.ResponseFilters.Tests;

internal sealed class HeaderPermissionChecker(IHttpContextAccessor http) : IPermissionChecker
{
    public Task<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken) =>
        Task.FromResult(http.HttpContext?.Request.Headers["X-Permission"].ToString() == permission);

    public Task<bool> IsGrantedAsync(string permission, string resourceType, Guid resourceId, CancellationToken cancellationToken) => IsGrantedAsync(permission, cancellationToken);

    public Task<IReadOnlyCollection<string>> GetGrantedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyCollection<Guid>> GetGrantedResourcesAsync(string permission, string resourceType, CancellationToken cancellationToken) => throw new NotSupportedException();
}
