using Coworkee.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nextended.Web.Controller;

namespace Coworkee.OData;

[Authorize]
[ODataEntityPermission]
[ODataQueryError]
public class EntityODataController<TEntity> : GenericODataController<TEntity>, IAsyncActionFilter
    where TEntity : class
{
    private IQueryable<TEntity>? _visible;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var query = HttpContext.RequestServices.GetRequiredService<CoworkeeDbContext>().Set<TEntity>().AsNoTracking();
        foreach (var filter in HttpContext.RequestServices.GetServices<IODataEntityFilter<TEntity>>())
        {
            query = await filter.ApplyAsync(query, HttpContext.RequestAborted);
        }

        _visible = query;
        await next();
    }

    protected override IQueryable<TEntity> Queryable() => _visible ?? throw new InvalidOperationException("The query is prepared before the action runs.");
}
