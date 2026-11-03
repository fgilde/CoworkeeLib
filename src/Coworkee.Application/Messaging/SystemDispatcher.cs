using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

internal sealed class SystemDispatcher(IServiceScopeFactory scopes, ICurrentUser user) : ISystemDispatcher
{
    public async Task<TResult> SendAsync<TResult>(IRequest<TResult> request, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        using var actor = CurrentUserScope.BeginSystem(user.TenantId, [.. permissions]);
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(request, cancellationToken);
    }
}
