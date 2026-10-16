using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Coworkee.Workflows.Elsa.Studio;

/// <summary>Sends the signed-in user's access token (kept on the server by the BFF session) to the Elsa API.</summary>
// ponytail: a circuit keeps the token of its connection; after the token expires the designer needs a reload, a refresh per call if that bites
internal sealed class AccessTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (accessor.HttpContext is { } context && await context.GetTokenAsync("access_token") is { } token)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
