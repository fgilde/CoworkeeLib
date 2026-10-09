using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.Contracts.Identity;
using Coworkee.Identity.Users.Privacy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Coworkee.Identity;

internal static partial class IdentityEndpoints
{
    static partial void MapPrivacy(RouteGroupBuilder api)
    {
        api.MapGet("/me/personal-data", (IDispatcher d, CancellationToken ct) => d.SendAsync(new ExportMyPersonalData(), ct).ToHttpResult());
        api.MapPost("/me/delete", (DeleteAccountRequest body, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteMyAccount(body.Email), ct).ToHttpResult());
        api.MapDelete("/users/{id:guid}", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new DeleteUser(id), ct).ToHttpResult());
    }
}
