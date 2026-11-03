using Coworkee.Identity.Domain;
using Coworkee.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Identity.Setup;

public sealed class SystemStateCache(IServiceScopeFactory scopes)
{
    private volatile bool _initialized;

    public async Task<bool> IsInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return true;
        }

        await using var scope = scopes.CreateAsyncScope();
        _initialized = await scope.ServiceProvider.GetRequiredService<CoworkeeDbContext>().Set<SystemState>()
            .AnyAsync(s => s.IsInitialized, cancellationToken);
        return _initialized;
    }

    public void Reset() => _initialized = false;
}
