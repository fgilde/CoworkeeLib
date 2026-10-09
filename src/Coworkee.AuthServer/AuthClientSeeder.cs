using System.Text.Json;
using Coworkee.Contracts.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Coworkee.AuthServer;

/// <summary>
/// Writes the clients and API scopes of the configuration to the OpenIddict stores at startup. They carry <see cref="ManagedProperty"/>:
/// the admin pages show them read-only, the configuration stays their source, and those it no longer names are removed.
/// </summary>
public sealed partial class AuthClientSeeder(IServiceScopeFactory scopes, IOptions<AuthServerOptions> options, ILogger<AuthClientSeeder> logger) : IHostedService
{
    public const string ManagedProperty = "coworkee_managed";

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

        var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var (name, resource) in options.Value.ApiScopes)
        {
            var descriptor = new OpenIddictScopeDescriptor { Name = name, DisplayName = name, Resources = { resource } };
            descriptor.Properties[ManagedProperty] = JsonSerializer.SerializeToElement(true);
            if (await scopeManager.FindByNameAsync(name, cancellationToken) is { } existing)
            {
                await scopeManager.UpdateAsync(existing, descriptor, cancellationToken);
            }
            else
            {
                await scopeManager.CreateAsync(descriptor, cancellationToken);
            }
        }

        await RemoveStaleAsync(options.Value.Clients.Select(c => c.ClientId), manager.ListAsync, manager.GetClientIdAsync, manager.GetPropertiesAsync, manager.DeleteAsync, cancellationToken);
        await RemoveStaleAsync(options.Value.ApiScopes.Keys, scopeManager.ListAsync, scopeManager.GetNameAsync, scopeManager.GetPropertiesAsync, scopeManager.DeleteAsync, cancellationToken);
    }

    // only what the configuration wrote before: clients and scopes added in the admin pages stay
    private static async Task RemoveStaleAsync(
        IEnumerable<string> configured,
        Func<int?, int?, CancellationToken, IAsyncEnumerable<object>> list,
        Func<object, CancellationToken, ValueTask<string?>> name,
        Func<object, CancellationToken, ValueTask<System.Collections.Immutable.ImmutableDictionary<string, JsonElement>>> properties,
        Func<object, CancellationToken, ValueTask> delete,
        CancellationToken cancellationToken)
    {
        var keep = configured.ToHashSet(StringComparer.Ordinal);
        var stale = new List<object>();
        await foreach (var item in list(null, null, cancellationToken))
        {
            if ((await properties(item, cancellationToken)).ContainsKey(ManagedProperty) && !keep.Contains(await name(item, cancellationToken) ?? string.Empty))
            {
                stale.Add(item);
            }
        }

        foreach (var item in stale)
        {
            await delete(item, cancellationToken);
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
        descriptor.Properties[ManagedProperty] = JsonSerializer.SerializeToElement(true);

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
