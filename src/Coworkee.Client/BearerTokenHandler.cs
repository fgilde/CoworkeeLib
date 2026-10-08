using System.Net.Http.Headers;

namespace Coworkee.Client;

/// <summary>Adds the bearer token from <paramref name="token"/> to every request.</summary>
public sealed class BearerTokenHandler(Func<CancellationToken, Task<string>> token) : DelegatingHandler(new HttpClientHandler())
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await token(cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}
