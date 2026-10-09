using Coworkee.Account.Email;
using Coworkee.Account.Users;
using Coworkee.Application.Messaging;
using Coworkee.AspNetCore.Http;
using Coworkee.AspNetCore.RateLimiting;
using Coworkee.Contracts.Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Account;

internal static class AccountEndpoints
{
    public static void Map(WebApplication app)
    {
        var api = app.MapCoworkeeApi("/api/v1/identity").WithTags("Identity").RequireAuthorization();
        api.MapPost("/users/{id:guid}/password-reset", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new SendPasswordReset(id), ct).ToHttpResult());
        api.MapPost("/users/{id:guid}/invitation", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new SendInvitation(id), ct).ToHttpResult());
        api.MapPut("/users/{id:guid}/email", (Guid id, SetUserEmailRequest body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetUserEmail(id, body.Email, body.Confirmed), ct).ToHttpResult());
        api.MapGet("/users/{id:guid}/language", (Guid id, IDispatcher d, CancellationToken ct) => d.SendAsync(new GetUserLanguage(id), ct).ToHttpResult());
        api.MapPut("/users/{id:guid}/language", (Guid id, UserLanguageDto body, IDispatcher d, CancellationToken ct) =>
            d.SendAsync(new SetUserLanguage(id, body.Culture), ct).ToHttpResult());
        api.MapPost("/me/email", (ChangeEmailRequest body, IDispatcher d, CancellationToken ct) =>
                d.SendAsync(new ChangeMyEmail(body.NewEmail, body.CurrentPassword), ct).ToHttpResult())
            .RequireCoworkeeRateLimit(CoworkeeRateLimitOptions.Auth);
    }
}
