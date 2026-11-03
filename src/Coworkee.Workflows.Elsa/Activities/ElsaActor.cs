using Coworkee.Core.Security;
using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Elsa.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows.Elsa.Activities;

/// <summary>A scope that acts as a user of the app, in the user's organisation; disposing ends both.</summary>
internal sealed class ElsaActor(AsyncServiceScope scope, IDisposable user) : IAsyncDisposable
{
    public IServiceProvider Services => scope.ServiceProvider;

    /// <summary>
    /// <paramref name="userId"/> or, when empty, the user whose action started the workflow; without either, nobody in the organisation the workflow
    /// started in (enough to notify, not to pass permission checks).
    /// </summary>
    public static async Task<ElsaActor> BeginAsync(ActivityExecutionContext context, Guid? userId)
    {
        var scopes = context.GetRequiredService<IServiceScopeFactory>();
        var id = userId ?? ElsaValues.Guid(context, ElsaInputs.UserId);
        var tenantId = id is { } user
            ? await TenantOfAsync(scopes, user, context.CancellationToken)
            : ElsaValues.Guid(context, ElsaInputs.TenantId) ?? throw new InvalidOperationException("Set Run as: the workflow was not started in an organisation.");
        // acting as the user who started the workflow keeps who was signed in as him; a "Run as" user acts on his own
        var impersonator = userId is null ? ElsaValues.Guid(context, ElsaInputs.ImpersonatorId) : null;
        var actor = CurrentUserScope.Begin(new ImpersonatedUser(id, tenantId, impersonator));
        return new ElsaActor(scopes.CreateAsyncScope(), actor);
    }

    private static async Task<Guid> TenantOfAsync(IServiceScopeFactory scopes, Guid userId, CancellationToken cancellationToken)
    {
        using var nobody = CurrentUserScope.Begin(new ImpersonatedUser(null, null));
        await using var lookup = scopes.CreateAsyncScope();
        return await lookup.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<User>().IgnoreQueryFilters()
            .Where(u => u.Id == userId && u.IsActive).Select(u => (Guid?)u.TenantId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"There is no active user {userId}.");
    }

    public Task SaveAsync(CancellationToken cancellationToken) => Services.GetRequiredService<CoworkeeDbContext>().SaveChangesAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        user.Dispose();
    }
}
