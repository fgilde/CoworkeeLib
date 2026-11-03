using Coworkee.Core.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Coworkee.Workflows;

/// <summary>The user a query runs for, read by EF when it executes the query (not when it is bound).</summary>
internal sealed class WorkflowUser(IHttpContextAccessor http)
{
    public Guid? Id => CurrentUserScope.Current is { } ambient ? ambient.UserId : http.HttpContext?.RequestServices.GetService<ICurrentUser>()?.UserId;
}
