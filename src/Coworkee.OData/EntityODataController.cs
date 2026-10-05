using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Web.Controller;

namespace Coworkee.OData;

[Authorize]
[ODataEntityPermission]
[ODataQueryError]
public class EntityODataController<TEntity> : GenericODataController<TEntity>
    where TEntity : class
{
    protected override IQueryable<TEntity> Queryable() =>
        HttpContext.RequestServices.GetRequiredService<CoworkeeDbContext>().Set<TEntity>().AsNoTracking();
}
