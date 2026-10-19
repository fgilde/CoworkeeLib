using Coworkee.Core.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Application.Messaging;

/// <summary>
/// Sends a request as the system, for work a module does on its own behalf whatever the caller may do (e.g. keeping the group of a team in step).
/// The request runs in a scope of its own, in the caller's organisation, signed in without a user and with exactly the given permissions;
/// the caller's pending changes are not saved with it and audit entries show "system".
/// </summary>
public interface ISystemDispatcher
{
    Task<TResult> SendAsync<TResult>(IRequest<TResult> request, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default);
}

internal sealed class SystemDispatcher(IServiceScopeFactory scopes, ICurrentUser user) : ISystemDispatcher
{
    public async Task<TResult> SendAsync<TResult>(IRequest<TResult> request, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default)
    {
        using var actor = CurrentUserScope.BeginSystem(user.TenantId, [.. permissions]);
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(request, cancellationToken);
    }
}
