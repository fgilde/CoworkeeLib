using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

public sealed partial class AuthClientSeeder(IServiceScopeFactory scopes, IOptions<AuthServerOptions> options, ILogger<AuthClientSeeder> logger) : IHostedService
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        foreach (var client in options.Value.Clients)
        {
            var descriptor = Describe(client);
            if (await manager.FindByClientIdAsync(client.ClientId, cancellationToken) is { } existing)
            {
                await manager.UpdateAsync(existing, descriptor, cancellationToken);
            }
            else
            {
                await manager.CreateAsync(descriptor, cancellationToken);
            }
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SeedAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSeedFailed(exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static OpenIddictApplicationDescriptor Describe(AuthClientOptions client)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = client.ClientId,
            DisplayName = client.DisplayName ?? client.ClientId,
            ClientSecret = client.ClientSecret,
            ClientType = string.IsNullOrEmpty(client.ClientSecret) ? ClientTypes.Public : ClientTypes.Confidential,
            ConsentType = ConsentTypes.Implicit,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.EndSession,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Scopes.Email,
                Permissions.Scopes.Profile,
                Permissions.Scopes.Roles,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };

        foreach (var scope in client.Scopes)
        {
            descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
        }

        foreach (var uri in client.RedirectUris)
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }

        foreach (var uri in client.PostLogoutRedirectUris)
        {
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));
        }

        return descriptor;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seeding OpenID Connect clients failed")]
    private partial void LogSeedFailed(Exception exception);
}
