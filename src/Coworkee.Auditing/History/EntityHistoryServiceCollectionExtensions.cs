using Coworkee.Application.Messaging;
using Coworkee.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Auditing.History;

public static class EntityHistoryServiceCollectionExtensions
{
    /// <summary>
    /// Opts an OData set into the record history. <paramref name="toCommand"/> turns a record into the app's update command, e.g.
    /// p =&gt; new AddEditProductCommand(p.Id, new AddEditProductRequest { Name = p.Name, ... }); a restore fills the record with the
    /// earlier values and sends that command, so its permissions, validation and audit apply.
    /// </summary>
    public static IServiceCollection AddEntityHistory<TEntity, TResult>(
        this IServiceCollection services, string entitySet, string restorePermission, Func<TEntity, IRequest<TResult>> toCommand)
        where TEntity : class
        where TResult : Result
    {
        Coworkee.OData.ODataEntityAccess.RequireGuidId<TEntity>("Entity history");
        return services.AddSingleton<EntityHistoryRegistration>(new EntityHistoryRegistration<TEntity, TResult>(entitySet, restorePermission, toCommand));
    }
}
