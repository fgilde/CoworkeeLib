using Elsa.Studio.Authentication.Abstractions.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Client;

namespace Coworkee.Workflows.Elsa.Studio;

/// <summary>The SignalR connections of the designer (live view of running workflows) carry the user's access token like its HTTP calls.</summary>
internal sealed class AccessTokenConnectionConfigurator(IHttpContextAccessor accessor) : IHttpConnectionOptionsConfigurator
{
    public async Task ConfigureAsync(HttpConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var token = accessor.HttpContext is { } context ? await context.GetTokenAsync("access_token") : null;
        options.AccessTokenProvider = () => Task.FromResult(token);
    }
}
