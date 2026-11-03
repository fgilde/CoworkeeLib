using Coworkee.Application;
using Coworkee.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Infrastructure.Persistence;

/// <summary>Saves the external contexts with changes first, then the app's context; there is no transaction across the databases.</summary>
internal sealed class CoworkeeUnitOfWork(IServiceProvider services, IUnitOfWork main) : IUnitOfWork
{
    // ponytail: the app's context only; external contexts in a transaction of their own count when a shared load reads them
    public bool InTransaction => main.InTransaction;

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var context in services.GetService<EntityDbContexts>()?.All(services) ?? [])
        {
            if (!context.ChangeTracker.HasChanges())
            {
                continue;
            }

            try
            {
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException exception)
            {
                throw new ConcurrencyConflictException("The entity was changed by someone else.", exception);
            }
        }

        await main.SaveChangesAsync(cancellationToken);
    }
}
