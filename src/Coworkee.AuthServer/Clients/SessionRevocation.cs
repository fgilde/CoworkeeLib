using Coworkee.Identity.Domain;
using Coworkee.Identity.Users;
using OpenIddict.Abstractions;

namespace Coworkee.AuthServer.Clients;

/// <summary>Revokes everything the auth server issued to a user whose sessions an administrator ended.</summary>
internal sealed class SessionRevocation(IOpenIddictTokenManager tokens, IOpenIddictAuthorizationManager authorizations) : IUserSessionListener
{
    public async Task SessionsEndedAsync(User user, CancellationToken cancellationToken)
    {
        await tokens.RevokeBySubjectAsync(user.Id.ToString(), cancellationToken);
        await authorizations.RevokeBySubjectAsync(user.Id.ToString(), cancellationToken);
    }
}
