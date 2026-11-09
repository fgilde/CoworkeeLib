using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer.Clients;

/// <summary>Revokes what the auth server issued to a user whose sessions ended: all of it, or the authorizations of one session with their tokens.</summary>
internal sealed class SessionRevocation(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations) : IUserSessionListener
{
    public async Task SessionsEndedAsync(User user, CancellationToken cancellationToken)
    {
        await tokens.RevokeBySubjectAsync(user.Id.ToString(), cancellationToken);
        await authorizations.RevokeBySubjectAsync(user.Id.ToString(), cancellationToken);
    }

    // a permanent consent is shared by the user's sessions and stays; the token endpoint refuses the ended session's refresh tokens
    public async Task SessionEndedAsync(Guid userId, string sessionId, CancellationToken cancellationToken)
    {
        foreach (var authorization in await authorizations.FindBySubjectAsync(userId.ToString(), cancellationToken).ToListAsync(cancellationToken))
        {
            var properties = await authorizations.GetPropertiesAsync(authorization, cancellationToken);
            if (await authorizations.HasStatusAsync(authorization, Statuses.Valid, cancellationToken)
                && (AuthSessions.Names(properties, AuthSessions.Property, sessionId) || AuthSessions.Names(properties, AuthImpersonation.SessionProperty, sessionId)))
            {
                await tokens.RevokeByAuthorizationIdAsync((await authorizations.GetIdAsync(authorization, cancellationToken))!, cancellationToken);
                await authorizations.TryRevokeAsync(authorization, cancellationToken);
            }
        }
    }
}
